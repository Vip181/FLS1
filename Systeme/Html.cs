using System.Collections.Generic;

namespace FileSystemOS.Systeme
{
    public class WebSeg
    {
        public string Text;
        public int Link = -1;      // index dans la liste des liens, -1 = texte simple
        public bool Head;          // titre (h1..h6)
    }

    public class WebLine
    {
        public string Text = "";
        public readonly List<int> Starts = new List<int>();
        public readonly List<WebSeg> Segs = new List<WebSeg>();
    }

    /// <summary>
    /// Transforme du HTML en lignes de texte avec liens cliquables (pas de CSS ni JavaScript).
    /// Titres, paragraphes, listes, liens [ ], images remplacees par [image].
    /// </summary>
    public static class Html
    {
        // ---- Decodage UTF-8 / Latin-1 vers ASCII (la police n'a pas d'accents) ----
        public static string Decode(byte[] d)
        {
            char[] o = new char[d.Length];
            int n = 0;
            for (int i = 0; i < d.Length; i++)
            {
                byte b = d[i];
                if (b < 128) { o[n++] = (char)b; continue; }
                if ((b == 0xC3 || b == 0xC2) && i + 1 < d.Length && d[i + 1] >= 0x80 && d[i + 1] <= 0xBF)
                {
                    byte c = d[++i];
                    o[n++] = b == 0xC2 ? (c == 0xA0 ? ' ' : '?') : Latin(c + 0x40);
                    continue;
                }
                if (b >= 0xE0 && b < 0xF0 && i + 2 < d.Length)
                {
                    // ponctuation typographique courante
                    if (b == 0xE2 && d[i + 1] == 0x80) { byte c = d[i + 2]; o[n++] = (c == 0x99 || c == 0x98) ? '\'' : ((c == 0x9C || c == 0x9D) ? '"' : (c == 0xA6 ? '.' : '-')); }
                    else o[n++] = '?';
                    i += 2;
                    continue;
                }
                if (b >= 0xC0) { o[n++] = '?'; if (b < 0xE0) i++; else if (b < 0xF0) i += 2; else i += 3; continue; }
                o[n++] = Latin(b);
            }
            return new string(o, 0, n);
        }

        public static char Latin(int c)
        {
            if (c >= 0xE0 && c <= 0xE5) return 'a';
            if (c == 0xE7) return 'c';
            if (c >= 0xE8 && c <= 0xEB) return 'e';
            if (c >= 0xEC && c <= 0xEF) return 'i';
            if (c >= 0xF2 && c <= 0xF6) return 'o';
            if (c >= 0xF9 && c <= 0xFC) return 'u';
            if (c >= 0xC0 && c <= 0xC5) return 'A';
            if (c == 0xC7) return 'C';
            if (c >= 0xC8 && c <= 0xCB) return 'E';
            if (c >= 0xCC && c <= 0xCF) return 'I';
            if (c >= 0xD2 && c <= 0xD6) return 'O';
            if (c >= 0xD9 && c <= 0xDC) return 'U';
            if (c == 0xA0) return ' ';
            return '?';
        }

        private static string Entity(string e)
        {
            if (e == "amp") return "&";
            if (e == "lt") return "<";
            if (e == "gt") return ">";
            if (e == "quot") return "\"";
            if (e == "apos" || e == "rsquo" || e == "lsquo" || e == "#39") return "'";
            if (e == "nbsp") return " ";
            if (e == "laquo" || e == "raquo" || e == "ldquo" || e == "rdquo") return "\"";
            if (e == "mdash" || e == "ndash") return "-";
            if (e == "hellip") return "...";
            if (e.Length > 2 && e[1] != '#' && (e.EndsWith("acute") || e.EndsWith("grave") || e.EndsWith("circ") || e.EndsWith("uml")))
                return e.Substring(0, 1);
            if (e == "ccedil") return "c";
            if (e.StartsWith("#"))
            {
                int v = e.Length > 1 && (e[1] == 'x' || e[1] == 'X') ? HexVal(e.Substring(2)) : Utils.Fmt.ParseInt(e.Substring(1));
                if (v > 0 && v < 128) return ((char)v).ToString();
                if (v >= 160 && v < 256) return Latin(v).ToString();
                if (v == 8217 || v == 8216) return "'";
                if (v == 8220 || v == 8221) return "\"";
                return "?";
            }
            return "?";
        }

