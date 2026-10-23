using FileSystemOS.Utils;

namespace FileSystemOS.Graphique
{
    /// <summary>
    /// GIF ANIME : le format video le plus simple, lisible partout (Windows, navigateurs).
    ///
    /// 256 couleurs fixes (6 niveaux de rouge x 7 de vert x 6 de bleu), chaque image compressee
    /// en LZW. L'encodeur travaille PAR MORCEAUX (quelques lignes a chaque tour de
    /// l'ordonnanceur) : l'enregistrement ne fige pas l'interface.
    /// Algorithme valide en Python avant d'etre traduit : les images decodees sont identiques.
    /// </summary>
    public static class GifAnime
    {
        /// <summary>Indice de la couleur la plus proche dans la palette fixe.</summary>
        public static byte Indice(int argb)
        {
            int r = (argb >> 16) & 0xFF, g = (argb >> 8) & 0xFF, b = argb & 0xFF;
            return (byte)(((r * 6) >> 8) * 42 + ((g * 7) >> 8) * 6 + ((b * 6) >> 8));
        }

        /// <summary>En-tete du fichier : taille, palette, et lecture en boucle.</summary>
        public static void Entete(Tampon t, int w, int h)
        {
            t.AjouterTexte("GIF89a");
            t.Ajouter16(w); t.Ajouter16(h);
            t.Ajouter(0xF7);                 // palette globale de 256 couleurs
            t.Ajouter(0); t.Ajouter(0);
            for (int r = 0; r < 6; r++)
                for (int g = 0; g < 7; g++)
                    for (int b = 0; b < 6; b++)
                    {
                        t.Ajouter((byte)(r * 255 / 5));
                        t.Ajouter((byte)(g * 255 / 6));
                        t.Ajouter((byte)(b * 255 / 5));
                    }
            for (int i = 252; i < 256; i++) { t.Ajouter(0); t.Ajouter(0); t.Ajouter(0); }
            // Extension NETSCAPE : lire en boucle
            t.Ajouter(0x21); t.Ajouter(0xFF); t.Ajouter(0x0B);
            t.AjouterTexte("NETSCAPE2.0");
            t.Ajouter(3); t.Ajouter(1); t.Ajouter(0); t.Ajouter(0); t.Ajouter(0);
        }

        /// <summary>Debut d'une image : duree d'affichage (centiemes de seconde) et position.</summary>
        public static void DebutImage(Tampon t, int w, int h, int delai)
        {
            t.Ajouter(0x21); t.Ajouter(0xF9); t.Ajouter(4); t.Ajouter(0x04);
            t.Ajouter16(delai); t.Ajouter(0); t.Ajouter(0);
            t.Ajouter(0x2C); t.Ajouter16(0); t.Ajouter16(0); t.Ajouter16(w); t.Ajouter16(h); t.Ajouter(0);
            t.Ajouter(8);                    // taille minimale des codes LZW
        }

        public static void FinImage(Tampon t) => t.Ajouter(0);
        public static void Fin(Tampon t) => t.Ajouter(0x3B);
    }

    /// <summary>Compresseur LZW de GIF, alimente par morceaux (une image = Debuter / Ajouter... / Terminer).</summary>
    public class LzwGif
    {
        private const int HSIZE = 5003;
        private readonly int[] htab = new int[HSIZE];
        private readonly int[] codetab = new int[HSIZE];
        private readonly byte[] paquet = new byte[256];
        private int nPaquet;
        private Tampon sortie;

        private int nBits, maxCode, libre, ent, hshift, curAccum, curBits;
        private bool clearFlg;
        private const int InitBits = 9, Clear = 256, Eof = 257;

        public void Debuter(Tampon t)
        {
            sortie = t;
            nBits = InitBits; maxCode = (1 << nBits) - 1;
            libre = Clear + 2; clearFlg = false;
            for (int i = 0; i < HSIZE; i++) htab[i] = -1;
            curAccum = 0; curBits = 0; nPaquet = 0; ent = -1;
            hshift = 0;
            for (int f = HSIZE; f < 65536; f *= 2) hshift++;
            hshift = 8 - hshift;
            Sortir(Clear);
        }

        public void Ajouter(byte[] px, int debut, int n)
        {
            int k = debut, fin = debut + n;
            if (ent < 0 && k < fin) { ent = px[k]; k++; }
            for (; k < fin; k++)
            {
                int c = px[k];
                int fcode = (c << 12) + ent;
                int i = (c << hshift) ^ ent;
                if (htab[i] == fcode) { ent = codetab[i]; continue; }
                if (htab[i] >= 0)
                {
                    int disp = HSIZE - i;
                    if (i == 0) disp = 1;
                    bool trouve = false;
                    while (true)
                    {
                        i -= disp;
                        if (i < 0) i += HSIZE;
                        if (htab[i] == fcode) { ent = codetab[i]; trouve = true; break; }
                        if (htab[i] < 0) break;
                    }
                    if (trouve) continue;
                }
                Sortir(ent);
                ent = c;
                if (libre < 4096)
                {
                    codetab[i] = libre++;
                    htab[i] = fcode;
                }
                else
                {
                    for (int j = 0; j < HSIZE; j++) htab[j] = -1;
                    libre = Clear + 2;
                    clearFlg = true;
                    Sortir(Clear);
                }
            }
        }

        public void Terminer()
        {
            Sortir(ent);
            Sortir(Eof);
        }

        private void Sortir(int code)
        {
            curAccum |= code << curBits;
            curBits += nBits;
            while (curBits >= 8)
            {
                Octet((byte)(curAccum & 0xFF));
                curAccum >>= 8;
                curBits -= 8;
            }
            if (libre > maxCode || clearFlg)
            {
                if (clearFlg)
                {
                    nBits = InitBits; maxCode = (1 << nBits) - 1; clearFlg = false;
                }
                else
                {
                    nBits++;
                    maxCode = nBits == 12 ? 4096 : (1 << nBits) - 1;
                }
            }
            if (code == Eof)
            {
                while (curBits > 0)
                {
                    Octet((byte)(curAccum & 0xFF));
                    curAccum >>= 8;
                    curBits -= 8;
                }
                Vider();
            }
        }

        private void Octet(byte b)
        {
            paquet[nPaquet++] = b;
            if (nPaquet >= 255) Vider();
        }

        private void Vider()
        {
            if (nPaquet == 0) return;
            sortie.Ajouter((byte)nPaquet);
            sortie.Ajouter(paquet, 0, nPaquet);
            nPaquet = 0;
        }
    }
}
