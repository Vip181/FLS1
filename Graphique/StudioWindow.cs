using System;
using System.Collections.Generic;
using System.Drawing;
using Cosmos.System;
using FileSystemOS.Fsl;
using FileSystemOS.Systeme;
using FileSystemOS.Utils;

namespace FileSystemOS.Graphique
{
    /// <summary>
    /// CREATEUR DE PROGRAMMES (le simplificateur) : on compose la fenetre de son programme a la
    /// souris, on choisit ce que fait chaque bouton dans une liste, et le code FSL est ecrit
    /// automatiquement (Fsl/Generateur.cs). Aucune ligne de code n'est necessaire.
    ///
    ///   a gauche  : les elements a poser (bouton, etiquette, champ, zone, dessin) et 4 modeles
    ///   au centre : la fenetre du futur programme ; glisser = deplacer, coin bleu = taille
    ///   a droite  : les proprietes de l'element choisi, et son action (clic ou touche Entree)
    ///   en haut   : Nouveau, Ouvrir, Enregistrer (.fsp + .fsl), Tester, Voir le code
    /// </summary>
    public class StudioWindow : Window
    {
        private const int ToolH = 42, PaletteW = 172, PanelW = 284, StatusH = 24, RangH = 32;
        private static readonly string[] Outils = { "Selection", "Bouton", "Etiquette", "Champ (une ligne)", "Zone de texte", "Dessin" };

        /// <summary>Liste cliquable : clic sur la moitie gauche = precedent, droite = suivant.</summary>
        private class ListeChoix
        {
            public int RX, RY, W;
            public const int H = 24;
            public string Libelle = "";

            public bool Hit(int lx, int ly) => lx >= RX && lx < RX + W && ly >= RY && ly < RY + H;

            public void Draw(PixelBuffer b, string valeur)
            {
                b.FillRect(Theme.PathBg, RX, RY, W, H);
                b.DrawRect(Theme.PathBorder, RX, RY, W, H);
                b.DrawText("<", Theme.Blue, RX + 6, RY + 4);
                b.DrawText(">", Theme.Blue, RX + W - 14, RY + 4);
                string t = Libelle + valeur;
                int max = (W - 40) / 8;
                if (t.Length > max) t = t.Substring(0, max);
                b.DrawText(t, Theme.Text, RX + 20, RY + 4);
            }
        }

        /// <summary>Une ligne du panneau de droite : un champ avec son libelle, ou une liste.</summary>
        private class Rang
        {
            public string Libelle = "";
            public TextField Champ;
            public ListeChoix Liste;
            public int X, Y;
        }

        private ProjetStudio projet = new ProjetStudio();
        private int outil;                               // 0 : selection ; sinon le genre a poser
        private int survolOutil = -1;
        private ElementStudio choisi;
        private string status = "Choisissez un element a gauche, puis cliquez dans la fenetre pour le poser.";
        private bool deplace, redim;
        private int prisX, prisY;

        private readonly Button bNouveau, bOuvrir, bEnregistrer, bTester, bCode, bSupprimer;
        private readonly TextField fNomProg = new TextField(0, 0, 100, false), fTitre = new TextField(0, 0, 100, false);
        private readonly TextField fLarg = new TextField(0, 0, 100, false), fHaut = new TextField(0, 0, 100, false);
        private readonly TextField fNom = new TextField(0, 0, 100, false), fTexte = new TextField(0, 0, 100, false);
        private readonly TextField fX = new TextField(0, 0, 100, false), fY = new TextField(0, 0, 100, false);
        private readonly TextField fL = new TextField(0, 0, 100, false), fH = new TextField(0, 0, 100, false);
        private readonly TextField fValeur = new TextField(0, 0, 100, false);
        private readonly ListeChoix cAction = new ListeChoix(), cCible = new ListeChoix(), cSource = new ListeChoix();
        private readonly ListeChoix cSource2 = new ListeChoix(), cOp = new ListeChoix();
        private readonly List<Rang> rangs = new List<Rang>();
        private TextField focus;
        private bool caret = true;
        private uint battement;

