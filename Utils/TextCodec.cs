using System.Collections.Generic;
using System.IO;

namespace FileSystemOS.Utils
{
    /// <summary>Lecture / ecriture de fichiers texte (ASCII + accents UTF-8 simplifies).</summary>
    public static class TextCodec
    {
        public const int MaxBytes = 1024 * 1024;

        public static List<string> ReadLines(string path)
        {
            byte[] d = FsTools.ReadBytes(path);
            return Decode(d, d.Length > MaxBytes ? MaxBytes : d.Length);
        }

        public static List<string> Decode(byte[] data, int n)
        {
            var lines = new List<string>();
            char[] buf = new char[2048];
            int count = 0;

            for (int i = 0; i < n; i++)
            {
                byte c = data[i];
                if (c == 10) { lines.Add(new string(buf, 0, count)); count = 0; continue; }
                if (c == 13) continue;
                if (c == 9)
                {
                    for (int s = 0; s < 4 && count < buf.Length; s++) buf[count++] = ' ';
                    continue;
                }

                char o;
                if (c >= 32 && c < 127) o = (char)c;
                else if (c == 0xC3 && i + 1 < n) { o = Unaccent(data[i + 1]); i++; }
                else if (c >= 0xC0) o = '?';
                else continue;

                if (count < buf.Length) buf[count++] = o;
            }
            lines.Add(new string(buf, 0, count));
            return lines;
        }

        public static string Join(List<string> lines)
        {
            int total = 0;
            for (int i = 0; i < lines.Count; i++) total += lines[i].Length + 1;
            char[] c = new char[total];
            int p = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                string l = lines[i];
                for (int j = 0; j < l.Length; j++) c[p++] = l[j];
                c[p++] = '\n';
            }
            return new string(c, 0, p);
        }

        public static byte[] Encode(List<string> lines)
        {
            int total = 0;
            for (int i = 0; i < lines.Count; i++) total += lines[i].Length + 1;
            if (total > 0) total--; // pas de saut de ligne apres la derniere ligne
            byte[] b = new byte[total];
            int p = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                string l = lines[i];
                for (int j = 0; j < l.Length; j++) b[p++] = l[j] < 128 ? (byte)l[j] : (byte)'?';
                if (i < lines.Count - 1) b[p++] = 10;
            }
            return b;
        }

        private static char Unaccent(byte b)
        {
            if (b >= 0xA0 && b <= 0xA5) return 'a';
            if (b == 0xA7) return 'c';
            if (b >= 0xA8 && b <= 0xAB) return 'e';
            if (b >= 0xAC && b <= 0xAF) return 'i';
            if (b >= 0xB2 && b <= 0xB6) return 'o';
            if (b >= 0xB9 && b <= 0xBC) return 'u';
            if (b >= 0x80 && b <= 0x85) return 'A';
            if (b == 0x87) return 'C';
            if (b >= 0x88 && b <= 0x8B) return 'E';
            if (b >= 0x8C && b <= 0x8F) return 'I';
            if (b >= 0x92 && b <= 0x96) return 'O';
            if (b >= 0x99 && b <= 0x9C) return 'U';
            return '?';
        }

        public static string FileName(string path)
        {
            int i = path.LastIndexOf('\\');
            return i >= 0 ? path.Substring(i + 1) : path;
        }

        public static string PathJoin(string dir, string name) =>
            dir.EndsWith("\\") ? dir + name : dir + "\\" + name;
    }
}
