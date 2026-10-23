using Cosmos.System.Graphics.Fonts;

namespace FileSystemOS.Graphique
{
    /// <summary>
    /// Accelerateur de texte : pour chaque caractere, la liste des pixels allumes
    /// est calculee UNE fois au demarrage (au lieu de tester 128 bits a chaque dessin).
    /// Code d'un pixel : (ligne << 8) | colonne.
    /// </summary>
    public static class FontCache
    {
        public static short[][] Pixels;
        public static bool Ready;
        public static int Count;

        public static void Build()
        {
            Font f = Services.Font;
            int fw = f.Width, fh = f.Height, bpr = (fw + 7) / 8;
            byte[] g = f.Data;
            Pixels = new short[256][];
            Count = 0;

            for (int ch = 0; ch < 256; ch++)
            {
                int n = 0;
                for (int row = 0; row < fh; row++)
                    for (int col = 0; col < fw; col++)
                        if ((g[ch * fh * bpr + row * bpr + col / 8] & (0x80 >> (col & 7))) != 0) n++;

                short[] p = new short[n];
                int k = 0;
                for (int row = 0; row < fh; row++)
                    for (int col = 0; col < fw; col++)
                        if ((g[ch * fh * bpr + row * bpr + col / 8] & (0x80 >> (col & 7))) != 0)
                            p[k++] = (short)((row << 8) | col);
                Pixels[ch] = p;
                Count += n;
            }
            Ready = true;
        }
    }
}