        public StudioWindow(int x, int y) : base("Createur de programmes", x, y, Largeur(), Hauteur(), Icons.FslApp)
        {
            int by = TitleH + 7;
            bNouveau = new Button(Glyphs.Plus, "Nouveau", 8, by, 104, 28, Theme.Blue, Theme.BlueHover);
            bOuvrir = new Button(Glyphs.Up, "Ouvrir", 118, by, 96, 28, Theme.Blue, Theme.BlueHover);
            bEnregistrer = new Button(Glyphs.Save, "Enregistrer", 220, by, 130, 28, Theme.Blue, Theme.BlueHover);
            bTester = new Button(Glyphs.Play, "Tester", 356, by, 96, 28, Theme.Green, Theme.GreenHover);
            bCode = new Button(Glyphs.Braces, "Voir le code", 458, by, 136, 28, Theme.Blue, Theme.BlueHover);
            bSupprimer = new Button("Supprimer l'element", W - PanelW + 12, H - StatusH - 40, PanelW - 24, 28, Theme.Red, Theme.RedHover);
            fNomProg.Max = 24; fTitre.Max = 40; fLarg.Max = 4; fHaut.Max = 4;
            fNom.Max = 24; fTexte.Max = 60; fX.Max = 4; fY.Max = 4; fL.Max = 4; fH.Max = 4; fValeur.Max = 200;
            Disposer();
        }

        private static int Largeur() => Services.ScreenW - 40 < 940 ? Services.ScreenW - 40 : 940;
        private static int Hauteur() => Services.ScreenH - 70 < 640 ? Services.ScreenH - 70 : 640;

        // ---- Geometrie ----
        private int ZoneY => TitleH + ToolH;
        private int PanelX => W - PanelW;
        private int FormX => PaletteW + 20;                 // coin du contenu de la fenetre dessinee
        private int FormY => ZoneY + 20 + 24;               // 24 : sa barre de titre

        private static int Aligner(int v) => v < 0 ? 0 : (v + 2) / 4 * 4;

        private static void TailleEtiquette(ElementStudio e)
        {
            if (e.Genre != ElementStudio.Etiquette) return;
            e.L = e.Texte.Length * 8 < 40 ? 40 : e.Texte.Length * 8;
            e.H = 16;
        }

        // ---- Fichiers : \FSOS\Applications\NOM.fsp (projet) et NOM.fsl (code) ----

        private static string Dossier() =>
            TextCodec.PathJoin(TextCodec.PathJoin(Live.Volume ?? "0:\\", "FSOS"), "Applications");

