using System;

namespace FileSystemOS.Graphique
{
    /// <summary>
    /// LECTEUR JPEG (JPEG "baseline", celui des appareils photo et du web).
    ///
    /// Tables de quantification et de Huffman, intervalle de redemarrage (marqueurs RST),
    /// gris ou couleur YCbCr en 4:4:4, 4:2:2 et 4:2:0. Transformee inverse en entiers (pas de
    /// calcul a virgule), et un bloc sans details est rempli d'un coup.
    /// Une tres grande photo (plus de 4 millions de pixels) est lue au 1/8 : un pixel par bloc,
    /// presque instantane, suffisant pour l'ecran.
    /// Valide en Python contre PIL : ecart moyen 0,1 a 0,9 sur 255 (le lissage des couleurs de
    /// PIL explique les petites differences aux bords nets).
    /// Non gere : JPEG progressif, CMJN (le message le dit).
    /// </summary>
    public static class JpegImage
    {
        private static readonly int[] ZZ =
        {
            0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5, 12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6, 7, 14, 21, 28,
            35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51, 58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63
        };

        // cos((2x+1) u pi / 16) * C(u) * 4096, avec C(0) = 1/racine(2) : calcule une fois en entiers
        private static readonly int[] Cos =
        {
            2896, 2896, 2896, 2896, 2896, 2896, 2896, 2896,
            4017, 3406, 2276, 799, -799, -2276, -3406, -4017,
            3784, 1567, -1567, -3784, -3784, -1567, 1567, 3784,
            3406, -799, -4017, -2276, 2276, 4017, 799, -3406,
            2896, -2896, -2896, 2896, 2896, -2896, -2896, 2896,
            2276, -4017, 799, 3406, -3406, -799, 4017, -2276,
            1567, -3784, 3784, -1567, -1567, 3784, -3784, 1567,
            799, -2276, 3406, -4017, 4017, -3406, 2276, -799
        };

        internal class Huff
        {
            public readonly int[] MaxCode = new int[18], ValPtr = new int[17], MinCode = new int[17];
            public byte[] Vals;
        }

        internal class Comp
        {
            public int Id, H, V, Tq, Td, Ta, Bw, Bh, Pred;
            public byte[] Plan;
        }

        internal class Lecture
        {
            public byte[] D;
            public int Pos, Buf, N;

            public int Bit()
            {
                if (N == 0)
                {
                    int b = Pos < D.Length ? D[Pos++] : 0;
                    if (b == 0xFF)
                    {
                        int b2 = Pos < D.Length ? D[Pos] : 0;
                        if (b2 == 0) Pos++;
                        else b = 0;                           // un marqueur : on donne des zeros
                    }
                    Buf = b;
                    N = 8;
                }
                N--;
                return (Buf >> N) & 1;
            }

            public int Bits(int n)
            {
                int v = 0;
                for (int i = 0; i < n; i++) v = (v << 1) | Bit();
                return v;
            }

            public int Decoder(Huff h)
            {
                int code = 0;
                for (int l = 1; l <= 16; l++)
                {
                    code = (code << 1) | Bit();
                    if (h.MaxCode[l] >= 0 && code <= h.MaxCode[l]) return h.Vals[h.ValPtr[l] + code - h.MinCode[l]];
                }
                throw new Exception("code de Huffman invalide (fichier abime ?)");
            }
        }

        private static int Etendre(int v, int t) => t == 0 ? 0 : (v >= (1 << (t - 1)) ? v : v - (1 << t) + 1);

        public static PixelBuffer Load(byte[] d, string nom)
        {
            Decodeur dec = Commencer(d, nom);
            while (!dec.Avancer(1 << 30)) { }
            return dec.Image;
        }

