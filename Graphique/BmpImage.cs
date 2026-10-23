using System;
using System.IO;

namespace FileSystemOS.Graphique
{
    /// <summary>Lecteur d'images BMP (24 ou 32 bits, non compressees) vers un PixelBuffer.</summary>
    public static class BmpImage
    {
        public const int MaxSide = 4096;

        private static int I32(byte[] d, int p) => d[p] | (d[p + 1] << 8) | (d[p + 2] << 16) | (d[p + 3] << 24);
        private static int U16(byte[] d, int p) => d[p] | (d[p + 1] << 8);

        public static PixelBuffer Load(string path)
        {
            byte[] d = File.ReadAllBytes(path);
            if (d.Length < 54 || d[0] != 'B' || d[1] != 'M') throw new Exception("ce n'est pas une image BMP");

            int offset = I32(d, 10);
            int w = I32(d, 18);
            int h = I32(d, 22);
            int bpp = U16(d, 28);
            int comp = I32(d, 30);

            bool topDown = h < 0;
            if (topDown) h = -h;
            if (w <= 0 || h <= 0 || w > MaxSide || h > MaxSide) throw new Exception("taille d'image non geree");
            if (bpp != 24 && bpp != 32) throw new Exception("BMP " + bpp + " bits : enregistrez-la en 24 bits");
            if (comp != 0 && comp != 3) throw new Exception("BMP compresse non gere");

            int stride = ((bpp * w + 31) / 32) * 4;
            int bytesPP = bpp / 8;
            if (offset + stride * h > d.Length) throw new Exception("fichier BMP tronque");

            var img = new PixelBuffer("image " + Utils.TextCodec.FileName(path), w, h);
            for (int y = 0; y < h; y++)
            {
                int srcRow = offset + (topDown ? y : h - 1 - y) * stride;
                int dst = y * w;
                for (int x = 0; x < w; x++)
                {
                    int p = srcRow + x * bytesPP;
                    img.Data[dst + x] = (int)(0xFF000000u | ((uint)d[p + 2] << 16) | ((uint)d[p + 1] << 8) | d[p]);
                }
            }
            return img;
        }

        public static bool IsImage(string name) => name.ToLower().EndsWith(".bmp");
    }
}
