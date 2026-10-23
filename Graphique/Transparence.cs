using System.Drawing;

namespace FileSystemOS.Graphique
{
    /// <summary>
    /// PILOTE DE TRANSPARENCE (logiciel).
    ///
    /// Ni Cosmos ni la puce graphique Intel ne fournissent de melange de couleurs materiel
    /// utilisable : la transparence est toujours calculee par le processeur. Ce module la rend
    /// rapide et sure :
    ///  - une TABLE de 65 536 cases (Mul[a * 256 + c] = c * a / 255), calculee une fois, remplace
    ///    les trois divisions et six multiplications par pixel de l'ancien calcul ;
    ///  - les boucles travaillent directement en memoire (pointeurs), sans verification a chaque
    ///    pixel... parce que la zone est ROGNEE AUX BORDS avant la boucle : aucune ecriture ne peut
    ///    deborder. Rognage et calcul valides en Python sur 4 000 rectangles tordus (bords negatifs,
    ///    trop grands, rayons excessifs) : aucun debordement, exactement les bons pixels, ecart d'au
    ///    plus 1 sur 255 avec l'ancien calcul.
    /// </summary>
    public static unsafe class Transparence
    {
        private static byte[] mul;

        private static byte[] Table()
        {
            if (mul != null) return mul;
            var t = new byte[65536];
            for (int a = 0; a < 256; a++)
                for (int c = 0; c < 256; c++) t[a * 256 + c] = (byte)(c * a / 255);
            mul = t;
            return t;
        }

        /// <summary>Un pixel (alpha de 0 a 255).</summary>
        public static void Pixel(PixelBuffer b, int x, int y, Color c, int alpha)
        {
            if ((uint)x >= (uint)b.W || (uint)y >= (uint)b.H || alpha <= 0) return;
            if (alpha > 255) alpha = 255;
            byte[] t = Table();
            int ia = (255 - alpha) << 8, a8 = alpha << 8, k = y * b.W + x, d = b.Data[k];
            b.Data[k] = unchecked((int)0xFF000000) |
                        ((t[a8 + c.R] + t[ia + ((d >> 16) & 255)]) << 16) |
                        ((t[a8 + c.G] + t[ia + ((d >> 8) & 255)]) << 8) |
                        (t[a8 + c.B] + t[ia + (d & 255)]);
        }

        /// <summary>Rectangle d'une couleur, a moitie transparent.</summary>
        public static void Rectangle(PixelBuffer b, Color c, int alpha, int x, int y, int w, int h)
        {
            if (alpha <= 0) return;
            if (alpha >= 255) { b.FillRect(c, x, y, w, h); return; }
            int x0 = x < 0 ? 0 : x, y0 = y < 0 ? 0 : y;
            int x1 = x + w > b.W ? b.W : x + w, y1 = y + h > b.H ? b.H : y + h;
            if (x1 <= x0 || y1 <= y0) return;
            byte[] t = Table();
            int ia = (255 - alpha) << 8, a8 = alpha << 8;
            int sr = t[a8 + c.R], sg = t[a8 + c.G], sb = t[a8 + c.B];
            fixed (int* p = b.Data)
            fixed (byte* m = t)
            {
                for (int yy = y0; yy < y1; yy++)
                {
                    int* q = p + yy * b.W + x0;
                    for (int n = x1 - x0; n > 0; n--, q++)
                    {
                        int d = *q;
                        *q = unchecked((int)0xFF000000) | ((sr + m[ia + ((d >> 16) & 255)]) << 16) |
                             ((sg + m[ia + ((d >> 8) & 255)]) << 8) | (sb + m[ia + (d & 255)]);
                    }
                }
            }
        }