        /// <summary>
        /// Lit les en-tetes et prepare un decodage PAR ETAPES (Decodeur.Avancer) : un fond d'ecran
        /// se prepare ainsi en arriere-plan sans figer le systeme.
        /// </summary>
        public static Decodeur Commencer(byte[] d, string nom)
        {
            if (d.Length < 4 || d[0] != 0xFF || d[1] != 0xD8) throw new Exception("ce fichier n'est pas un JPEG");
            int[][] q = new int[4][];
            Huff[] hdc = new Huff[4], hac = new Huff[4];
            Comp[] comps = null;
            int ri = 0, w = 0, h = 0, pos = 2;

            while (pos + 4 <= d.Length)
            {
                if (d[pos] != 0xFF) { pos++; continue; }
                int m = d[pos + 1];
                pos += 2;
                if (m == 0xD8 || m == 0x01 || m == 0xFF || (m >= 0xD0 && m <= 0xD7)) { if (m == 0xFF) pos--; continue; }
                if (m == 0xD9) break;
                int ln = d[pos] << 8 | d[pos + 1], seg = pos + 2, fin = pos + ln;
                if (fin > d.Length) throw new Exception("JPEG tronque");

                if (m == 0xDB)
                {
                    int p = seg;
                    while (p < fin)
                    {
                        int pq = d[p] >> 4, tq = d[p] & 15;
                        p++;
                        int[] t = new int[64];
                        for (int i = 0; i < 64; i++)
                        {
                            if (pq != 0) { t[ZZ[i]] = d[p] << 8 | d[p + 1]; p += 2; }
                            else t[ZZ[i]] = d[p++];
                        }
                        q[tq & 3] = t;
                    }
                }
                else if (m == 0xC0 || m == 0xC1)
                {
                    h = d[seg + 1] << 8 | d[seg + 2];
                    w = d[seg + 3] << 8 | d[seg + 4];
                    int nc = d[seg + 5], p = seg + 6;
                    if (nc != 1 && nc != 3) throw new Exception("JPEG a " + nc + " couleurs (CMJN ?) non gere");
                    comps = new Comp[nc];
                    for (int i = 0; i < nc; i++)
                    {
                        comps[i] = new Comp { Id = d[p], H = d[p + 1] >> 4, V = d[p + 1] & 15, Tq = d[p + 2] & 3 };
                        if (comps[i].H < 1 || comps[i].V < 1 || comps[i].H > 4 || comps[i].V > 4) throw new Exception("echantillonnage JPEG invalide");
                        p += 3;
                    }
                }
                else if (m == 0xC2) throw new Exception("JPEG progressif non gere : enregistrez-le en JPEG standard ou en PNG");
                else if (m >= 0xC3 && m <= 0xCF && m != 0xC4 && m != 0xC8 && m != 0xCC)
                    throw new Exception("ce type de JPEG n'est pas gere");
                else if (m == 0xC4)
                {
                    int p = seg;
                    while (p < fin)
                    {
                        int tc = d[p] >> 4, th = d[p] & 3;
                        var hf = new Huff();
                        int code = 0, k = 0, total = 0;
                        for (int l = 1; l <= 16; l++) total += d[p + l];
                        hf.Vals = new byte[total];
                        for (int i = 0; i < total; i++) hf.Vals[i] = d[p + 17 + i];
                        for (int l = 1; l <= 16; l++)
                        {
                            int n = d[p + l];
                            hf.ValPtr[l] = k;
                            hf.MinCode[l] = code;
                            code += n;
                            k += n;
                            hf.MaxCode[l] = n != 0 ? code - 1 : -1;
                            code <<= 1;
                        }
                        if (tc == 0) hdc[th] = hf; else hac[th] = hf;
                        p += 17 + total;
                    }
                }
                else if (m == 0xDD) ri = d[seg] << 8 | d[seg + 1];
                else if (m == 0xDA)
                {
                    if (comps == null) throw new Exception("JPEG sans en-tete d'image");
                    int ns = d[seg], p = seg + 1;
                    for (int i = 0; i < ns; i++)
                    {
                        int cid = d[p], td = d[p + 1] >> 4, ta = d[p + 1] & 15;
                        p += 2;
                        for (int k = 0; k < comps.Length; k++)
                            if (comps[k].Id == cid) { comps[k].Td = td & 3; comps[k].Ta = ta & 3; }
                    }
                    return new Decodeur(d, fin, w, h, comps, q, hdc, hac, ri, nom);
                }
                pos = fin;
            }
            throw new Exception("JPEG sans donnees d'image");
        }

