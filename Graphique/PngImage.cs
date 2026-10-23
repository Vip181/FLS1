using System;
using FileSystemOS.Utils;

namespace FileSystemOS.Graphique
{
    /// <summary>
    /// Lecteur PNG : niveaux de gris, RVB, palette, avec ou sans transparence
    /// (1 a 16 bits), non entrelace. La transparence est fondue sur du blanc.
    /// </summary>
    public static class PngImage
    {
        private static int BE(byte[] d, int p) => (d[p] << 24) | (d[p + 1] << 16) | (d[p + 2] << 8) | d[p + 3];

        public static PixelBuffer Load(byte[] d, string name)
        {
            if (d.Length < 8 || d[0] != 0x89 || d[1] != 'P' || d[2] != 'N' || d[3] != 'G')
                throw new Exception("ce n'est pas une image PNG");

            int w = 0, h = 0, depth = 0, ctype = 0, interlace = 0;
            byte[] palette = null;
            byte[] idat = new byte[d.Length];
            int idatLen = 0;

            int pos = 8;
            while (pos + 8 <= d.Length)
            {
                int len = BE(d, pos);
                string type = "" + (char)d[pos + 4] + (char)d[pos + 5] + (char)d[pos + 6] + (char)d[pos + 7];
                int data = pos + 8;
                if (len < 0 || data + len > d.Length) throw new Exception("PNG tronque");

                if (type == "IHDR")
                {
                    w = BE(d, data); h = BE(d, data + 4);
                    depth = d[data + 8]; ctype = d[data + 9]; interlace = d[data + 12];
                }
                else if (type == "PLTE")
                {
                    palette = new byte[len];
                    Array.Copy(d, data, palette, 0, len);
                }
                else if (type == "IDAT")
                {
                    Array.Copy(d, data, idat, idatLen, len);
                    idatLen += len;
                }
                else if (type == "IEND") break;
                pos = data + len + 4;   // + CRC
            }

            if (w <= 0 || h <= 0 || w > BmpImage.MaxSide || h > BmpImage.MaxSide) throw new Exception("taille d'image non geree");
            if (interlace != 0) throw new Exception("PNG entrelace non gere");

            int channels = ctype == 0 ? 1 : ctype == 2 ? 3 : ctype == 3 ? 1 : ctype == 4 ? 2 : ctype == 6 ? 4 : 0;
            if (channels == 0) throw new Exception("type de PNG inconnu");
            if (ctype == 3 && palette == null) throw new Exception("palette manquante");

            byte[] raw = Inflate.Zlib(idat, 0, idatLen);
            int bitsPP = channels * depth;
            int bpp = bitsPP >= 8 ? bitsPP / 8 : 1;
            int stride = (w * bitsPP + 7) / 8;
            if (raw.Length < (stride + 1) * h) throw new Exception("donnees PNG incompletes");

            var img = new PixelBuffer("image " + name, w, h);
            byte[] prev = new byte[stride];
            byte[] cur = new byte[stride];

            for (int y = 0; y < h; y++)
            {
                int rp = y * (stride + 1);
                int filter = raw[rp];
                for (int x = 0; x < stride; x++)
                {
                    int a = x >= bpp ? cur[x - bpp] : 0;
                    int b = prev[x];
                    int c = x >= bpp ? prev[x - bpp] : 0;
                    int v = raw[rp + 1 + x];
                    if (filter == 1) v += a;
                    else if (filter == 2) v += b;
                    else if (filter == 3) v += (a + b) / 2;
                    else if (filter == 4)
                    {
                        int p = a + b - c;
                        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
                        v += (pa <= pb && pa <= pc) ? a : (pb <= pc ? b : c);
                    }
                    cur[x] = (byte)v;
                }

                for (int x = 0; x < w; x++)
                {
                    int r, g, bl, al = 255;
                    if (depth < 8)
                    {
                        int bit = x * depth;
                        int val = (cur[bit >> 3] >> (8 - depth - (bit & 7))) & ((1 << depth) - 1);
                        if (ctype == 3) { r = palette[val * 3]; g = palette[val * 3 + 1]; bl = palette[val * 3 + 2]; }
                        else { r = g = bl = val * 255 / ((1 << depth) - 1); }
                    }
                    else
                    {
                        int step = depth / 8;                     // 1 ou 2 octets par canal (16 bits : octet fort)
                        int p = x * channels * step;
                        if (ctype == 0) { r = g = bl = cur[p]; }
                        else if (ctype == 2) { r = cur[p]; g = cur[p + step]; bl = cur[p + 2 * step]; }
                        else if (ctype == 3) { int i = cur[p] * 3; r = palette[i]; g = palette[i + 1]; bl = palette[i + 2]; }
                        else if (ctype == 4) { r = g = bl = cur[p]; al = cur[p + step]; }
                        else { r = cur[p]; g = cur[p + step]; bl = cur[p + 2 * step]; al = cur[p + 3 * step]; }
                    }
                    if (al < 255)
                    {
                        r = (r * al + 255 * (255 - al)) / 255;
                        g = (g * al + 255 * (255 - al)) / 255;
                        bl = (bl * al + 255 * (255 - al)) / 255;
                    }
                    img.Data[y * w + x] = (int)(0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | (uint)bl);
                }

                byte[] t = prev; prev = cur; cur = t;
            }
            return img;
        }
    }

    /// <summary>Choisit le bon lecteur selon l'extension.</summary>
    public static class ImageLoader
    {
        public static bool IsImage(string name)
        {
            string n = name.ToLower();
            return n.EndsWith(".bmp") || n.EndsWith(".png") || n.EndsWith(".jpg") || n.EndsWith(".jpeg") || n.EndsWith(".gif");
        }

        public static PixelBuffer Load(string path)
        {
            string n = path.ToLower();
            if (n.EndsWith(".png")) return PngImage.Load(FsTools.ReadBytes(path), TextCodec.FileName(path));
            if (n.EndsWith(".jpg") || n.EndsWith(".jpeg")) return JpegImage.Load(FsTools.ReadBytes(path), TextCodec.FileName(path));
            if (n.EndsWith(".gif"))
            {
                // Premiere image (la visionneuse, elle, joue l'animation)
                var g = new GifLecteur(FsTools.ReadBytes(path));
                var img = new PixelBuffer(TextCodec.FileName(path), g.W, g.H);
                img.Clear(System.Drawing.Color.Black);
                g.Suivante(img);
                return img;
            }
            return BmpImage.Load(path);
        }
    }
}
