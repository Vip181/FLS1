using System;
using System.Collections.Generic;

namespace FileSystemOS.Utils
{
    /// <summary>
    /// ECRIVAIN DE PDF : fabrique un vrai fichier PDF (A4, polices standard Helvetica, Helvetica-Bold
    /// et Courier, accents en WinAnsi) que l'on lit dans File System OS comme sous Windows.
    /// Mise en page automatique : sections numerotees, paragraphes coupes a la largeur, listes,
    /// encadres de code et notes, pages et pieds de page, couverture avec sommaire.
    /// Uniquement des nombres entiers (aucun calcul a virgule) ; valide en Python avant traduction
    /// (qpdf : aucune erreur ; rendu verifie page par page).
    /// </summary>
    public class PdfEcrivain
    {
        private const int L = 595, H = 842, MG = 56, MH = 56, MB = 72, LARG = 595 - 56 - 56;

        // Largeur des caracteres 32 a 255 (en milliemes de la taille), metriques officielles Adobe
        private static readonly int[] Helv =
        {
            278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,
            556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
            1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,
            667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,
            333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556,
            556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584, 761,
            761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761,
            761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761,
            278, 333, 556, 556, 556, 556, 260, 556, 333, 737, 370, 556, 584, 333, 737, 333,
            400, 584, 333, 333, 333, 556, 537, 278, 333, 333, 365, 556, 834, 834, 834, 611,
            667, 667, 667, 667, 667, 667, 1000, 722, 667, 667, 667, 667, 278, 278, 278, 278,
            722, 722, 778, 778, 778, 778, 778, 584, 778, 722, 722, 722, 722, 667, 667, 611,
            556, 556, 556, 556, 556, 556, 889, 500, 556, 556, 556, 556, 278, 278, 278, 278,
            556, 556, 556, 556, 556, 556, 556, 584, 611, 556, 556, 556, 556, 500, 556, 500
        };

        private static readonly int[] HelvGras =
        {
            278, 333, 474, 556, 556, 889, 722, 238, 333, 333, 389, 584, 278, 333, 278, 278,
            556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 333, 333, 584, 584, 584, 611,
            975, 722, 722, 722, 722, 667, 611, 778, 722, 278, 556, 722, 611, 833, 722, 778,
            667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 333, 278, 333, 584, 556,
            333, 556, 611, 556, 611, 556, 333, 611, 611, 278, 278, 556, 278, 889, 611, 611,
            611, 611, 389, 556, 333, 611, 556, 778, 556, 556, 500, 389, 280, 389, 584, 761,
            761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761,
            761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761,
            278, 333, 556, 556, 556, 556, 280, 556, 333, 737, 370, 556, 584, 333, 737, 333,
            400, 584, 333, 333, 333, 611, 556, 278, 333, 333, 365, 556, 834, 834, 834, 611,
            722, 722, 722, 722, 722, 722, 1000, 722, 667, 667, 667, 667, 278, 278, 278, 278,
            722, 722, 778, 778, 778, 778, 778, 584, 778, 722, 722, 722, 722, 667, 667, 611,
            556, 556, 556, 556, 556, 556, 889, 556, 556, 556, 556, 556, 278, 278, 278, 278,
            611, 611, 611, 611, 611, 611, 611, 584, 611, 611, 611, 611, 611, 556, 611, 556
        };

        private static readonly int[] Texte = { 34, 37, 47 }, Indigo = { 88, 101, 242 }, Gris = { 107, 115, 133 };
        private static readonly int[] CodeFond = { 243, 244, 248 }, NoteFond = { 236, 239, 255 }, Blanc = { 255, 255, 255 }, Filet = { 214, 218, 228 };

        private readonly string titre, auteur;
        private readonly List<List<string>> pages = new List<List<string>>();
        private List<string> page;
        private int y, nsec;
        private readonly List<string> sections = new List<string>();
        private readonly List<int> pagesSections = new List<int>();

        public PdfEcrivain(string titreDocument, string auteurDocument)
        {
            titre = titreDocument;
            auteur = auteurDocument;
            Nouvelle();
        }

        private void Nouvelle()
        {
            page = new List<string>();
            pages.Add(page);
            y = MH;
        }

        private void Place(int h)
        {
            if (y + h > H - MB) Nouvelle();
        }

        // ---- Outils ----

        private static string Comp(int c)
        {
            int k = c * 1000 / 255;
            if (k >= 1000) return "1";
            return "0." + (k < 10 ? "00" : (k < 100 ? "0" : "")) + k;
        }

        private static string Couleur(int[] c) => Comp(c[0]) + " " + Comp(c[1]) + " " + Comp(c[2]);

        private static string Echapper(string t)
        {
            string r = "";
            for (int i = 0; i < t.Length; i++)
            {
                char ch = t[i];
                if (ch == '\\') r += "\\\\";
                else if (ch == '(') r += "\\(";
                else if (ch == ')') r += "\\)";
                else if (ch >= 256) r += "?";
                else r += ch;
            }
            return r;
        }