        /// <summary>
        /// Decodage d'un JPEG par etapes : d'abord les blocs (MCU) un groupe a la fois, puis la
        /// conversion des couleurs ligne par ligne. Memes calculs que la version d'un seul bloc
        /// (validee contre PIL) ; seul le decoupage change.
        /// </summary>
        public class Decodeur
        {
            private readonly byte[] d;
            private readonly Comp[] comps;
            private readonly int[][] q;
            private readonly Huff[] hdc, hac;
            private readonly int ri, hmax = 1, vmax = 1, mcux, mcuy, ow, oh;
            private readonly bool petit;
            private readonly Lecture lec;
            private readonly int[] blk = new int[64], tmp = new int[64];
            private int restants, mx, my, ligne;
            public readonly PixelBuffer Image;

            internal Decodeur(byte[] data, int pos, int w, int h, Comp[] c, int[][] qt, Huff[] hd, Huff[] ha, int intervalle, string nom)
            {
                if (w <= 0 || h <= 0) throw new Exception("taille de JPEG invalide");
                d = data; comps = c; q = qt; hdc = hd; hac = ha; ri = intervalle;
                petit = (long)w * h > 4000000;                  // tres grande photo : lue au 1/8
                for (int i = 0; i < comps.Length; i++) { if (comps[i].H > hmax) hmax = comps[i].H; if (comps[i].V > vmax) vmax = comps[i].V; }
                mcux = (w + 8 * hmax - 1) / (8 * hmax);
                mcuy = (h + 8 * vmax - 1) / (8 * vmax);
                for (int i = 0; i < comps.Length; i++)
                {
                    Comp k = comps[i];
                    if (q[k.Tq] == null || hdc[k.Td] == null || hac[k.Ta] == null) throw new Exception("tables JPEG manquantes");
                    k.Bw = mcux * k.H;
                    k.Bh = mcuy * k.V;
                    k.Plan = new byte[petit ? k.Bw * k.Bh : k.Bw * 8 * k.Bh * 8];
                    k.Pred = 0;
                }
                lec = new Lecture { D = d, Pos = pos };
                restants = ri;
                ow = petit ? (w + 7) / 8 : w;
                oh = petit ? (h + 7) / 8 : h;
                Image = new PixelBuffer(nom, ow, oh);
            }

            /// <summary>Avancement de 0 a 100 (les blocs comptent pour 80, les couleurs pour 20).</summary>
            public int Progression
            {
                get
                {
                    long fait = (long)my * mcux + mx, total = (long)mcux * mcuy;
                    return (int)(fait * 80 / (total < 1 ? 1 : total)) + ligne * 20 / (oh < 1 ? 1 : oh);
                }
            }

            /// <summary>Fait au plus n etapes (un MCU, ou une ligne de couleurs) ; vrai quand l'image est finie.</summary>
            public bool Avancer(int n)
            {
                while (n-- > 0)
                {
                    if (my < mcuy)
                    {
                        Mcu();
                        if (++mx >= mcux) { mx = 0; my++; }
                        continue;
                    }
                    if (ligne < oh) { Couleurs(ligne); ligne++; continue; }
                    return true;
                }
                return my >= mcuy && ligne >= oh;
            }

            private void Mcu()
            {
                if (ri != 0)
                {
                    if (restants == 0)
                    {
                        lec.N = 0;
                        while (lec.Pos + 1 < d.Length && !(d[lec.Pos] == 0xFF && d[lec.Pos + 1] >= 0xD0 && d[lec.Pos + 1] <= 0xD7)) lec.Pos++;
                        lec.Pos += 2;
                        for (int i = 0; i < comps.Length; i++) comps[i].Pred = 0;
                        restants = ri;
                    }
                    restants--;
                }
                for (int i = 0; i < comps.Length; i++)
                {
                    Comp c = comps[i];
                    for (int v = 0; v < c.V; v++)
                        for (int hh = 0; hh < c.H; hh++)
                            Bloc(lec, c, q[c.Tq], hdc[c.Td], hac[c.Ta], mx * c.H + hh, my * c.V + v, blk, tmp, petit);
                }
            }

