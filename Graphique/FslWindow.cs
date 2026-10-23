using System.Collections.Generic;
using System.Drawing;
using Cosmos.System;

namespace FileSystemOS.Graphique
{
    /// <summary>Un element d'une fenetre FSL : bouton, etiquette, champ, zone de texte ou dessin.</summary>
    public class ControleFsl
    {
        public const int Bouton = 1, Etiquette = 2, Champ = 3, Zone = 4, Dessin = 5;
        public int Id, Genre, X, Y, L, H;
        public string Texte = "";
        public PixelBuffer Image;          // pour un dessin
        public bool Survol, Appuye;
    }

    /// <summary>
    /// FENETRE D'UN PROGRAMME FSL. Ses elements sont crees par le programme (bouton, champ,
    /// zone, etiquette, dessin) ; les clics et les touches deviennent des EVENEMENTS que le
    /// programme lit avec evenement() : "clic:3", "touche:a", "valider:4", "souris:5:10:20"...
    /// </summary>
    public class FslWindow : Window
    {
        public readonly int Id;
        public readonly List<ControleFsl> Controles = new List<ControleFsl>();
        public readonly List<string> Evenements;       // file du programme proprietaire
        private ControleFsl focus;
        private bool caret = true;
        private uint dernierBattement;

        public FslWindow(int id, string titre, int w, int h, List<string> evenements)
            : base(titre, 80 + (id % 6) * 30, 70 + (id % 6) * 24, w, h + TitleH, Icons.FslApp)
        {
            Id = id;
            Evenements = evenements;
        }

        private void Evenement(string e)
        {
            if (Evenements.Count < 256) Evenements.Add(e);
        }

        public override void Tick()
        {
            if (Hidden) return;
            uint t = FileSystemOS.Processus.Horloge.Millis;
            if (focus != null && (focus.Genre == ControleFsl.Champ || focus.Genre == ControleFsl.Zone) && t - dernierBattement >= 500)
            {
                dernierBattement = t;
                caret = !caret;
                Dirty = true;
            }
        }

        protected override void DrawContent(PixelBuffer b, int cx, int cy, int cw, int ch)
        {
            for (int i = 0; i < Controles.Count; i++)
            {
                ControleFsl c = Controles[i];
                int x = c.X, y = cy + c.Y;
                switch (c.Genre)
                {
                    case ControleFsl.Bouton:
                        b.FillRoundRect(c.Appuye ? Theme.TitleActive : (c.Survol ? Theme.BlueHover : Theme.Blue), x, y, c.L, c.H, 7);
                        string t = Couper(c.Texte, (c.L - 8) / 8);
                        b.DrawText(t, Color.White, x + (c.L - t.Length * 8) / 2, y + (c.H - 16) / 2);
                        break;

                    case ControleFsl.Etiquette:
                        b.DrawText(c.Texte, Theme.Text, x, y);
                        break;

                    case ControleFsl.Champ:
                        Cadre(b, c, x, y);
                        string vis = c.Texte;
                        int max = (c.L - 12) / 8;
                        if (vis.Length > max) vis = vis.Substring(vis.Length - max);       // on voit la fin
                        b.DrawText(vis, Theme.Text, x + 6, y + (c.H - 16) / 2);
                        if (c == focus && caret) b.FillRect(Theme.Text, x + 6 + vis.Length * 8, y + 4, 2, c.H - 8);
                        break;

                    case ControleFsl.Zone:
                        Cadre(b, c, x, y);
                        DessinerZone(b, c, x, y);
                        break;

                    case ControleFsl.Dessin:
                        if (c.Image != null) b.Blit(c.Image, x, y);
                        break;
                }
            }
        }

        private void Cadre(PixelBuffer b, ControleFsl c, int x, int y)
        {
            b.FillRect(c == focus ? Theme.Blue : Theme.TextMuted, x - 1, y - 1, c.L + 2, c.H + 2);
            b.FillRect(Color.White, x, y, c.L, c.H);
        }

