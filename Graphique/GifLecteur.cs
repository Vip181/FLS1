using System;

namespace FileSystemOS.Graphique
{
    /// <summary>
    /// LECTEUR DE GIF, anime ou non. Garde le fichier en memoire et decode UNE image a la fois
    /// sur un ecran de la taille du GIF (peu de memoire, meme pour une longue animation).
    /// Gere les palettes globale et locales, la transparence, l'entrelacement, l'effacement
    /// (disposition 2) et la lecture en boucle. Valide en Python avant traduction : images
    /// identiques a celles de PIL.
    /// </summary>
    public class GifLecteur
    {
        private readonly byte[] d;
        public readonly int W, H;
        private readonly int[] palGlobale;
        private readonly int debut;
        private int pos, disposition, transparent = -1, delai = 10;
        private int prevX, prevY, prevW, prevH, prevDisposition;
        private bool aPrecedente;
        public int Images;

        // Tables du decodeur LZW, reutilisees d'une image a l'autre
        private readonly int[] prefixe = new int[4096];
        private readonly byte[] suffixe = new byte[4096];
        private readonly byte[] pile = new byte[4097];

        public GifLecteur(byte[] data)
        {
            d = data;
            if (d.Length < 13 || d[0] != (byte)'G' || d[1] != (byte)'I' || d[2] != (byte)'F')
                throw new Exception("ce fichier n'est pas un GIF");
            W = d[6] | d[7] << 8;
            H = d[8] | d[9] << 8;
            if (W <= 0 || H <= 0 || W > 4096 || H > 4096) throw new Exception("taille de GIF non geree");
            int fl = d[10], p = 13;
            if ((fl & 0x80) != 0) palGlobale = Palette(ref p, 2 << (fl & 7));
            debut = p;
            pos = p;
        }

        private int[] Palette(ref int p, int n)
        {
            if (p + 3 * n > d.Length) throw new Exception("palette GIF tronquee");
            int[] pal = new int[n];
            for (int i = 0; i < n; i++)
                pal[i] = unchecked((int)0xFF000000) | d[p + 3 * i] << 16 | d[p + 3 * i + 1] << 8 | d[p + 3 * i + 2];
            p += 3 * n;
            return pal;
        }

        /// <summary>Contenu des sous-blocs (taille, octets...) jusqu'au bloc vide.</summary>
        private byte[] Blocs()
        {
            int total = 0, p = pos;
            while (p < d.Length && d[p] != 0) { total += d[p]; p += d[p] + 1; }
            byte[] r = new byte[total];
            int k = 0;
            while (pos < d.Length)
            {
                int n = d[pos++];
                if (n == 0) break;
                for (int i = 0; i < n && pos < d.Length; i++) r[k++] = d[pos++];
            }
            return r;
        }

        /// <summary>Dessine l'image suivante sur 'ecran' (W x H). Renvoie sa duree en millisecondes.</summary>
        public int Suivante(PixelBuffer ecran)
        {
            if (aPrecedente && prevDisposition == 2)
                ecran.FillRect(System.Drawing.Color.Black, prevX, prevY, prevW, prevH);

            int tours = 0;
            while (true)
            {
                if (pos >= d.Length || d[pos] == 0x3B)          // fin : on reprend au debut
                {
                    pos = debut;
                    if (++tours > 1) return 1000;
                    continue;
                }
                int b = d[pos++];
                if (b == 0x21)                                    // extension
                {
                    int etiquette = d[pos++];
                    if (etiquette == 0xF9 && pos + 5 < d.Length)
                    {
                        int n = d[pos], fl = d[pos + 1];
                        delai = d[pos + 2] | d[pos + 3] << 8;
                        transparent = (fl & 1) != 0 ? d[pos + 4] : -1;
                        disposition = (fl >> 2) & 7;
                        pos += 1 + n;
                    }
                    Blocs();
                    continue;
                }
                if (b != 0x2C) { pos = debut; return 1000; }      // fichier inattendu : on s'arrete la

                int x = d[pos] | d[pos + 1] << 8, y = d[pos + 2] | d[pos + 3] << 8;
                int w = d[pos + 4] | d[pos + 5] << 8, h = d[pos + 6] | d[pos + 7] << 8, fli = d[pos + 8];
                pos += 9;
                int[] pal = palGlobale;
                if ((fli & 0x80) != 0) pal = Palette(ref pos, 2 << (fli & 7));
                if (pal == null) throw new Exception("GIF sans palette");
                bool entrelace = (fli & 0x40) != 0;
                int mini = d[pos++];
                byte[] px = Lzw(Blocs(), mini, w * h);

                for (int i = 0; i < h; i++)
                {
                    int ly = entrelace ? LigneEntrelacee(i, h) : i;
                    int yy = y + ly;
                    if (yy >= H || yy >= ecran.H) continue;
                    for (int lx = 0; lx < w; lx++)
                    {
                        int xx = x + lx;
                        if (xx >= W || xx >= ecran.W) continue;
                        int c = px[i * w + lx];
                        if (c == transparent || c >= pal.Length) continue;
                        ecran.Data[yy * ecran.W + xx] = pal[c];
                    }
                }
                prevX = x; prevY = y; prevW = w; prevH = h; prevDisposition = disposition; aPrecedente = true;
                disposition = 0;
                transparent = -1;
                Images++;
                int ms = delai * 10;
                return ms < 20 ? 100 : ms;
            }
        }

        /// <summary>Ordre des lignes d'une image entrelacee : 0, 8, 16... puis 4, 12... puis 2, 6... puis 1, 3...</summary>
        private static int LigneEntrelacee(int i, int h)
        {
            int n1 = (h + 7) / 8, n2 = (h + 3) / 8, n3 = (h + 1) / 4;
            if (i < n1) return i * 8;
            i -= n1;
            if (i < n2) return 4 + i * 8;
            i -= n2;
            if (i < n3) return 2 + i * 4;
            i -= n3;
            return 1 + i * 2;
        }

        private byte[] Lzw(byte[] data, int mini, int total)
        {
            byte[] sortie = new byte[total];
            if (mini < 2 || mini > 8) return sortie;
            int n = 0, clear = 1 << mini, eoi = clear + 1, taille = mini + 1, prochain = clear + 2;
            for (int i = 0; i < clear; i++) suffixe[i] = (byte)i;
            int ancien = -1, premier = 0, acc = 0, nb = 0, p = 0;
            while (n < total)
            {
                while (nb < taille)
                {
                    if (p >= data.Length) return sortie;
                    acc |= data[p++] << nb;
                    nb += 8;
                }
                int code = acc & ((1 << taille) - 1);
                acc >>= taille;
                nb -= taille;
                if (code == clear) { taille = mini + 1; prochain = clear + 2; ancien = -1; continue; }
                if (code == eoi) break;
                if (ancien == -1)
                {
                    if (code >= clear) break;
                    sortie[n++] = suffixe[code];
                    ancien = code;
                    premier = code;
                    continue;
                }
                int entree = code, sp = 0;
                if (code >= prochain) { pile[sp++] = (byte)premier; code = ancien; }
                while (code >= clear && sp < 4096) { pile[sp++] = suffixe[code]; code = prefixe[code]; }
                premier = suffixe[code];
                pile[sp++] = (byte)premier;
                while (sp > 0 && n < total) sortie[n++] = pile[--sp];
                if (prochain < 4096)
                {
                    prefixe[prochain] = ancien;
                    suffixe[prochain] = (byte)premier;
                    prochain++;
                    if (prochain == (1 << taille) && taille < 12) taille++;
                }
                ancien = entree;
            }
            return sortie;
        }
    }
}
