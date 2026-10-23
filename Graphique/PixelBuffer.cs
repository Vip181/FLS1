using System.Collections.Generic;
using System.Drawing;
using Cosmos.System.Graphics.Fonts;
using FileSystemOS.Memoire;

namespace FileSystemOS.Graphique
{
    /// <summary>
    /// Buffer de pixels ARGB 32 bits en memoire.
    /// Chaque fenetre, la barre des taches, le menu, le bureau et l'ecran ont le leur.
    /// Tous les buffers sont enregistres dans Registry (commande 'buffers').
    /// </summary>
    public unsafe class PixelBuffer
    {
        public static readonly List<PixelBuffer> Registry = new List<PixelBuffer>();

        public readonly string Name;
        public readonly int W, H;
        public readonly int[] Data;

        public PixelBuffer(string name, int w, int h) : this(name, w, h, new int[w * h]) { }

        public PixelBuffer(string name, int w, int h, int[] data)
        {
            Name = name; W = w; H = h; Data = data;
            Registry.Add(this);
        }

        /// <summary>Copie independante de ce buffer (pour figer une image).</summary>
        public PixelBuffer Copie(string nom)
        {
            var c = new PixelBuffer(nom, W, H);
            FileSystemOS.Memoire.MemoryAccelerator.Copy32(c.Data, 0, Data, 0, Data.Length);
            return c;
        }

        /// <summary>Retire le buffer de la liste (quand il est remplace).</summary>
        public void Release() => Registry.Remove(this);

        public uint Address { get { fixed (int* p = Data) { return (uint)p; } } }
        public uint Bytes => (uint)(W * H * 4);

        public static int Argb(Color c) =>
            (int)(0xFF000000u | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B);

        // ---- Primitives ----
        public void Clear(Color c) => MemoryAccelerator.Fill32(Data, 0, Data.Length, Argb(c));

        public void SetPixel(int x, int y, int argb)
        {
            if ((uint)x < (uint)W && (uint)y < (uint)H) Data[y * W + x] = argb;
        }

        public void FillRect(Color c, int x, int y, int w, int h)
        {
            int x0 = x < 0 ? 0 : x, y0 = y < 0 ? 0 : y;
            int x1 = x + w > W ? W : x + w, y1 = y + h > H ? H : y + h;
            if (x0 >= x1 || y0 >= y1) return;
            int v = Argb(c);
            for (int yy = y0; yy < y1; yy++)
                MemoryAccelerator.Fill32(Data, yy * W + x0, x1 - x0, v);
        }

        public void DrawRect(Color c, int x, int y, int w, int h)
        {
            FillRect(c, x, y, w, 1);
            FillRect(c, x, y + h - 1, w, 1);
            FillRect(c, x, y, 1, h);
            FillRect(c, x + w - 1, y, 1, h);
        }

        public void FillCircle(Color c, int cx, int cy, int r)
        {
            int r2 = r * r;
            for (int dy = -r; dy <= r; dy++)
            {
                int dx = 0;
                while ((dx + 1) * (dx + 1) + dy * dy <= r2) dx++;
                FillRect(c, cx - dx, cy + dy, dx * 2 + 1, 1);
            }
        }

        /// <summary>Texte dessine pixel par pixel a partir des glyphes de la police PSF.</summary>
        public void DrawText(string s, Color c, int x, int y)
        {
            Font f = Services.Font;
            int fw = f.Width, fh = f.Height;
            int v = Argb(c);

            // Chemin rapide : pixels de chaque caractere precalcules au demarrage
            if (FontCache.Ready)
            {
                for (int i = 0; i < s.Length; i++)
                {
                    int ch = s[i] > 255 ? '?' : s[i];
                    int gx = x + i * fw;
                    if (gx >= W) break;
                    if (gx + fw <= 0) continue;
                    short[] g = FontCache.Pixels[ch];
                    bool inside = gx >= 0 && gx + fw <= W && y >= 0 && y + fh <= H;
                    for (int k = 0; k < g.Length; k++)
                    {
                        int px = gx + (g[k] & 0xFF), py = y + (g[k] >> 8);
                        if (inside) Data[py * W + px] = v;
                        else SetPixel(px, py, v);
                    }
                }
                return;
            }

            byte[] glyphs = f.Data;
            int bpr = (fw + 7) / 8; // octets par ligne de glyphe

            for (int i = 0; i < s.Length; i++)
            {
                int ch = s[i];
                if (ch > 255) ch = '?';
                int g = ch * fh * bpr;
                int gx = x + i * fw;
                if (gx >= W) break;

                for (int row = 0; row < fh; row++)
                {
                    int py = y + row;
                    if ((uint)py >= (uint)H) continue;
                    for (int col = 0; col < fw; col++)
                    {
                        byte b = glyphs[g + row * bpr + col / 8];
                        if ((b & (0x80 >> (col & 7))) != 0) SetPixel(gx + col, py, v);
                    }
                }
            }
        }