        /// <summary>Zone de texte : plusieurs lignes, coupees a la largeur, les dernieres visibles.</summary>
        private void DessinerZone(PixelBuffer b, ControleFsl c, int x, int y)
        {
            int parLigne = (c.L - 12) / 8, nbLignes = (c.H - 8) / 18;
            if (parLigne < 1 || nbLignes < 1) return;
            var lignes = new List<string>();
            string reste = c.Texte;
            while (true)
            {
                int nl = reste.IndexOf('\n');
                string l = nl >= 0 ? reste.Substring(0, nl) : reste;
                while (l.Length > parLigne) { lignes.Add(l.Substring(0, parLigne)); l = l.Substring(parLigne); }
                lignes.Add(l);
                if (nl < 0) break;
                reste = reste.Substring(nl + 1);
            }
            int debut = lignes.Count > nbLignes ? lignes.Count - nbLignes : 0;
            for (int i = debut; i < lignes.Count; i++)
                b.DrawText(lignes[i], Theme.Text, x + 6, y + 4 + (i - debut) * 18);
            if (c == focus && caret)
            {
                string der = lignes[lignes.Count - 1];
                b.FillRect(Theme.Text, x + 6 + der.Length * 8, y + 4 + (lignes.Count - 1 - debut) * 18, 2, 16);
            }
        }

        private static string Couper(string s, int n) => n <= 0 ? "" : (s.Length > n ? s.Substring(0, n) : s);

        private ControleFsl Sous(int mx, int my)
        {
            int lx = mx - X, ly = my - (Y + TitleH);
            for (int i = Controles.Count - 1; i >= 0; i--)
            {
                ControleFsl c = Controles[i];
                if (lx >= c.X && lx < c.X + c.L && ly >= c.Y && ly < c.Y + c.H) return c;
            }
            return null;
        }

        public override void OnMouseMove(int mx, int my)
        {
            ControleFsl s = Sous(mx, my);
            bool change = false;
            for (int i = 0; i < Controles.Count; i++)
            {
                ControleFsl c = Controles[i];
                bool h = c == s && c.Genre == ControleFsl.Bouton;
                if (h != c.Survol) { c.Survol = h; change = true; }
            }
            if (change) Dirty = true;
        }

        public override void OnMouseDown(int mx, int my)
        {
            ControleFsl c = Sous(mx, my);
            if (c == null) return;
            if (c.Genre == ControleFsl.Bouton) c.Appuye = true;
            else if (c.Genre == ControleFsl.Champ || c.Genre == ControleFsl.Zone) focus = c;
            else if (c.Genre == ControleFsl.Dessin)
                Evenement("souris:" + c.Id + ":" + (mx - X - c.X) + ":" + (my - Y - TitleH - c.Y));
            Dirty = true;
        }

        public override void OnMouseUp(int mx, int my)
        {
            ControleFsl s = Sous(mx, my);
            for (int i = 0; i < Controles.Count; i++)
            {
                ControleFsl c = Controles[i];
                if (c.Appuye && c == s) Evenement("clic:" + c.Id);
                c.Appuye = false;
            }
            Dirty = true;
        }

        public override void OnKey(KeyEvent k)
        {
            // Un champ ou une zone a le clavier : on ecrit dedans
            if (focus != null && (focus.Genre == ControleFsl.Champ || focus.Genre == ControleFsl.Zone))
            {
                if (k.Key == ConsoleKeyEx.Backspace)
                {
                    if (focus.Texte.Length > 0) focus.Texte = focus.Texte.Substring(0, focus.Texte.Length - 1);
                }
                else if (k.Key == ConsoleKeyEx.Enter)
                {
                    if (focus.Genre == ControleFsl.Zone) focus.Texte += "\n";
                    else Evenement("valider:" + focus.Id);
                }
                else if (k.Key == ConsoleKeyEx.Escape) focus = null;
                else if (k.KeyChar >= ' ' && k.KeyChar <= '~' && focus.Texte.Length < 4000) focus.Texte += k.KeyChar;
                caret = true;
                Dirty = true;
                return;
            }
            Evenement("touche:" + NomTouche(k));
        }

        public static string NomTouche(KeyEvent k)
        {
            switch (k.Key)
            {
                case ConsoleKeyEx.LeftArrow: return "gauche";
                case ConsoleKeyEx.RightArrow: return "droite";
                case ConsoleKeyEx.UpArrow: return "haut";
                case ConsoleKeyEx.DownArrow: return "bas";
                case ConsoleKeyEx.Enter: return "entree";
                case ConsoleKeyEx.Escape: return "echap";
                case ConsoleKeyEx.Backspace: return "retour";
                case ConsoleKeyEx.Spacebar: return "espace";
                case ConsoleKeyEx.Tab: return "tab";
            }
            if (k.KeyChar > ' ' && k.KeyChar <= '~') return k.KeyChar.ToString();
            return "autre";
        }
    }
}