        private static int HexVal(string s)
        {
            int v = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                int d = c >= '0' && c <= '9' ? c - '0' : (c >= 'a' && c <= 'f' ? c - 'a' + 10 : (c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1));
                if (d < 0) return -1;
                v = v * 16 + d;
            }
            return v;
        }

        private static string Attr(string tag, string name)
        {
            string low = tag.ToLower();
            int i = low.IndexOf(name + "=");
            if (i < 0) return null;
            i += name.Length + 1;
            if (i >= tag.Length) return null;
            char q = tag[i];
            if (q == '"' || q == '\'')
            {
                int e = tag.IndexOf(q, i + 1);
                return e < 0 ? tag.Substring(i + 1) : tag.Substring(i + 1, e - i - 1);
            }
            int end = i;
            while (end < tag.Length && tag[end] != ' ' && tag[end] != '>') end++;
            return tag.Substring(i, end - i);
        }

        private static bool IsBlock(string t)
        {
            return t == "p" || t == "br" || t == "div" || t == "li" || t == "tr" || t == "ul" || t == "ol" ||
                   t == "table" || t == "h1" || t == "h2" || t == "h3" || t == "h4" || t == "h5" || t == "h6" ||
                   t == "section" || t == "article" || t == "header" || t == "footer" || t == "blockquote" ||
                   t == "pre" || t == "hr" || t == "form" || t == "dt" || t == "dd" || t == "nav" || t == "center";
        }

        /// <summary>HTML -> blocs (paragraphes) de segments ; remplit 'links' et 'title'.</summary>
        public static List<List<WebSeg>> Parse(string html, string baseUrl, List<string> links, out string title)
        {
            var blocks = new List<List<WebSeg>>();
            var cur = new List<WebSeg>();
            title = "";
            int link = -1;
            bool head = false, skip = false, inTitle = false, pre = false;
            string text = "";

            int i = 0;
            while (i < html.Length)
            {
                char c = html[i];
                if (c == '<')
                {
                    int e = html.IndexOf('>', i);
                    if (e < 0) break;
                    string tag = html.Substring(i + 1, e - i - 1);
                    i = e + 1;
                    if (tag.StartsWith("!")) continue;                  // commentaire / doctype
                    bool close = tag.StartsWith("/");
                    string body = close ? tag.Substring(1) : tag;
                    int sp = 0;
                    while (sp < body.Length && body[sp] != ' ' && body[sp] != '/' && body[sp] != '\n' && body[sp] != '\t') sp++;
                    string name = body.Substring(0, sp).ToLower();

                    if (name == "script" || name == "style" || name == "noscript" || name == "svg") { skip = !close; continue; }
                    if (name == "title") { inTitle = !close; continue; }
                    if (skip) continue;

                    if (IsBlock(name) || name == "a" || name == "img")
                    {
                        Flush(cur, ref text, link, head);
                        if (IsBlock(name))
                        {
                            if (cur.Count > 0 || name == "br" || name == "p") { blocks.Add(cur); cur = new List<WebSeg>(); }
                            head = !close && name.Length == 2 && name[0] == 'h' && name[1] >= '1' && name[1] <= '6';
                            if (name == "pre") pre = !close;
                            if (!close && name == "li") text = "- ";
                            if (!close && name == "hr") { var hr = new List<WebSeg>(); var s = new WebSeg(); s.Text = "------------------------------"; hr.Add(s); blocks.Add(hr); }
                        }
                        else if (name == "a")
                        {
                            if (close) link = -1;
                            else
                            {
                                string href = Attr(tag, "href");
                                string abs = href == null ? null : Resolve(baseUrl, Unescape(href));
                                if (abs != null) { links.Add(abs); link = links.Count - 1; } else link = -1;
                            }
                        }
                        else if (name == "img")
                        {
                            // Image : lien cliquable qui la telecharge (si elle est en http)
                            string alt = Attr(tag, "alt");
                            string src = Attr(tag, "src");
                            int imgLink = -1;
                            if (src != null)
                            {
                                src = Unescape(src);
                                bool https = src.ToLower().StartsWith("https://") ||
                                             (src.StartsWith("/") && baseUrl.ToLower().StartsWith("https://"));
                                string abs = https ? null : Resolve(baseUrl, src);
                                if (abs != null) { links.Add(abs); imgLink = links.Count - 1; }
                            }
                            text = "[image" + (alt != null && alt.Length > 0 ? " : " + Unescape(alt) : "") + "] ";
                            Flush(cur, ref text, imgLink, false);
                        }
                    }
                    continue;
                }

                if (c == '&')
                {
                    int e = html.IndexOf(';', i);
                    if (e > i && e - i < 10)
                    {
                        string ent = Entity(html.Substring(i + 1, e - i - 1));
                        if (inTitle) title += ent; else if (!skip) text += ent;
                        i = e + 1;
                        continue;
                    }
                }

                if (inTitle) { title += c == '\n' ? ' ' : c; i++; continue; }
                if (skip) { i++; continue; }

                if (!pre && (c == '\n' || c == '\r' || c == '\t')) c = ' ';
                if (pre && c == '\n') { Flush(cur, ref text, link, head); blocks.Add(cur); cur = new List<WebSeg>(); i++; continue; }
                if (!pre && c == ' ' && (text.EndsWith(" ") || (text.Length == 0 && cur.Count == 0))) { i++; continue; }
                text += c;
                i++;
            }
            Flush(cur, ref text, link, head);
            if (cur.Count > 0) blocks.Add(cur);
            title = title.Trim();
            return blocks;
        }