        /// <summary>Dessine une bitmap ARGB (alpha 0 = transparent).</summary>
        /// <summary>
        /// Dessine une image ARGB avec MELANGE ALPHA : un pixel a demi transparent se fond
        /// dans le fond au lieu de l'ecraser (icones aux bords lisses, ombres...).
        /// </summary>
        public void DrawImage(uint[] px, int bw, int bh, int x, int y)
        {
            for (int j = 0; j < bh; j++)
            {
                int yy = y + j;
                if ((uint)yy >= (uint)H) continue;
                for (int i = 0; i < bw; i++)
                {
                    uint p = px[j * bw + i];
                    uint a = p >> 24;
                    if (a == 0) continue;
                    int xx = x + i;
                    if ((uint)xx >= (uint)W) continue;
                    int k = yy * W + xx;
                    if (a == 255) { Data[k] = (int)p; continue; }

                    uint d = (uint)Data[k];
                    uint inv = 255 - a;
                    uint r = (((p >> 16) & 255) * a + ((d >> 16) & 255) * inv) / 255;
                    uint g = (((p >> 8) & 255) * a + ((d >> 8) & 255) * inv) / 255;
                    uint b = ((p & 255) * a + (d & 255) * inv) / 255;
                    Data[k] = (int)(0xFF000000 | (r << 16) | (g << 8) | b);
                }
            }
        }

        // ---- Effets modernes ----

        // ---- Verre depoli : barre des taches, menu Demarrer, barre du haut ----

        private static int Melange(int d, int cr, int cg, int cb, int a)
        {
            int inv = 255 - a;
            int r = (cr * a + ((d >> 16) & 255) * inv) / 255;
            int g = (cg * a + ((d >> 8) & 255) * inv) / 255;
            int bl = (cb * a + (d & 255) * inv) / 255;
            return unchecked((int)0xFF000000) | (r << 16) | (g << 8) | bl;
        }

        /// <summary>Melange une couleur sur un pixel (alpha de 0 a 255).</summary>
        public void Melanger(int x, int y, Color c, int alpha)
        {
            Transparence.Pixel(this, x, y, c, alpha);
        }

        /// <summary>Copie telle quelle une zone d'un autre buffer.</summary>
        public void CopierZone(PixelBuffer src, int sx, int sy, int dx, int dy, int w, int h)
        {
            // Cas courant : tout est a l'interieur des deux buffers -> une copie par ligne
            if (sx >= 0 && sy >= 0 && dx >= 0 && dy >= 0 && w > 0 && h > 0 &&
                sx + w <= src.W && sy + h <= src.H && dx + w <= W && dy + h <= H)
            {
                for (int j = 0; j < h; j++) MemoryAccelerator.Copy32(Data, (dy + j) * W + dx, src.Data, (sy + j) * src.W + sx, w);
                return;
            }
            for (int j = 0; j < h; j++)
            {
                int ys = sy + j, yd = dy + j;
                if ((uint)ys >= (uint)src.H || (uint)yd >= (uint)H) continue;
                int ls = ys * src.W, ld = yd * W;
                for (int i = 0; i < w; i++)
                {
                    int xs = sx + i, xd = dx + i;
                    if ((uint)xs >= (uint)src.W || (uint)xd >= (uint)W) continue;
                    Data[ld + xd] = src.Data[ls + xs];
                }
            }
        }

