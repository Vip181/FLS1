using System;
using System.Collections.Generic;
using FileSystemOS.Utils;

namespace FileSystemOS.Systeme
{
    /// <summary>
    /// Extraction du TEXTE d'un fichier PDF (sans mise en page ni images) :
    ///  1. on parcourt tous les flux (stream ... endstream) ;
    ///  2. on les decompresse (FlateDecode, avec notre Inflate) ;
    ///  3. les tables "ToUnicode" servent a traduire les codes des polices ;
    ///  4. les flux de contenu (BT ... ET) sont lus : Tj, TJ, ', " = texte ; Td, T*, Tm = retours a la ligne.
    /// Chaque flux de contenu = une page (approximation suffisante pour la plupart des PDF).
    /// </summary>
    public static class PdfText
    {
        private static readonly Dictionary<int, int> cmap = new Dictionary<int, int>();
        private static bool twoByte;

        private static int Find(byte[] d, string s, int from)
        {
            for (int i = from; i <= d.Length - s.Length; i++)
            {
                int k = 0;
                while (k < s.Length && d[i + k] == s[k]) k++;
                if (k == s.Length) return i;
            }
            return -1;
        }

        private static int FindBack(byte[] d, string s, int from)
        {
            for (int i = from; i >= 0; i--)
            {
                int k = 0;
                while (k < s.Length && i + k < d.Length && d[i + k] == s[k]) k++;
                if (k == s.Length) return i;
            }
            return -1;
        }

        private static string Ascii(byte[] d, int a, int b)
        {
            char[] c = new char[b - a];
            for (int i = a; i < b; i++) c[i - a] = (char)d[i];
            return new string(c);
        }

        public static List<string> Extract(byte[] d, out string info)
        {
            if (d.Length < 5 || d[0] != '%' || d[1] != 'P' || d[2] != 'D' || d[3] != 'F')
                throw new Exception("ce n'est pas un fichier PDF");

            cmap.Clear();
            twoByte = false;
            var contents = new List<byte[]>();
            int streams = 0, skipped = 0;

            int pos = 0;
            while (true)
            {
                int s = Find(d, "stream", pos);
                if (s < 0) break;
                pos = s + 6;
                if (s >= 3 && d[s - 1] == 'd' && d[s - 2] == 'n' && d[s - 3] == 'e') continue;   // "endstream"

                int dataStart = s + 6;
                if (dataStart < d.Length && d[dataStart] == 13) dataStart++;
                if (dataStart < d.Length && d[dataStart] == 10) dataStart++;
                int e = Find(d, "endstream", dataStart);
                if (e < 0) break;
                int dataEnd = e;
                while (dataEnd > dataStart && (d[dataEnd - 1] == 10 || d[dataEnd - 1] == 13)) dataEnd--;
                pos = e + 9;

                int objStart = FindBack(d, "obj", s);
                string dict = objStart < 0 ? "" : Ascii(d, objStart, s);
                streams++;

                if (dict.IndexOf("/Image") >= 0 || dict.IndexOf("/XRef") >= 0 || dict.IndexOf("/ObjStm") >= 0 ||
                    dict.IndexOf("/DCTDecode") >= 0 || dict.IndexOf("/FontFile") >= 0 || dict.IndexOf("/Metadata") >= 0 ||
                    dict.IndexOf("/LZWDecode") >= 0 || dict.IndexOf("/ASCII85Decode") >= 0) { skipped++; continue; }

                byte[] data;
                try
                {
                    if (dict.IndexOf("/FlateDecode") >= 0) data = Inflate.Zlib(d, dataStart, dataEnd - dataStart);
                    else { data = new byte[dataEnd - dataStart]; Array.Copy(d, dataStart, data, 0, data.Length); }
                }
                catch (Exception) { skipped++; continue; }

                if (Find(data, "begincmap", 0) >= 0) { ParseCMap(Ascii(data, 0, data.Length)); continue; }
                if (Find(data, "BT", 0) >= 0 && (Find(data, "Tj", 0) >= 0 || Find(data, "TJ", 0) >= 0)) contents.Add(data);
            }

            var pages = new List<string>();
            for (int i = 0; i < contents.Count; i++) pages.Add(Content(contents[i]));
            info = contents.Count + " page(s) de texte, " + streams + " flux" + (skipped > 0 ? " (" + skipped + " ignores : images, polices...)" : "");
            return pages;
        }

        // ---------- Tables ToUnicode ----------
        private static int HexNum(string s)
        {
            int v = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                int x = c >= '0' && c <= '9' ? c - '0' : (c >= 'a' && c <= 'f' ? c - 'a' + 10 : (c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1));
                if (x < 0) continue;
                v = v * 16 + x;
            }
            return v;
        }