        private static string Resolve(string b, string h) => Http.Resolve(b, h);

        private static string Unescape(string s)
        {
            return s.Replace("&amp;", "&").Replace("&quot;", "\"").Replace("&#39;", "'");
        }

        private static void Flush(List<WebSeg> cur, ref string text, int link, bool head)
        {
            if (text.Length == 0) return;
            var s = new WebSeg();
            s.Text = text; s.Link = link; s.Head = head;
            cur.Add(s);
            text = "";
        }

        /// <summary>Met les blocs en lignes de 'cols' caracteres (coupure aux espaces).</summary>
        public static List<WebLine> Layout(List<List<WebSeg>> blocks, int cols)
        {
            var lines = new List<WebLine>();
            for (int bi = 0; bi < blocks.Count; bi++)
            {
                var blk = blocks[bi];
                if (blk.Count == 0)
                {
                    if (lines.Count > 0 && lines[lines.Count - 1].Text.Length > 0) lines.Add(new WebLine());
                    continue;
                }
                var line = new WebLine();
                for (int si = 0; si < blk.Count; si++)
                {
                    WebSeg seg = blk[si];
                    string rest = seg.Text;
                    while (rest.Length > 0)
                    {
                        int room = cols - line.Text.Length;
                        if (room <= 0) { lines.Add(line); line = new WebLine(); room = cols; rest = rest.TrimStart(); if (rest.Length == 0) break; }
                        string part;
                        if (rest.Length <= room) part = rest;
                        else
                        {
                            int cut = rest.LastIndexOf(' ', room);
                            if (cut <= 0) cut = line.Text.Length == 0 ? room : 0;
                            if (cut == 0) { lines.Add(line); line = new WebLine(); rest = rest.TrimStart(); continue; }
                            part = rest.Substring(0, cut);
                        }
                        var s = new WebSeg();
                        s.Text = part; s.Link = seg.Link; s.Head = seg.Head;
                        line.Starts.Add(line.Text.Length);
                        line.Segs.Add(s);
                        line.Text += part;
                        rest = rest.Substring(part.Length);
                    }
                }
                if (line.Text.Length > 0) lines.Add(line);
                lines.Add(new WebLine());   // espace entre paragraphes
            }
            return lines;
        }
    }
}