        /// <summary>Copie une zone a l'interieur d'un rectangle aux coins arrondis (rayon r).</summary>
        public void CopierArrondi(PixelBuffer src, int sx, int sy, int dx, int dy, int w, int h, int r)
        {
            if (r * 2 > h) r = h / 2;
            if (r * 2 > w) r = w / 2;
            for (int j = 0; j < h; j++)
            {
                int ins = j < r ? CornerInset(r, j) : (j >= h - r ? CornerInset(r, h - 1 - j) : 0);
                int ys = sy + j, yd = dy + j;
                if ((uint)ys >= (uint)src.H || (uint)yd >= (uint)H) continue;
                int ls = ys * src.W, ld = yd * W, n = w - 2 * ins;
                if (n <= 0) continue;
                if (sx + ins >= 0 && sx + ins + n <= src.W && dx + ins >= 0 && dx + ins + n <= W)
                {
                    MemoryAccelerator.Copy32(Data, ld + dx + ins, src.Data, ls + sx + ins, n);
                    continue;
                }
                for (int i = ins; i < w - ins; i++)
                {
                    int xs = sx + i, xd = dx + i;
                    if ((uint)xs >= (uint)src.W || (uint)xd >= (uint)W) continue;
                    Data[ld + xd] = src.Data[ls + xs];
                }
            }
        }

        /// <summary>Melange une couleur sur toute une zone : teinte du verre.</summary>
        public void Teinter(int x, int y, int w, int h, Color c, int alpha)
        {
            Transparence.Rectangle(this, c, alpha, x, y, w, h);
        }

        /// <summary>Rectangle aux coins arrondis, a moitie transparent (alpha de 0 a 255).</summary>
        public void FillRoundRectAlpha(Color c, int alpha, int x, int y, int w, int h, int r)
        {
            Transparence.RectangleArrondi(this, c, alpha, x, y, w, h, r);
        }

        private static int Moyenne4(int a, int b, int c, int d)
        {
            int r = (((a >> 16) & 255) + ((b >> 16) & 255) + ((c >> 16) & 255) + ((d >> 16) & 255)) >> 2;
            int g = (((a >> 8) & 255) + ((b >> 8) & 255) + ((c >> 8) & 255) + ((d >> 8) & 255)) >> 2;
            int bl = ((a & 255) + (b & 255) + (c & 255) + (d & 255)) >> 2;
            return unchecked((int)0xFF000000) | (r << 16) | (g << 8) | bl;
        }

        /// <summary>
        /// VERRE DEPOLI RAPIDE : la zone (w x h) de src est reduite au quart (un pixel pour 4 x 4),
        /// floutee et teintee a cette taille, puis agrandie dans dst (une ligne calculee, recopiee
        /// sur les trois suivantes). Seize fois moins de pixels a traiter : sous Cosmos, sur le
        /// Dell, un flou en pleine resolution coutait une a deux secondes.
        /// </summary>
        public static void VerreRapide(PixelBuffer src, int sx, int sy, PixelBuffer dst, int dx, int dy, int w, int h,
                                       int rayon, Color teinte, int alpha)
        {
            if (w <= 0 || h <= 0) return;
            int pw = (w + 3) >> 2, ph = (h + 3) >> 2;
            var petit = new PixelBuffer("verre (reduit)", pw, ph);
            int[] p = petit.Data, s = src.Data;
            int smx = src.W - 1, smy = src.H - 1;
            for (int j = 0; j < ph; j++)
            {
                int y0 = sy + (j << 2), y1 = y0 + 2;
                y0 = y0 < 0 ? 0 : (y0 > smy ? smy : y0);
                y1 = y1 < 0 ? 0 : (y1 > smy ? smy : y1);
                int l0 = y0 * src.W, l1 = y1 * src.W;
                for (int i = 0; i < pw; i++)
                {
                    int x0 = sx + (i << 2), x1 = x0 + 2;
                    x0 = x0 < 0 ? 0 : (x0 > smx ? smx : x0);
                    x1 = x1 < 0 ? 0 : (x1 > smx ? smx : x1);
                    p[j * pw + i] = Moyenne4(s[l0 + x0], s[l0 + x1], s[l1 + x0], s[l1 + x1]);
                }
            }
            petit.Flouter(0, 0, pw, ph, rayon);
            petit.Teinter(0, 0, pw, ph, teinte, alpha);

            int xa = dx < 0 ? 0 : dx, xb = dx + w > dst.W ? dst.W : dx + w;
            if (xb <= xa) { petit.Release(); return; }
            int[] d = dst.Data;
            for (int j = 0; j < h; j += 4)
            {
                int yd = dy + j;
                if (yd >= dst.H) break;
                int ligne = (j >> 2) * pw;
                if (yd >= 0)
                {
                    int base0 = yd * dst.W;
                    for (int x = xa; x < xb; x++) d[base0 + x] = p[ligne + ((x - dx) >> 2)];
                }
                for (int k = 1; k < 4 && j + k < h; k++)
                {
                    int yk = yd + k;
                    if (yk >= dst.H) break;
                    if (yk < 0 || yd < 0) continue;
                    MemoryAccelerator.Copy32(d, yk * dst.W + xa, d, yd * dst.W + xa, xb - xa);
                }
            }
            petit.Release();
        }