        private static List<string> HexTokens(string s, int from, int to)
        {
            var r = new List<string>();
            int i = from;
            while (i < to)
            {
                if (s[i] == '<')
                {
                    int e = s.IndexOf('>', i);
                    if (e < 0 || e > to) break;
                    r.Add(s.Substring(i + 1, e - i - 1));
                    i = e + 1;
                }
                else if (s[i] == '[') { r.Add("["); i++; }
                else if (s[i] == ']') { r.Add("]"); i++; }
                else i++;
            }
            return r;
        }

        private static void ParseCMap(string t)
        {
            int p = 0;
            while ((p = t.IndexOf("beginbfchar", p)) >= 0)
            {
                int e = t.IndexOf("endbfchar", p);
                if (e < 0) break;
                var tk = HexTokens(t, p, e);
                for (int i = 0; i + 1 < tk.Count; i += 2)
                {
                    if (tk[i].Length >= 4) twoByte = true;
                    string dst = tk[i + 1].Length > 4 ? tk[i + 1].Substring(0, 4) : tk[i + 1];
                    cmap[HexNum(tk[i])] = HexNum(dst);
                }
                p = e;
            }
            p = 0;
            while ((p = t.IndexOf("beginbfrange", p)) >= 0)
            {
                int e = t.IndexOf("endbfrange", p);
                if (e < 0) break;
                var tk = HexTokens(t, p, e);
                int i = 0;
                while (i + 2 < tk.Count)
                {
                    int lo = HexNum(tk[i]), hi = HexNum(tk[i + 1]);
                    if (tk[i].Length >= 4) twoByte = true;
                    if (tk[i + 2] == "[")
                    {
                        int k = i + 3, code = lo;
                        while (k < tk.Count && tk[k] != "]") { string dst = tk[k].Length > 4 ? tk[k].Substring(0, 4) : tk[k]; cmap[code++] = HexNum(dst); k++; }
                        i = k + 1;
                    }
                    else
                    {
                        string dst = tk[i + 2].Length > 4 ? tk[i + 2].Substring(0, 4) : tk[i + 2];
                        int u = HexNum(dst);
                        for (int c = lo; c <= hi && c - lo < 2000; c++) cmap[c] = u + (c - lo);
                        i += 3;
                    }
                }
                p = e;
            }
        }

        // ---------- Texte ----------
        private static string Uni(int u)
        {
            if (u >= 32 && u < 127) return ((char)u).ToString();
            if (u == 8220 || u == 8221 || u == 171 || u == 187) return "\"";   // avant les accents : « » ne sont pas des lettres
            if (u >= 160 && u < 256) return Html.Latin(u).ToString();
            if (u == 8217 || u == 8216) return "'";
            if (u == 8211 || u == 8212 || u == 8226) return "-";
            if (u == 8230) return "...";
            if (u == 0xFB01) return "fi";
            if (u == 0xFB02) return "fl";
            if (u == 9 || u == 10 || u == 13) return " ";
            return u < 32 ? "" : "?";
        }

        private static string DecodeString(List<byte> b)
        {
            string r = "";
            if (twoByte && cmap.Count > 0)
            {
                for (int i = 0; i + 1 < b.Count; i += 2)
                {
                    int code = (b[i] << 8) | b[i + 1];
                    r += cmap.ContainsKey(code) ? Uni(cmap[code]) : "?";
                }
                return r;
            }
            for (int i = 0; i < b.Count; i++)
            {
                int code = b[i];
                r += cmap.ContainsKey(code) ? Uni(cmap[code]) : Uni(code);
            }
            return r;
        }

        private static bool Delim(byte c) =>
            c == ' ' || c == '\n' || c == '\r' || c == '\t' || c == '(' || c == ')' || c == '<' || c == '>' ||
            c == '[' || c == ']' || c == '/' || c == '%' || c == '{' || c == '}';