            // Conversion en couleurs RGB d'une ligne (chaque couleur prise au plus proche)
            private void Couleurs(int y)
            {
                for (int x = 0; x < ow; x++)
                {
                    int r, g, b;
                    if (comps.Length == 1)
                    {
                        r = g = b = Valeur(comps[0], x, y, hmax, vmax, petit);
                    }
                    else
                    {
                        int Y = Valeur(comps[0], x, y, hmax, vmax, petit);
                        int cb = Valeur(comps[1], x, y, hmax, vmax, petit) - 128;
                        int cr = Valeur(comps[2], x, y, hmax, vmax, petit) - 128;
                        r = Y + ((91881 * cr + 32768) >> 16);
                        g = Y - ((22554 * cb + 46802 * cr - 32768) >> 16);
                        b = Y + ((116130 * cb + 32768) >> 16);
                    }
                    r = r < 0 ? 0 : (r > 255 ? 255 : r);
                    g = g < 0 ? 0 : (g > 255 ? 255 : g);
                    b = b < 0 ? 0 : (b > 255 ? 255 : b);
                    Image.Data[y * ow + x] = unchecked((int)0xFF000000) | r << 16 | g << 8 | b;
                }
            }
        }

        private static int Valeur(Comp c, int x, int y, int hmax, int vmax, bool petit)
        {
            int cx = x * c.H / hmax, cy = y * c.V / vmax;
            int pw = petit ? c.Bw : c.Bw * 8;
            return c.Plan[cy * pw + cx];
        }

        private static void Bloc(Lecture lec, Comp c, int[] qt, Huff hd, Huff ha, int bx, int by,
                                 int[] blk, int[] tmp, bool petit)
        {
            for (int i = 0; i < 64; i++) blk[i] = 0;
            int t = lec.Decoder(hd);
            c.Pred += Etendre(t == 0 ? 0 : lec.Bits(t), t);
            blk[0] = c.Pred * qt[0];
            bool seulDc = true;
            int k = 1;
            while (k < 64)
            {
                int rs = lec.Decoder(ha), r = rs >> 4, s = rs & 15;
                if (s == 0)
                {
                    if (r == 15) { k += 16; continue; }
                    break;
                }
                k += r;
                if (k > 63) break;
                blk[ZZ[k]] = Etendre(lec.Bits(s), s) * qt[ZZ[k]];
                seulDc = false;
                k++;
            }

            if (petit)
            {
                int v0 = ((blk[0] + 4) >> 3) + 128;
                c.Plan[by * c.Bw + bx] = (byte)(v0 < 0 ? 0 : (v0 > 255 ? 255 : v0));
                return;
            }

            int pw = c.Bw * 8, x0 = bx * 8, y0 = by * 8;
            if (seulDc)
            {
                int v = ((blk[0] + 4) >> 3) + 128;
                byte bv = (byte)(v < 0 ? 0 : (v > 255 ? 255 : v));
                for (int y = 0; y < 8; y++)
                    for (int x = 0; x < 8; x++) c.Plan[(y0 + y) * pw + x0 + x] = bv;
                return;
            }

            // Transformee inverse separable : colonnes, puis lignes
            for (int u = 0; u < 8; u++)
                for (int y = 0; y < 8; y++)
                {
                    int s = 0;
                    for (int v = 0; v < 8; v++) s += blk[v * 8 + u] * Cos[v * 8 + y];
                    tmp[y * 8 + u] = (s + 2048) >> 12;
                }
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                {
                    int s = 0;
                    for (int u = 0; u < 8; u++) s += tmp[y * 8 + u] * Cos[u * 8 + x];
                    int v = ((s + 8192) >> 14) + 128;
                    c.Plan[(y0 + y) * pw + x0 + x] = (byte)(v < 0 ? 0 : (v > 255 ? 255 : v));
                }
        }
    }
}