        /// <summary>
        /// Contour d'un pixel d'un rectangle aux coins arrondis, a moitie transparent : seuls les
        /// bords sont touches (quelques milliers de pixels au lieu de toute la surface).
        /// </summary>
        public void ContourArrondiAlpha(Color c, int alpha, int x, int y, int w, int h, int r)
        {
            if (r * 2 > h) r = h / 2;
            if (r * 2 > w) r = w / 2;
            for (int j = 0; j < h; j++)
            {
                int ins = Retrait(r, h, j);
                if (j == 0 || j == h - 1)
                {
                    for (int i = ins; i < w - ins; i++) Melanger(x + i, y + j, c, alpha);
                    continue;
                }
                // Dans les coins, on relie a la ligne voisine pour que la courbe n'ait pas de trou
                int voisin = Retrait(r, h, j - 1), autre = Retrait(r, h, j + 1);
                if (autre > voisin) voisin = autre;
                int fin = voisin - 1 > ins ? voisin - 1 : ins;
                for (int i = ins; i <= fin; i++)
                {
                    Melanger(x + i, y + j, c, alpha);
                    Melanger(x + w - 1 - i, y + j, c, alpha);
                }
            }
        }

        private static int Retrait(int r, int h, int j) =>
            j < 0 || j >= h ? 0 : (j < r ? CornerInset(r, j) : (j >= h - r ? CornerInset(r, h - 1 - j) : 0));

        /// <summary>
        /// Flou rapide d'une zone (moyenne glissante, une passe par lignes puis une par colonnes) :
        /// son cout ne depend pas du rayon. Sert au verre depoli.
        /// </summary>
        public void Flouter(int x, int y, int w, int h, int r)
        {
            Transparence.Flou(this, x, y, w, h, r);
        }

        /// <summary>Retrait horizontal de la ligne i d'un coin arrondi de rayon r.</summary>
        public static int CornerInset(int r, int i)
        {
            int d = r - i, dx = 0;
            while ((dx + 1) * (dx + 1) + d * d <= r * r) dx++;
            return r - dx;
        }

        /// <summary>Rectangle plein aux coins arrondis.</summary>
        public void FillRoundRect(Color c, int x, int y, int w, int h, int r)
        {
            if (r * 2 > h) r = h / 2;
            for (int i = 0; i < h; i++)
            {
                int k = i < r ? i : (i >= h - r ? h - 1 - i : r);
                int inset = k < r ? CornerInset(r, k) : 0;
                FillRect(c, x + inset, y + i, w - inset * 2, 1);
            }
        }

        /// <summary>Assombrit une zone (ombre douce sans transparence) : -25 % par passage.</summary>
        public void Darken(int x, int y, int w, int h)
        {
            int x0 = x < 0 ? 0 : x, y0 = y < 0 ? 0 : y;
            int x1 = x + w > W ? W : x + w, y1 = y + h > H ? H : y + h;
            for (int yy = y0; yy < y1; yy++)
            {
                int row = yy * W;
                for (int xx = x0; xx < x1; xx++)
                {
                    uint v = (uint)Data[row + xx];
                    v = v - ((v >> 2) & 0x003F3F3Fu);
                    Data[row + xx] = (int)(v | 0xFF000000u);
                }
            }
        }