        private static string Content(byte[] d)
        {
            var lines = new List<string>();
            string line = "";
            string lastString = "";
            string arrayText = "";
            bool inArray = false;
            double lastNum = 0, prevNum = 0, lastTmY = double.NaN;

            int i = 0;
            while (i < d.Length)
            {
                byte c = d[i];
                if (c == ' ' || c == '\n' || c == '\r' || c == '\t') { i++; continue; }
                if (c == '%') { while (i < d.Length && d[i] != '\n') i++; continue; }

                if (c == '(')
                {
                    var buf = new List<byte>();
                    int depth = 1;
                    i++;
                    while (i < d.Length && depth > 0)
                    {
                        byte x = d[i];
                        if (x == '\\' && i + 1 < d.Length)
                        {
                            byte n = d[i + 1];
                            if (n == 'n') buf.Add(10); else if (n == 'r') buf.Add(13); else if (n == 't') buf.Add(9);
                            else if (n >= '0' && n <= '7')
                            {
                                int v = 0, k = 0;
                                while (k < 3 && i + 1 + k < d.Length && d[i + 1 + k] >= '0' && d[i + 1 + k] <= '7') { v = v * 8 + (d[i + 1 + k] - '0'); k++; }
                                buf.Add((byte)v);
                                i += k + 1;
                                continue;
                            }
                            else if (n == '\r' || n == '\n') { }
                            else buf.Add(n);
                            i += 2;
                            continue;
                        }
                        if (x == '(') depth++;
                        if (x == ')') { depth--; if (depth == 0) { i++; break; } }
                        buf.Add(x);
                        i++;
                    }
                    string t = DecodeString(buf);
                    if (inArray) arrayText += t; else lastString = t;
                    continue;
                }

                if (c == '<')
                {
                    if (i + 1 < d.Length && d[i + 1] == '<') { i += 2; continue; }
                    var buf = new List<byte>();
                    i++;
                    int hi = -1;
                    while (i < d.Length && d[i] != '>')
                    {
                        byte x = d[i++];
                        int v = x >= '0' && x <= '9' ? x - '0' : (x >= 'a' && x <= 'f' ? x - 'a' + 10 : (x >= 'A' && x <= 'F' ? x - 'A' + 10 : -1));
                        if (v < 0) continue;
                        if (hi < 0) hi = v; else { buf.Add((byte)(hi * 16 + v)); hi = -1; }
                    }
                    if (hi >= 0) buf.Add((byte)(hi * 16));
                    i++;
                    string t = DecodeString(buf);
                    if (inArray) arrayText += t; else lastString = t;
                    continue;
                }

                if (c == '>') { i++; continue; }
                if (c == '[') { inArray = true; arrayText = ""; i++; continue; }
                if (c == ']') { inArray = false; i++; continue; }
                if (c == '/') { i++; while (i < d.Length && !Delim(d[i])) i++; continue; }

                if ((c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.')
                {
                    int st = i;
                    i++;
                    while (i < d.Length && ((d[i] >= '0' && d[i] <= '9') || d[i] == '.')) i++;
                    double v = ParseNum(d, st, i);
                    if (inArray) { if (v < -180) arrayText += " "; }
                    else { prevNum = lastNum; lastNum = v; }
                    continue;
                }

                // Operateur
                int os = i;
                while (i < d.Length && !Delim(d[i])) i++;
                if (i == os) { i++; continue; }
                string op = Ascii(d, os, i);

                if (op == "Tj") line += lastString;
                else if (op == "TJ") line += arrayText;
                else if (op == "'" || op == "\"") { NewLine(lines, ref line); line += lastString; }
                else if (op == "T*") NewLine(lines, ref line);
                else if (op == "Td" || op == "TD")
                {
                    if (lastNum != 0) NewLine(lines, ref line);
                    else if (line.Length > 0 && !line.EndsWith(" ")) line += " ";
                }
                else if (op == "Tm")
                {
                    if (!double.IsNaN(lastTmY) && lastNum != lastTmY) NewLine(lines, ref line);
                    lastTmY = lastNum;
                }
                else if (op == "ET") { if (line.Length > 0 && !line.EndsWith(" ")) line += " "; }
                else if (op == "BI")
                {
                    int ei = Find(d, "EI", i);
                    i = ei < 0 ? d.Length : ei + 2;
                }
            }
            NewLine(lines, ref line);

            string r = "";
            for (int k = 0; k < lines.Count; k++) r += lines[k] + "\n";
            return r;
        }

        private static void NewLine(List<string> lines, ref string line)
        {
            // Les espaces de debut de ligne sont gardes : l'indentation du code reste lisible
            string t = line.TrimEnd();
            if (t.Trim().Length > 0) lines.Add(t);
            line = "";
        }

        private static double ParseNum(byte[] d, int a, int b)
        {
            bool neg = false;
            int i = a;
            if (d[i] == '-') { neg = true; i++; } else if (d[i] == '+') i++;
            double v = 0, frac = 0, div = 1;
            bool dot = false;
            for (; i < b; i++)
            {
                if (d[i] == '.') { dot = true; continue; }
                if (!dot) v = v * 10 + (d[i] - '0');
                else { frac = frac * 10 + (d[i] - '0'); div *= 10; }
            }
            v += frac / div;
            return neg ? -v : v;
        }
    }
}