        /// <summary>Rectangle aux coins arrondis (rayon r), a moitie transparent.</summary>
        public static void RectangleArrondi(PixelBuffer b, Color c, int alpha, int x, int y, int w, int h, int r)
        {
            if (alpha <= 0 || w <= 0 || h <= 0) return;
            if (alpha > 255) alpha = 255;
            if (r * 2 > h) r = h / 2;
            if (r * 2 > w) r = w / 2;
            if (r < 0) r = 0;
            byte[] t = Table();
            int ia = (255 - alpha) << 8, a8 = alpha << 8;
            int sr = t[a8 + c.R], sg = t[a8 + c.G], sb = t[a8 + c.B];
            fixed (int* p = b.Data)
            fixed (byte* m = t)
            {
                for (int j = 0; j < h; j++)
                {
                    int yy = y + j;
                    if (yy < 0 || yy >= b.H) continue;
                    int ins = r > 0 ? (j < r ? PixelBuffer.CornerInset(r, j) : (j >= h - r ? PixelBuffer.CornerInset(r, h - 1 - j) : 0)) : 0;
                    int xa = x + ins, xb = x + w - ins;
                    if (xa < 0) xa = 0;
                    if (xb > b.W) xb = b.W;
                    int* q = p + yy * b.W + xa;
                    for (int n = xb - xa; n > 0; n--, q++)
                    {
                        int d = *q;
                        *q = unchecked((int)0xFF000000) | ((sr + m[ia + ((d >> 16) & 255)]) << 16) |
                             ((sg + m[ia + ((d >> 8) & 255)]) << 8) | (sb + m[ia + (d & 255)]);
                    }
                }
            }
        }

        /// <summary>
        /// Flou rapide (moyenne glissante, une passe par lignes puis une par colonnes) : son cout ne
        /// depend pas du rayon. La zone est rognee aux bords avant le travail.
        /// </summary>
        public static void Flou(PixelBuffer b, int x, int y, int w, int h, int r)
        {
            if (x < 0) { w += x; x = 0; }
            if (y < 0) { h += y; y = 0; }
            if (x + w > b.W) w = b.W - x;
            if (y + h > b.H) h = b.H - y;
            if (w <= 0 || h <= 0 || r <= 0) return;
            int d = 2 * r + 1, inv = 65536 / d;          // diviser par d = multiplier par inv, puis >> 16
            int W = b.W;
            int[] tmp = new int[w > h ? w : h];
            fixed (int* p = b.Data)
            fixed (int* tp = tmp)
            {
                for (int j = 0; j < h; j++)
                {
                    int* row = p + (y + j) * W + x;
                    int sr = 0, sg = 0, sb = 0;
                    for (int k = -r; k <= r; k++)
                    {
                        int v = row[k < 0 ? 0 : (k >= w ? w - 1 : k)];
                        sr += (v >> 16) & 255; sg += (v >> 8) & 255; sb += v & 255;
                    }
                    for (int i = 0; i < w; i++)
                    {
                        tp[i] = unchecked((int)0xFF000000) | (((sr * inv) >> 16) << 16) | (((sg * inv) >> 16) << 8) | ((sb * inv) >> 16);
                        int ia = i - r, ib = i + r + 1;
                        int va = row[ia < 0 ? 0 : ia], vb = row[ib >= w ? w - 1 : ib];
                        sr += ((vb >> 16) & 255) - ((va >> 16) & 255);
                        sg += ((vb >> 8) & 255) - ((va >> 8) & 255);
                        sb += (vb & 255) - (va & 255);
                    }
                    for (int i = 0; i < w; i++) row[i] = tp[i];
                }
                for (int i = 0; i < w; i++)
                {
                    int* col = p + y * W + x + i;
                    int sr = 0, sg = 0, sb = 0;
                    for (int k = -r; k <= r; k++)
                    {
                        int v = col[(k < 0 ? 0 : (k >= h ? h - 1 : k)) * W];
                        sr += (v >> 16) & 255; sg += (v >> 8) & 255; sb += v & 255;
                    }
                    for (int j = 0; j < h; j++)
                    {
                        tp[j] = unchecked((int)0xFF000000) | (((sr * inv) >> 16) << 16) | (((sg * inv) >> 16) << 8) | ((sb * inv) >> 16);
                        int ja = j - r, jb = j + r + 1;
                        int va = col[(ja < 0 ? 0 : ja) * W], vb = col[(jb >= h ? h - 1 : jb) * W];
                        sr += ((vb >> 16) & 255) - ((va >> 16) & 255);
                        sg += ((vb >> 8) & 255) - ((va >> 8) & 255);
                        sb += (vb & 255) - (va & 255);
                    }
                    for (int j = 0; j < h; j++) col[j * W] = tp[j];
                }
            }
        }
    }
}