        /// <summary>Comme Blit, mais avec les coins arrondis (le fond reste visible aux coins).</summary>
        public void BlitRounded(PixelBuffer src, int dx, int dy, int r)
        {
            for (int row = 0; row < src.H; row++)
            {
                int ty = dy + row;
                if (ty < 0 || ty >= H) continue;
                int k = row < r ? row : (row >= src.H - r ? src.H - 1 - row : r);
                int inset = k < r ? CornerInset(r, k) : 0;

                int sx = inset, tx = dx + inset, w = src.W - inset * 2;
                if (tx < 0) { sx -= tx; w += tx; tx = 0; }
                if (tx + w > W) w = W - tx;
                if (w <= 0) continue;
                MemoryAccelerator.Copy32(Data, ty * W + tx, src.Data, row * src.W + sx, w);
            }
        }

        /// <summary>Copie une zone rectangulaire d'un buffer de meme taille (restauration sous le curseur).</summary>
        public void CopyRegion(PixelBuffer src, int x, int y, int w, int h)
        {
            int x0 = x < 0 ? 0 : x, y0 = y < 0 ? 0 : y;
            int x1 = x + w > W ? W : x + w, y1 = y + h > H ? H : y + h;
            if (x0 >= x1) return;
            for (int yy = y0; yy < y1; yy++)
                MemoryAccelerator.Copy32(Data, yy * W + x0, src.Data, yy * src.W + x0, x1 - x0);
        }

        /// <summary>
        /// Copie redimensionnee (plus proche voisin) de la zone source (sx, sy, sw, sh)
        /// vers le rectangle (dx, dy, dw, dh). Sert aux images et au fond d'ecran.
        /// </summary>
        public void BlitScaled(PixelBuffer src, int sx, int sy, int sw, int sh, int dx, int dy, int dw, int dh)
        {
            if (dw <= 0 || dh <= 0 || sw <= 0 || sh <= 0) return;
            for (int y = 0; y < dh; y++)
            {
                int ty = dy + y;
                if (ty < 0 || ty >= H) continue;
                int syy = sy + y * sh / dh;
                int srow = syy * src.W;
                int drow = ty * W;
                for (int x = 0; x < dw; x++)
                {
                    int tx = dx + x;
                    if (tx < 0 || tx >= W) continue;
                    Data[drow + tx] = src.Data[srow + sx + x * sw / dw];
                }
            }
        }

        /// <summary>Image entiere, agrandie/reduite pour TENIR dans le rectangle (sans deformation).</summary>
        public void DrawFit(PixelBuffer img, int x, int y, int w, int h)
        {
            int dw = w, dh = img.H * w / img.W;
            if (dh > h) { dh = h; dw = img.W * h / img.H; }
            BlitScaled(img, 0, 0, img.W, img.H, x + (w - dw) / 2, y + (h - dh) / 2, dw, dh);
        }

        /// <summary>Image qui COUVRE tout le buffer (recadree au centre) : fond d'ecran.</summary>
        public void DrawCover(PixelBuffer img)
        {
            int sw = img.W, sh = img.W * H / W;
            if (sh > img.H) { sh = img.H; sw = img.H * W / H; }
            BlitScaled(img, (img.W - sw) / 2, (img.H - sh) / 2, sw, sh, 0, 0, W, H);
        }

        /// <summary>Copie un autre buffer dedans, ligne par ligne, avec decoupage aux bords.</summary>
        public void Blit(PixelBuffer src, int dx, int dy)
        {
            int sx0 = 0, sy0 = 0, w = src.W, h = src.H;
            if (dx < 0) { sx0 = -dx; w += dx; dx = 0; }
            if (dy < 0) { sy0 = -dy; h += dy; dy = 0; }
            if (dx + w > W) w = W - dx;
            if (dy + h > H) h = H - dy;
            if (w <= 0 || h <= 0) return;

            for (int row = 0; row < h; row++)
                MemoryAccelerator.Copy32(Data, (dy + row) * W + dx,
                                         src.Data, (sy0 + row) * src.W + sx0, w);
        }
    }
}
