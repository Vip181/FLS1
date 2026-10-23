namespace FileSystemOS.Utils
{
    /// <summary>Formatage fait maison (plus fiable sous Cosmos que ToString("X8")).</summary>
    public static class Fmt
    {
        private const string Digits = "0123456789ABCDEF";

        public static string Hex(uint value, int digits = 8)
        {
            char[] c = new char[digits];
            for (int i = digits - 1; i >= 0; i--)
            {
                c[i] = Digits[(int)(value & 0xF)];
                value >>= 4;
            }
            return new string(c);
        }

        public static string Pad(string s, int width)
        {
            if (s.Length >= width) return s;
            return s + new string(' ', width - s.Length);
        }

        public static string Two(int v)
        {
            return v < 10 ? "0" + v.ToString() : v.ToString();
        }

        public static int ParseInt(string s)
        {
            if (s.Length == 0) return -1;
            int v = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c < '0' || c > '9') return -1;
                v = v * 10 + (c - '0');
            }
            return v;
        }
    
        public static string Size(long bytes)
        {
            if (bytes < 1024) return bytes.ToString() + " o";
            if (bytes < 1024 * 1024) return (bytes / 1024).ToString() + " Ko";
            if (bytes < 1024L * 1024 * 1024) return (bytes / (1024 * 1024)).ToString() + " Mo";
            return (bytes / (1024L * 1024 * 1024)).ToString() + " Go";
        }
    }
}