        /// <summary>Largeur d'un texte en milliemes de point.</summary>
        private static int Largeur(string t, int police, int taille)
        {
            if (police == 3) return t.Length * 600 * taille;
            int[] tab = police == 1 ? Helv : HelvGras;
            int tot = 0;
            for (int i = 0; i < t.Length; i++)
            {
                int c = t[i];
                tot += c >= 32 && c < 256 ? tab[c - 32] : 556;
            }
            return tot * taille;
        }

        private void Ecrit(int x, int haut, int police, int taille, int[] c, string t)
        {
            page.Add("BT /F" + police + " " + taille + " Tf " + Couleur(c) + " rg " + x + " " + (H - haut - taille) + " Td (" + Echapper(t) + ") Tj ET");
        }

        private void Rect(int x, int haut, int w, int h, int[] c)
        {
            page.Add(Couleur(c) + " rg " + x + " " + (H - haut - h) + " " + w + " " + h + " re f");
        }

        /// <summary>Coupe un texte en lignes qui tiennent dans la largeur (mots entiers si possible).</summary>
        private static List<string> Couper(string t, int police, int taille, int larg)
        {
            var lignes = new List<string>();
            string[] mots = t.Split(' ');
            string cur = "";
            for (int i = 0; i < mots.Length; i++)
            {
                string m = mots[i];
                string essai = cur.Length == 0 ? m : cur + " " + m;
                if (Largeur(essai, police, taille) <= larg * 1000) { cur = essai; continue; }
                if (cur.Length > 0) lignes.Add(cur);
                cur = m;
                while (Largeur(cur, police, taille) > larg * 1000 && cur.Length > 1)
                {
                    int k = cur.Length - 1;
                    while (k > 1 && Largeur(cur.Substring(0, k), police, taille) > larg * 1000) k--;
                    lignes.Add(cur.Substring(0, k));
                    cur = cur.Substring(k);
                }
            }
            if (cur.Length > 0 || lignes.Count == 0) lignes.Add(cur);
            return lignes;
        }

        // ---- Blocs ----

        public void Section(string t)
        {
            nsec++;
            if (y > MH) y += 14;
            Place(80);
            string s = nsec + ". " + t;
            sections.Add(s);
            pagesSections.Add(pages.Count);
            Ecrit(MG, y, 2, 18, Indigo, s); y += 26;
            Rect(MG, y, LARG, 2, Indigo); y += 14;
        }

        public void Annexe(string t)
        {
            Nouvelle();
            sections.Add(t);
            pagesSections.Add(pages.Count);
            Ecrit(MG, y, 2, 18, Indigo, t); y += 26;
            Rect(MG, y, LARG, 2, Indigo); y += 14;
        }

        public void SousSection(string t)
        {
            y += 4;
            Place(40);
            Ecrit(MG, y, 2, 13, Texte, t); y += 20;
        }

        public void Paragraphe(string t)
        {
            List<string> l = Couper(t, 1, 11, LARG);
            for (int i = 0; i < l.Count; i++) { Place(15); Ecrit(MG, y, 1, 11, Texte, l[i]); y += 15; }
            y += 6;
        }

        public void Liste(string t)
        {
            List<string> l = Couper(t, 1, 11, LARG - 16);
            for (int i = 0; i < l.Count; i++)
            {
                Place(15);
                if (i == 0) Rect(MG + 3, y + 4, 5, 5, Indigo);
                Ecrit(MG + 16, y, 1, 11, Texte, l[i]);
                y += 15;
            }
            y += 3;
        }

        public void Code(string t)
        {
            var lignes = new List<string>();
            string[] brut = t.Split('\n');
            for (int i = 0; i < brut.Length; i++)
            {
                string l = brut[i];
                while (l.Length > 76) { lignes.Add(l.Substring(0, 76)); l = "  " + l.Substring(76); }
                lignes.Add(l);
            }
            // Un bloc court ne se coupe pas : il passe entier a la page suivante
            if (lignes.Count <= 24 && y + lignes.Count * 13 + 16 > H - MB) Nouvelle();
            int k = 0;
            while (k < lignes.Count)
            {
                int dispo = (H - MB - y - 16) / 13;
                if (dispo < 2) { Nouvelle(); continue; }
                int n = dispo < lignes.Count - k ? dispo : lignes.Count - k;
                int h = n * 13 + 16;
                Rect(MG, y, LARG, h, CodeFond);
                Rect(MG, y, 3, h, Indigo);
                for (int j = 0; j < n; j++) Ecrit(MG + 14, y + 8 + j * 13, 3, 10, Texte, lignes[k + j]);
                y += h + 10;
                k += n;
            }
        }

        public void Note(string t)
        {
            List<string> l = Couper(t, 1, 11, LARG - 28);
            int h = l.Count * 15 + 16;
            Place(h);
            Rect(MG, y, LARG, h, NoteFond);
            Rect(MG, y, 3, h, Indigo);
            for (int i = 0; i < l.Count; i++) Ecrit(MG + 16, y + 8 + i * 15, 1, 11, Texte, l[i]);
            y += h + 10;
        }

