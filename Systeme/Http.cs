using System;
using System.Collections.Generic;
using Cosmos.System.Network.Config;
using Cosmos.System.Network.IPv4;
using Cosmos.System.Network.IPv4.TCP;
using Cosmos.System.Network.IPv4.UDP.DNS;

namespace FileSystemOS.Systeme
{
    /// <summary>
    /// Client HTTP minimal (HTTP/1.0, port 80) : DNS + TCP de Cosmos.
    /// Pas de HTTPS (Cosmos n'a pas de TLS) : les sites https passent par le
    /// lecteur simplifie FrogFind (voir Navigateur).
    /// </summary>
    public static class Http
    {
        public const int MaxBytes = 512 * 1024;
        private static int localPort = 40000;

        public static string Home = "http://frogfind.com/";

        public static string Encode(string s)
        {
            const string hex = "0123456789ABCDEF";
            string r = "";
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_' || c == '.' || c == '~') r += c;
                else if (c == ' ') r += '+';
                else { int b = c < 256 ? c : '?'; r += "%" + hex[b >> 4] + hex[b & 15]; }
            }
            return r;
        }

        /// <summary>Texte tape dans la barre d'adresse -> URL http utilisable.</summary>
        public static string Normalize(string input)
        {
            string s = input.Trim();
            if (s.Length == 0) return Home;
            if (s.StartsWith("https://")) return "http://frogfind.com/read.php?a=" + s;
            if (s.StartsWith("http://")) return s;
            if (s.IndexOf(' ') >= 0 || s.IndexOf('.') < 0) return "http://frogfind.com/?q=" + Encode(s);
            return "http://" + s;
        }

        /// <summary>Lien d'une page -> URL absolue (null si inutilisable : mailto, javascript, ancre).</summary>
        public static string Resolve(string baseUrl, string href)
        {
            href = href.Trim();
            if (href.Length == 0 || href[0] == '#') return null;
            string low = href.ToLower();
            if (low.StartsWith("mailto:") || low.StartsWith("javascript:") || low.StartsWith("tel:")) return null;
            if (low.StartsWith("http://") || low.StartsWith("https://")) return Normalize(href);
            if (href.StartsWith("//")) return "http:" + href;

            Split(baseUrl, out string host, out int port, out string path);
            string origin = "http://" + host + (port != 80 ? ":" + port : "");
            if (href[0] == '/') return origin + href;
            int q = path.IndexOf('?');
            string dir = q >= 0 ? path.Substring(0, q) : path;
            dir = dir.Substring(0, dir.LastIndexOf('/') + 1);
            return origin + dir + href;
        }

        private static void Split(string url, out string host, out int port, out string path)
        {
            string s = url.StartsWith("http://") ? url.Substring(7) : url;
            int slash = s.IndexOf('/');
            string hp = slash < 0 ? s : s.Substring(0, slash);
            path = slash < 0 ? "/" : s.Substring(slash);
            int colon = hp.IndexOf(':');
            port = 80;
            if (colon > 0) { int p = Utils.Fmt.ParseInt(hp.Substring(colon + 1)); if (p > 0) port = p; hp = hp.Substring(0, colon); }
            host = hp;
        }

        private static Address Lookup(string host)
        {
            bool numeric = true;
            for (int i = 0; i < host.Length; i++) if (!(host[i] == '.' || (host[i] >= '0' && host[i] <= '9'))) numeric = false;
            if (numeric) return Address.Parse(host);

            if (DNSConfig.DNSNameservers.Count == 0) throw new Exception("pas de serveur DNS (reseau non configure)");
            using (var dns = new DnsClient())
            {
                dns.Connect(DNSConfig.DNSNameservers[0]);
                dns.SendAsk(host);
                Address a = dns.Receive();
                if (a == null) throw new Exception("site introuvable : " + host);
                return a;
            }
        }

        /// <summary>Telecharge une page. Suit les redirections (5 maximum).</summary>
        public static byte[] Get(string url, out string finalUrl, out string contentType)
        {
            if (!Reseau.Connected) throw new Exception("pas de connexion reseau (" + Reseau.Status + ")");
            contentType = "";
            for (int hop = 0; hop < 5; hop++)
            {
                Split(url, out string host, out int port, out string path);
                byte[] raw = Request(Lookup(host), port, host, path);

                // En-tetes
                int end = -1;
                for (int i = 0; i + 3 < raw.Length; i++)
                    if (raw[i] == 13 && raw[i + 1] == 10 && raw[i + 2] == 13 && raw[i + 3] == 10) { end = i; break; }
                if (end < 0) throw new Exception("reponse HTTP invalide");
                char[] hc = new char[end];
                for (int i = 0; i < end; i++) hc[i] = (char)raw[i];
                string[] headers = new string(hc).Split('\n');

                string status = headers[0];
                int code = status.Length >= 12 ? Utils.Fmt.ParseInt(status.Substring(9, 3)) : 0;
                string location = null;
                for (int i = 1; i < headers.Length; i++)
                {
                    string h = headers[i].Trim();
                    string hl = h.ToLower();
                    if (hl.StartsWith("location:")) location = h.Substring(9).Trim();
                    if (hl.StartsWith("content-type:")) contentType = hl.Substring(13).Trim();
                }

                if (code >= 300 && code < 400 && location != null)
                {
                    url = Resolve(url, location);
                    if (url == null) throw new Exception("redirection invalide");
                    continue;
                }
                if (code >= 400) throw new Exception("erreur HTTP " + code);

                finalUrl = url;
                byte[] body = new byte[raw.Length - end - 4];
                for (int i = 0; i < body.Length; i++) body[i] = raw[end + 4 + i];
                return body;
            }
            throw new Exception("trop de redirections");
        }

        private static byte[] Request(Address ip, int port, string host, string path)
        {
            string req = "GET " + path + " HTTP/1.0\r\nHost: " + host +
                         "\r\nUser-Agent: FileSystemOS/" + Services.Version + " (Cosmos)" +
                         "\r\nAccept: text/html, text/plain\r\nConnection: close\r\n\r\n";
            byte[] rq = new byte[req.Length];
            for (int i = 0; i < req.Length; i++) rq[i] = (byte)req[i];

            var buf = new List<byte>();
            localPort++;
            if (localPort > 60000) localPort = 40000;
            using (var tcp = new TcpClient(localPort))
            {
                tcp.Connect(ip, port);
                tcp.Send(rq);
                var ep = new EndPoint(Address.Zero, 0);

                byte[] first = tcp.Receive(ref ep);
                if (first != null) buf.AddRange(first);

                // Suite de la reponse : on attend jusqu'a 3 s de silence
                int idle = 0;
                while (idle < 300 && buf.Count < MaxBytes)
                {
                    byte[] d = tcp.NonBlockingReceive(ref ep);
                    if (d == null || d.Length == 0) { idle++; Cosmos.HAL.Global.PIT.Wait(10); continue; }
                    buf.AddRange(d);
                    idle = 0;
                }
                tcp.Close();
            }
            return buf.ToArray();
        }
    }
}