        private string Base()
        {
            string n = projet.Nom.Trim();
            char[] r = new char[n.Length];
            for (int i = 0; i < n.Length; i++)
            {
                char c = n[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-';
                r[i] = ok ? c : '_';
            }
            string s = new string(r);
            if (s.Length == 0) s = "MonProgramme";
            return TextCodec.PathJoin(Dossier(), s);
        }

        private static void Ecrire(string chemin, string texte)
        {
            byte[] o = new byte[texte.Length];
            for (int i = 0; i < texte.Length; i++) o[i] = texte[i] < 256 ? (byte)texte[i] : (byte)'?';
            FsTools.WriteBytes(chemin, o);
        }

        private bool Enregistrer()
        {
            try
            {
                FsTools.MakeDir(Dossier());
                string b = Base();
                Ecrire(b + ".fsp", projet.Sauver());
                Ecrire(b + ".fsl", projet.GenererCode());
                status = "Enregistre : " + TextCodec.FileName(b) + ".fsp (projet) et .fsl (programme) dans Applications";
                return true;
            }
            catch (Exception e) { status = "Erreur d'enregistrement : " + e.Message; return false; }
        }

        private void Tester()
        {
            if (!Enregistrer()) return;
            Launcher.Run(Base() + ".fsl", out string m);
            status = m;
        }

        private void VoirCode()
        {
            if (!Enregistrer()) return;
            Services.EditorWin.Load(Base() + ".fsl");
            Services.Windows.Open(Services.EditorWin);
        }

        private void Ouvrir()
        {
            string c = Base() + ".fsp";
            if (!FsTools.Exists(c)) { status = "Aucun projet " + TextCodec.FileName(c) + " dans Applications (tapez son nom a droite)"; return; }
            Charger(c);
        }

        /// <summary>Ouvre un projet .fsp (aussi depuis l'Explorateur ou le bureau).</summary>
        public void Charger(string chemin)
        {
            try
            {
                projet = ProjetStudio.Lire(TextCodec.Join(TextCodec.ReadLines(chemin)));
                for (int i = 0; i < projet.Elements.Count; i++) TailleEtiquette(projet.Elements[i]);
                choisi = null;
                outil = 0;
                focus = null;
                status = "Projet ouvert : " + TextCodec.FileName(chemin) + " (" + projet.Elements.Count + " element(s))";
            }
            catch (Exception e) { status = "Impossible d'ouvrir ce projet : " + e.Message; }
            Disposer();
            Dirty = true;
        }

        private void Remplacer(ProjetStudio p, string message)
        {
            projet = p;
            for (int i = 0; i < projet.Elements.Count; i++) TailleEtiquette(projet.Elements[i]);
            choisi = null;
            outil = 0;
            focus = null;
            status = message;
            Disposer();
        }

        // ---- Panneau de droite ----

        private void AjouterChamp(string libelle, TextField t, int x, int y, int w, string valeur)
        {
            t.RX = x; t.RY = y + 18; t.W = w;
            if (focus != t) t.Text = valeur;          // on ne change pas le champ en cours de saisie
            rangs.Add(new Rang { Libelle = libelle, Champ = t, X = x, Y = y });
        }

        private void AjouterListe(ListeChoix c, string libelle, int x, int y, int w)
        {
            c.RX = x; c.RY = y; c.W = w; c.Libelle = libelle;
            rangs.Add(new Rang { Liste = c, X = x, Y = y });
        }

        /// <summary>Place les champs du panneau selon l'element choisi et son action.</summary>
        private void Disposer()
        {
            rangs.Clear();
            int x = PanelX + 12, w = PanelW - 24, d = (w - 8) / 2, y = ZoneY + 36;
            if (choisi == null)
            {
                AjouterChamp("Nom du programme (fichier)", fNomProg, x, y, w, projet.Nom); y += 50;
                AjouterChamp("Titre de la fenetre", fTitre, x, y, w, projet.Titre); y += 50;
                AjouterChamp("Largeur", fLarg, x, y, d, projet.Largeur.ToString());
                AjouterChamp("Hauteur", fHaut, x + d + 8, y, d, projet.Hauteur.ToString());
                Dirty = true;
                return;
            }

            ElementStudio e = choisi;
            AjouterChamp("Nom (pour les actions)", fNom, x, y, w, e.Nom); y += 50;
            if (e.Genre != ElementStudio.Dessin)
            {
                AjouterChamp(e.Genre == ElementStudio.Champ || e.Genre == ElementStudio.Zone ? "Texte de depart" : "Texte", fTexte, x, y, w, e.Texte);
                y += 50;
            }
            AjouterChamp("X", fX, x, y, d, e.X.ToString());
            AjouterChamp("Y", fY, x + d + 8, y, d, e.Y.ToString());
            y += 50;
            if (e.Genre != ElementStudio.Etiquette)
            {
                AjouterChamp("Largeur", fL, x, y, d, e.L.ToString());
                if (e.Genre != ElementStudio.Champ) AjouterChamp("Hauteur", fH, x + d + 8, y, d, e.H.ToString());
                y += 50;
            }
            if (!e.PeutAgir) { Dirty = true; return; }

            y += 4;
            AjouterListe(cAction, e.Genre == ElementStudio.Bouton ? "Au clic : " : "Touche Entree : ", x, y, w);
            y += RangH + 4;
            switch (e.Action)
            {
                case ProjetStudio.AEcrire:
                case ProjetStudio.AAjouter:
                    AjouterListe(cCible, "Dans : ", x, y, w); y += RangH;
                    AjouterListe(cSource, "Avec : ", x, y, w); y += RangH;
                    AjouterChamp("Texte a mettre avant", fValeur, x, y, w, e.Valeur);
                    break;
                case ProjetStudio.ACalculer:
                    AjouterListe(cSource, "Nombre 1 : ", x, y, w); y += RangH;
                    AjouterListe(cOp, "Operation : ", x, y, w); y += RangH;
                    AjouterListe(cSource2, "Nombre 2 : ", x, y, w); y += RangH;
                    AjouterListe(cCible, "Resultat dans : ", x, y, w);
                    break;
                case ProjetStudio.AEffacer:
                    AjouterListe(cCible, "Effacer : ", x, y, w);
                    break;
                case ProjetStudio.ACompter:
                case ProjetStudio.AZero:
                    AjouterListe(cCible, "Afficher dans : ", x, y, w); y += RangH;
                    AjouterChamp("Texte avant le nombre", fValeur, x, y, w, e.Valeur);
                    break;
                case ProjetStudio.AEnregistrer:
                    AjouterListe(cSource, "Contenu de : ", x, y, w); y += RangH;
                    AjouterChamp("Fichier (dans \\FSOS)", fValeur, x, y, w, e.Valeur);
                    break;
                case ProjetStudio.ACharger:
                    AjouterListe(cCible, "Dans : ", x, y, w); y += RangH;
                    AjouterChamp("Fichier (dans \\FSOS)", fValeur, x, y, w, e.Valeur);
                    break;
                case ProjetStudio.ACode:
                    AjouterChamp("Code FSL (ex. afficher(\"clic\");)", fValeur, x, y, w, e.Valeur);
                    break;
            }
            Dirty = true;
        }

        private string ValeurDe(ListeChoix c)
        {
            if (choisi == null) return "";
            if (c == cAction) return ProjetStudio.Actions[choisi.Action];
            if (c == cOp)
            {
                for (int i = 0; i < ProjetStudio.Operations.Length; i++) if (choisi.Valeur == ProjetStudio.Operations[i]) return choisi.Valeur;
                return "+";
            }
            string v = c == cCible ? choisi.Cible : (c == cSource ? choisi.Source : choisi.Source2);
            return v.Length == 0 ? "(aucun)" : v;
        }

        private List<string> Options(ListeChoix c)
        {
            var l = new List<string>();
            if (c == cAction) { for (int i = 0; i < ProjetStudio.Actions.Length; i++) l.Add(ProjetStudio.Actions[i]); return l; }
            if (c == cOp) { for (int i = 0; i < ProjetStudio.Operations.Length; i++) l.Add(ProjetStudio.Operations[i]); return l; }
            l.Add("(aucun)");
            for (int i = 0; i < projet.Elements.Count; i++)
                if (projet.Elements[i].Genre != ElementStudio.Dessin && !Listes.Contient(l, projet.Elements[i].Nom)) l.Add(projet.Elements[i].Nom);
            return l;
        }

        /// <summary>Valeur suivante (sens = 1) ou precedente (-1) d'une liste.</summary>
        private void Tourner(ListeChoix c, int sens)
        {
            if (choisi == null) return;
            List<string> l = Options(c);
            int i = Listes.Indice(l, ValeurDe(c));
            if (i < 0) i = 0;
            i = (i + sens + l.Count) % l.Count;
            if (c == cAction)
            {
                choisi.Action = i;
                // Une valeur adaptee a la nouvelle action
                if (i == ProjetStudio.ACalculer) choisi.Valeur = "+";
                else if (i == ProjetStudio.AEnregistrer || i == ProjetStudio.ACharger) choisi.Valeur = "Documents\\notes.txt";
                else if (i == ProjetStudio.ACode) choisi.Valeur = "afficher(\"clic\");";
                else choisi.Valeur = "";
                status = "Action : " + ProjetStudio.Actions[i];
            }
            else if (c == cOp) choisi.Valeur = l[i];
            else
            {
                string v = i == 0 ? "" : l[i];
                if (c == cCible) choisi.Cible = v;
                else if (c == cSource) choisi.Source = v;
                else choisi.Source2 = v;
            }
            Disposer();
        }

        /// <summary>Recopie un champ du panneau dans le projet, a chaque touche.</summary>
        private void Appliquer(TextField t)
        {
            if (t == fNomProg) projet.Nom = t.Text;
            else if (t == fTitre) projet.Titre = t.Text;
            else if (t == fLarg) projet.Largeur = Borne(ProjetStudio.Nombre(t.Text, projet.Largeur), 120, 1600);
            else if (t == fHaut) projet.Hauteur = Borne(ProjetStudio.Nombre(t.Text, projet.Hauteur), 60, 1000);
            else if (choisi != null)
            {
                ElementStudio e = choisi;
                if (t == fNom)
                {
                    // Les actions qui visaient l'ancien nom suivent le nouveau
                    string ancien = e.Nom;
                    e.Nom = t.Text;
                    for (int i = 0; i < projet.Elements.Count; i++)
                    {
                        ElementStudio a = projet.Elements[i];
                        if (a.Cible == ancien) a.Cible = e.Nom;
                        if (a.Source == ancien) a.Source = e.Nom;
                        if (a.Source2 == ancien) a.Source2 = e.Nom;
                    }
                }
                else if (t == fTexte) { e.Texte = t.Text; TailleEtiquette(e); }
                else if (t == fX) e.X = Borne(ProjetStudio.Nombre(t.Text, e.X), 0, 1600);
                else if (t == fY) e.Y = Borne(ProjetStudio.Nombre(t.Text, e.Y), 0, 1000);
                else if (t == fL) e.L = Borne(ProjetStudio.Nombre(t.Text, e.L), 8, 1600);
                else if (t == fH) e.H = Borne(ProjetStudio.Nombre(t.Text, e.H), 8, 1000);
                else if (t == fValeur) e.Valeur = t.Text;
            }
            Dirty = true;
        }

        private static int Borne(int v, int a, int b) => v < a ? a : (v > b ? b : v);

        // ---- Elements de la fenetre composee ----

        private ElementStudio Creer(int genre, int x, int y)
        {
            string n = projet.NomLibre(genre);
            ElementStudio e;
            switch (genre)
            {
                case ElementStudio.Bouton: e = new ElementStudio(genre, n, "Bouton", x, y, 110, 32); break;
                case ElementStudio.Etiquette: e = new ElementStudio(genre, n, "Texte", x, y, 40, 16); break;
                case ElementStudio.Champ: e = new ElementStudio(genre, n, "", x, y, 200, 26); break;
                case ElementStudio.Zone: e = new ElementStudio(genre, n, "", x, y, 240, 120); break;
                default: e = new ElementStudio(genre, n, "", x, y, 200, 140); break;
            }
            TailleEtiquette(e);
            return e;
        }

        private ElementStudio Sous(int fx, int fy)
        {
            for (int i = projet.Elements.Count - 1; i >= 0; i--)
            {
                ElementStudio e = projet.Elements[i];
                if (fx >= e.X && fx < e.X + e.L && fy >= e.Y && fy < e.Y + e.H) return e;
            }
            return null;
        }

        private static bool Redimensionnable(ElementStudio e) => e.Genre != ElementStudio.Etiquette;

        private static bool SurPoignee(ElementStudio e, int fx, int fy) =>
            Redimensionnable(e) && fx >= e.X + e.L - 7 && fx <= e.X + e.L + 5 && fy >= e.Y + e.H - 7 && fy <= e.Y + e.H + 5;

        private void Supprimer()
        {
            if (choisi == null) return;
            status = "'" + choisi.Nom + "' supprime";
            projet.Elements.Remove(choisi);
            choisi = null;
            focus = null;
            Disposer();
        }

        // ---- Dessin ----

        protected override void DrawContent(PixelBuffer b, int cx, int cy, int cw, int ch)
        {
            // Zone de travail et fenetre composee
            b.FillRect(Theme.ToolbarBg, PaletteW, ZoneY, PanelX - PaletteW, H - ZoneY - StatusH);
            int fw = projet.Largeur, fh = projet.Hauteur;
            b.FillRect(Theme.ButtonShadow, FormX + 4, FormY - 24 + 5, fw, fh + 24);
            b.FillRect(Theme.TitleActive, FormX, FormY - 24, fw, 24);
            b.DrawText(Couper(projet.Titre, (fw - 16) / 8), Theme.TitleText, FormX + 8, FormY - 20);
            b.FillRect(Theme.WindowBg, FormX, FormY, fw, fh);
            for (int gy = 0; gy < fh; gy += 16)
                for (int gx = 0; gx < fw; gx += 16) b.SetPixel(FormX + gx, FormY + gy, PixelBuffer.Argb(Theme.Separator));

            for (int i = 0; i < projet.Elements.Count; i++) DessinerElement(b, projet.Elements[i]);

            // Palette (a gauche)
            b.FillRect(Theme.SidebarBg, 0, ZoneY, PaletteW, H - ZoneY - StatusH);
            b.DrawText("Elements", Theme.TextMuted, 12, ZoneY + 8);
            for (int i = 0; i < Outils.Length; i++)
            {
                int y = ZoneY + 30 + i * 30;
                if (i == outil) b.FillRect(Theme.RowSelected, 6, y - 4, PaletteW - 12, 26);
                else if (i == survolOutil) b.FillRect(Theme.RowHover, 6, y - 4, PaletteW - 12, 26);
                b.DrawText(Outils[i], Theme.Text, 14, y);
            }
            int my = ZoneY + 30 + Outils.Length * 30 + 14;
            b.DrawText("Modeles", Theme.TextMuted, 12, my);
            for (int i = 0; i < ProjetStudio.Modeles.Length; i++)
            {
                int y = my + 22 + i * 30;
                if (survolOutil == 100 + i) b.FillRect(Theme.RowHover, 6, y - 4, PaletteW - 12, 26);
                b.DrawText(ProjetStudio.Modeles[i], Theme.Blue, 14, y);
            }

            // Panneau des proprietes (a droite)
            b.FillRect(Theme.SidebarBg, PanelX, ZoneY, PanelW, H - ZoneY - StatusH);
            b.FillRect(Theme.Separator, PanelX, ZoneY, 1, H - ZoneY - StatusH);
            string titre = choisi == null ? "Programme" : ElementStudio.NomGenre(choisi.Genre) + " '" + choisi.Nom + "'";
            b.DrawText(Couper(titre, (PanelW - 24) / 8), Theme.Text, PanelX + 12, ZoneY + 10);
            for (int i = 0; i < rangs.Count; i++)
            {
                Rang r = rangs[i];
                if (r.Champ != null)
                {
                    b.DrawText(r.Libelle, Theme.TextMuted, r.X, r.Y);
                    r.Champ.Draw(b, focus == r.Champ, caret);
                }
                else r.Liste.Draw(b, ValeurDe(r.Liste));
            }
            if (choisi == null)
            {
                string[] aide =
                {
                    "1. Choisissez un element a gauche",
                    "2. Cliquez dans la fenetre pour",
                    "   le poser",
                    "3. Glissez-le pour le deplacer ;",
                    "   le coin bleu change sa taille",
                    "4. Choisissez l'action de chaque",
                    "   bouton a droite",
                    "5. Tester : le programme se lance",
                    "",
                    "Suppr : efface l'element choisi"
                };
                int ay = ZoneY + 200;
                for (int i = 0; i < aide.Length; i++) b.DrawText(aide[i], Theme.TextMuted, PanelX + 12, ay + i * 18);
            }
            else bSupprimer.Draw(b, 0, 0);

            // Barre d'outils (en haut) et barre d'etat (en bas)
            b.FillRect(Theme.ToolbarBg, 0, TitleH, W, ToolH);
            b.FillRect(Theme.Separator, 0, TitleH + ToolH - 1, W, 1);
            bNouveau.Draw(b, 0, 0);
            bOuvrir.Draw(b, 0, 0);
            bEnregistrer.Draw(b, 0, 0);
            bTester.Draw(b, 0, 0);
            bCode.Draw(b, 0, 0);
            b.FillRect(Theme.StatusBg, 0, H - StatusH, W, StatusH);
            b.DrawText(Couper(status, (W - 16) / 8), Theme.TextMuted, 8, H - StatusH + 4);
        }

        private void DessinerElement(PixelBuffer b, ElementStudio e)
        {
            int x = FormX + e.X, y = FormY + e.Y;
            switch (e.Genre)
            {
                case ElementStudio.Bouton:
                    {
                        b.FillRoundRect(Theme.Blue, x, y, e.L, e.H, 7);
                        string t = Couper(e.Texte, (e.L - 8) / 8);
                        b.DrawText(t, Color.White, x + (e.L - t.Length * 8) / 2, y + (e.H - 16) / 2);
                        break;
                    }
                case ElementStudio.Etiquette:
                    if (e.Texte.Length > 0) b.DrawText(e.Texte, Theme.Text, x, y);
                    else b.DrawText("(vide)", Theme.TextMuted, x, y);
                    break;
                case ElementStudio.Champ:
                case ElementStudio.Zone:
                    {
                        b.FillRect(Theme.TextMuted, x - 1, y - 1, e.L + 2, e.H + 2);
                        b.FillRect(Color.White, x, y, e.L, e.H);
                        string t = Couper(e.Texte, (e.L - 12) / 8);
                        int nl = t.IndexOf('\n');
                        if (nl >= 0) t = t.Substring(0, nl);
                        b.DrawText(t, Theme.Text, x + 6, y + (e.Genre == ElementStudio.Champ ? 5 : 4));
                        if (e.Texte.Length == 0)
                            b.DrawText(e.Genre == ElementStudio.Champ ? "champ" : "zone de texte", Theme.Separator, x + 6, y + (e.Genre == ElementStudio.Champ ? 5 : 4));
                        break;
                    }
                default:
                    b.FillRect(Color.FromArgb(232, 236, 244), x, y, e.L, e.H);
                    b.DrawRect(Theme.TextMuted, x, y, e.L, e.H);
                    b.DrawText("dessin", Theme.TextMuted, x + (e.L - 48) / 2, y + (e.H - 16) / 2);
                    break;
            }
            // Point vert : cet element declenche une action
            if (e.PeutAgir && e.Action != ProjetStudio.ARien) b.FillRect(Theme.Green, x + e.L - 6, y - 3, 6, 6);
            if (e == choisi)
            {
                b.DrawRect(Theme.Blue, x - 3, y - 3, e.L + 6, e.H + 6);
                b.DrawRect(Theme.Blue, x - 2, y - 2, e.L + 4, e.H + 4);
                if (Redimensionnable(e)) b.FillRect(Theme.Blue, x + e.L - 3, y + e.H - 3, 8, 8);
            }
        }

        private static string Couper(string s, int n) => n <= 0 ? "" : (s.Length > n ? s.Substring(0, n) : s);

        // ---- Souris ----

        public override void OnMouseMove(int mx, int my)
        {
            if (mx < 0) { survolOutil = -1; Dirty = true; return; }
            bNouveau.Hover = bNouveau.Hit(X, Y, mx, my);
            bOuvrir.Hover = bOuvrir.Hit(X, Y, mx, my);
            bEnregistrer.Hover = bEnregistrer.Hit(X, Y, mx, my);
            bTester.Hover = bTester.Hit(X, Y, mx, my);
            bCode.Hover = bCode.Hit(X, Y, mx, my);
            bSupprimer.Hover = choisi != null && bSupprimer.Hit(X, Y, mx, my);
            survolOutil = LignePalette(mx - X, my - Y);
            Dirty = true;
        }

        /// <summary>Ligne de la palette sous la souris : 0-5 outils, 100+ modeles, -1 rien.</summary>
        private int LignePalette(int lx, int ly)
        {
            if (lx < 0 || lx >= PaletteW || ly < ZoneY + 26) return -1;
            int i = (ly - (ZoneY + 26)) / 30;
            if (i < Outils.Length) return i;
            int my = ZoneY + 30 + Outils.Length * 30 + 14 + 22 - 4;
            if (ly < my) return -1;
            int k = (ly - my) / 30;
            return k < ProjetStudio.Modeles.Length ? 100 + k : -1;
        }

        public override void OnMouseDown(int mx, int my)
        {
            int lx = mx - X, ly = my - Y;
            if (bNouveau.Hit(X, Y, mx, my)) { Remplacer(new ProjetStudio(), "Nouveau programme : posez des elements dans la fenetre"); return; }
            if (bOuvrir.Hit(X, Y, mx, my)) { Ouvrir(); Dirty = true; return; }
            if (bEnregistrer.Hit(X, Y, mx, my)) { Enregistrer(); Dirty = true; return; }
            if (bTester.Hit(X, Y, mx, my)) { Tester(); Dirty = true; return; }
            if (bCode.Hit(X, Y, mx, my)) { VoirCode(); Dirty = true; return; }
            if (ly < ZoneY || ly >= H - StatusH) return;

            // Palette
            if (lx < PaletteW)
            {
                focus = null;
                int p = LignePalette(lx, ly);
                if (p >= 0 && p < Outils.Length)
                {
                    outil = p;
                    status = p == 0 ? "Selection : cliquez sur un element pour le choisir" : "Cliquez dans la fenetre pour poser : " + Outils[p];
                }
                else if (p >= 100)
                    Remplacer(ProjetStudio.Modele(p - 100), "Modele '" + ProjetStudio.Modeles[p - 100] + "' charge : cliquez sur Tester");
                Dirty = true;
                return;
            }

            // Panneau de droite
            if (lx >= PanelX)
            {
                if (choisi != null && bSupprimer.Hit(X, Y, mx, my)) { Supprimer(); return; }
                for (int i = 0; i < rangs.Count; i++)
                {
                    Rang r = rangs[i];
                    if (r.Champ != null && r.Champ.Hit(lx, ly)) { focus = r.Champ; caret = true; Dirty = true; return; }
                    if (r.Liste != null && r.Liste.Hit(lx, ly)) { focus = null; Tourner(r.Liste, lx < r.Liste.RX + r.Liste.W / 2 ? -1 : 1); return; }
                }
                focus = null;
                Dirty = true;
                return;
            }

            // Fenetre composee
            focus = null;
            int fx = lx - FormX, fy = ly - FormY;
            if (outil != 0)
            {
                if (fx < 0 || fy < 0 || fx >= projet.Largeur || fy >= projet.Hauteur)
                {
                    status = "Cliquez a l'interieur de la fenetre pour poser l'element";
                    Dirty = true;
                    return;
                }
                ElementStudio n = Creer(outil, Aligner(fx), Aligner(fy));
                projet.Elements.Add(n);
                choisi = n;
                status = "'" + n.Nom + "' pose" + (n.PeutAgir ? " : choisissez son action a droite" : "");
                outil = 0;
                Disposer();
                return;
            }
            if (choisi != null && SurPoignee(choisi, fx, fy)) { redim = true; return; }
            ElementStudio e = Sous(fx, fy);
            if (e != null)
            {
                choisi = e;
                deplace = true;
                prisX = fx - e.X;
                prisY = fy - e.Y;
            }
            else choisi = null;
            Disposer();
        }

        public override void OnMouseDrag(int mx, int my)
        {
            if (choisi == null || (!deplace && !redim)) return;
            int fx = mx - X - FormX, fy = my - Y - FormY;
            if (deplace)
            {
                choisi.X = Borne(Aligner(fx - prisX), 0, projet.Largeur - 8);
                choisi.Y = Borne(Aligner(fy - prisY), 0, projet.Hauteur - 8);
            }
            else
            {
                choisi.L = Borne(Aligner(fx - choisi.X), 16, 1600);
                if (choisi.Genre != ElementStudio.Champ) choisi.H = Borne(Aligner(fy - choisi.Y), 16, 1000);
            }
            Disposer();
        }

        public override void OnMouseUp(int mx, int my)
        {
            deplace = false;
            redim = false;
        }

        // ---- Clavier ----

        public override void Tick()
        {
            if (Hidden || focus == null) return;
            uint t = FileSystemOS.Processus.Horloge.Millis;
            if (t - battement >= 500) { battement = t; caret = !caret; Dirty = true; }
        }

        public override void OnKey(KeyEvent k)
        {
            if (focus != null)
            {
                if (k.Key == ConsoleKeyEx.Enter || k.Key == ConsoleKeyEx.Escape) { focus = null; Disposer(); return; }
                if (k.Key == ConsoleKeyEx.Tab) { focus = Suivant(focus); caret = true; Dirty = true; return; }
                if (focus.Key(k)) { Appliquer(focus); caret = true; }
                return;
            }
            if (choisi == null) return;
            if (k.Key == ConsoleKeyEx.Delete) { Supprimer(); return; }
            if (k.Key == ConsoleKeyEx.Escape) { choisi = null; Disposer(); return; }
            int dx = k.Key == ConsoleKeyEx.LeftArrow ? -4 : (k.Key == ConsoleKeyEx.RightArrow ? 4 : 0);
            int dy = k.Key == ConsoleKeyEx.UpArrow ? -4 : (k.Key == ConsoleKeyEx.DownArrow ? 4 : 0);
            if (dx != 0 || dy != 0)
            {
                choisi.X = Borne(choisi.X + dx, 0, projet.Largeur - 8);
                choisi.Y = Borne(choisi.Y + dy, 0, projet.Hauteur - 8);
                Disposer();
            }
        }

        /// <summary>Champ suivant du panneau (touche Tab).</summary>
        private TextField Suivant(TextField t)
        {
            int k = -1;
            var champs = new List<TextField>();
            for (int i = 0; i < rangs.Count; i++)
                if (rangs[i].Champ != null)
                {
                    if (rangs[i].Champ == t) k = champs.Count;
                    champs.Add(rangs[i].Champ);
                }
            if (champs.Count == 0) return null;
            return champs[(k + 1) % champs.Count];
        }
    }
}