        /// <summary>Une entree de reference : la signature en Courier, puis sa description.</summary>
        public void Fonction(string signature, string description)
        {
            List<string> l = description.Length > 0 ? Couper(description, 1, 10, LARG - 16) : new List<string>();
            Place(16 + 13 * l.Count);
            Ecrit(MG, y, 3, 10, Indigo, signature); y += 15;
            for (int i = 0; i < l.Count; i++) { Ecrit(MG + 16, y, 1, 10, Gris, l[i]); y += 13; }
            y += 5;
        }

        // ---- Fin : couverture, pieds de page, fichier ----

        public byte[] Terminer(string sousTitre, string version, string editeur)
        {
            var couv = new List<string>();
            pages.Insert(0, couv);
            page = couv;
            Rect(0, 0, L, 250, Indigo);
            Ecrit(MG, 70, 2, 30, Blanc, titre);
            Ecrit(MG, 118, 1, 15, Blanc, sousTitre);
            Ecrit(MG, 170, 1, 11, Blanc, editeur);
            Ecrit(MG, 188, 1, 11, Blanc, "Auteur : " + auteur);
            Ecrit(MG, 206, 1, 11, Blanc, version);
            Ecrit(MG, 290, 2, 16, Texte, "Sommaire");
            int yy = 322;
            for (int i = 0; i < sections.Count; i++)
            {
                string num = (pagesSections[i] + 1).ToString();
                Ecrit(MG, yy, 1, 11, Texte, sections[i]);
                Ecrit(MG + LARG - Largeur(num, 1, 11) / 1000, yy, 1, 11, Gris, num);
                Rect(MG, yy + 16, LARG, 1, Filet);
                yy += 22;
            }
            int total = pages.Count;
            for (int i = 1; i < total; i++)
            {
                page = pages[i];
                Rect(MG, H - 50, LARG, 1, Filet);
                Ecrit(MG, H - 44, 1, 9, Gris, titre + " - File System OS");
                string num = "page " + (i + 1) + " / " + total;
                Ecrit(MG + LARG - Largeur(num, 1, 9) / 1000, H - 44, 1, 9, Gris, num);
            }
            return Octets();
        }

        private static string Dix(int v)
        {
            string s = v.ToString();
            while (s.Length < 10) s = "0" + s;
            return s;
        }

        private byte[] Octets()
        {
            var o = new Tampon(65536);
            var offs = new List<int>();
            o.AjouterTexte("%PDF-1.4\n%\u00e2\u00e3\u00cf\u00d3\n");
            int np = pages.Count;
            string kids = "";
            for (int i = 0; i < np; i++) kids += (i > 0 ? " " : "") + (7 + 2 * i) + " 0 R";
            Objet(o, offs, 1, "<< /Type /Catalog /Pages 2 0 R >>");
            Objet(o, offs, 2, "<< /Type /Pages /Kids [" + kids + "] /Count " + np + " >>");
            Objet(o, offs, 3, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
            Objet(o, offs, 4, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");
            Objet(o, offs, 5, "<< /Type /Font /Subtype /Type1 /BaseFont /Courier /Encoding /WinAnsiEncoding >>");
            Objet(o, offs, 6, "<< /Title (" + Echapper(titre) + ") /Author (" + Echapper(auteur) + ") /Creator (File System OS) /Producer (File System OS - PdfEcrivain) >>");
            for (int i = 0; i < np; i++)
            {
                Objet(o, offs, 7 + 2 * i, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 " + L + " " + H + "] /Resources << /Font << /F1 3 0 R /F2 4 0 R /F3 5 0 R >> >> /Contents " + (8 + 2 * i) + " 0 R >>");
                List<string> p = pages[i];
                int n = 0;
                for (int k = 0; k < p.Count; k++) n += p[k].Length + 1;
                offs.Add(o.Count);
                o.AjouterTexte((8 + 2 * i) + " 0 obj\n<< /Length " + n + " >>\nstream\n");
                for (int k = 0; k < p.Count; k++) { o.AjouterTexte(p[k]); o.Ajouter((byte)'\n'); }
                o.AjouterTexte("endstream\nendobj\n");
            }
            int xref = o.Count, total = offs.Count + 1;
            o.AjouterTexte("xref\n0 " + total + "\n0000000000 65535 f \n");
            for (int i = 0; i < offs.Count; i++) o.AjouterTexte(Dix(offs[i]) + " 00000 n \n");
            o.AjouterTexte("trailer\n<< /Size " + total + " /Root 1 0 R /Info 6 0 R >>\nstartxref\n" + xref + "\n%%EOF\n");
            return o.VersTableau();
        }

        private static void Objet(Tampon o, List<int> offs, int n, string corps)
        {
            offs.Add(o.Count);
            o.AjouterTexte(n + " 0 obj\n" + corps + "\nendobj\n");
        }
    }
}
