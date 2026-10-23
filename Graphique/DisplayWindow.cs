using System.Collections.Generic;
using System.Drawing;
using FileSystemOS.Pilotes;
using FileSystemOS.Systeme;

namespace FileSystemOS.Graphique
{
    /// <summary>Affichage : resolution (appliquee au redemarrage) et fond d'ecran.</summary>
    public class DisplayWindow : Window
    {
        private const int ListY = 40, RowH = 24, MaxRows = 7;
        // Vignettes des fonds d'ecran (coordonnees du contenu)
        private const int FondY = ListY + MaxRows * RowH + 176, SwY = FondY + 24, SwW = 76, SwH = 46, Pas = 84;
        private int survolFond = -1;
        private readonly List<string> choices = new List<string>();
        private readonly Button btnApply, btnEcrans;
        private readonly System.Collections.Generic.List<string> ecrans = new System.Collections.Generic.List<string>();
        private int selected;
        private string status = "";

        public DisplayWindow(int x, int y) : base("Parametres", x, y, 720, 600, Icons.SettingsIcon)
        {
            btnApply = new Button(Glyphs.Refresh, "Appliquer et redemarrer", 20, ListY + MaxRows * RowH + 8, 230, 30, Theme.Blue, Theme.BlueHover);
            btnEcrans = new Button(Glyphs.Refresh, "Chercher les ecrans", 250, ListY + MaxRows * RowH + 8, 200, 30, Theme.Blue, Theme.BlueHover);
        }

        public override void OnOpen()
        {
            Ecrans();
            choices.Clear();
            choices.Add("auto");
            for (int i = 0; i < Materiel.Modes.Count && choices.Count < MaxRows; i++)
            {
                string m = Materiel.Modes[i].Columns + "x" + Materiel.Modes[i].Rows;
                if (Materiel.Modes[i].Columns >= 800 && choices.IndexOf(m) < 0) choices.Add(m);
            }
            string cur = Config.Get("resolution", "auto");
            selected = choices.IndexOf(cur);
            if (selected < 0) selected = 0;
            status = "Actuelle : " + Services.ScreenW + "x" + Services.ScreenH + " (" + Materiel.Choix + ")";
        }

        protected override void DrawContent(PixelBuffer b, int cx, int cy, int cw, int ch)
        {
            b.DrawText("Resolution de l'ecran", Theme.Text, 20, cy + 14);
            for (int i = 0; i < choices.Count; i++)
            {
                int y = cy + ListY + i * RowH;
                if (i == selected) b.FillRect(Theme.RowSelected, 16, y, W - 32, RowH - 2);
                b.FillCircle(Theme.TextMuted, 30, y + 11, 6);
                b.FillCircle(Theme.WindowBg, 30, y + 11, 4);
                if (i == selected) b.FillCircle(Theme.TitleActive, 30, y + 11, 3);
                string label = choices[i] == "auto" ? "Automatique (adaptee a cet ordinateur)" : choices[i];
                b.DrawText(label, Theme.Text, 46, y + 3);
            }
            btnApply.Draw(b, 0, cy);
            btnEcrans.Draw(b, 0, cy);

            // Sorties video detectees (ecran interne, HDMI, DisplayPort, VGA)
            int ey = cy + ListY + MaxRows * RowH + 48;
            b.DrawText("Sorties video", Theme.Text, 250, ey - 4);
            for (int i = 0; i < ecrans.Count && i < 6; i++)
                b.DrawText(Shorten(ecrans[i], (W - 266) / 8), ecrans[i].StartsWith("  [branche]") ? Theme.Green : Theme.TextMuted,
                           250, ey + 14 + i * 16);

            // Fonds d'ecran : des degrades simples (instantanes), les vagues, ou vos images
            b.DrawText("Fond d'ecran : " + Shorten(Desktop.WallpaperInfo, 60), Theme.Text, 20, cy + FondY);
            string choix = Desktop.Choix().ToLower();
            for (int i = 0; i < Desktop.Fonds.Length; i++)
            {
                int x = 20 + i * Pas, y = cy + SwY;
                bool actif = choix == Desktop.Fonds[i];
                if (actif) b.FillRoundRect(Theme.Blue, x - 3, y - 3, SwW + 6, SwH + 6, 8);
                else if (i == survolFond) b.FillRoundRect(Theme.Separator, x - 2, y - 2, SwW + 4, SwH + 4, 7);
                Desktop.Degrade(b, i, x, y, SwW, SwH);
                if (Desktop.Fonds[i] == "vagues")
                {
                    b.FillRect(Color.FromArgb(118, 160, 242), x, y + 12, SwW, 7);
                    b.FillRect(Color.FromArgb(58, 108, 226), x, y + 23, SwW, 8);
                    b.FillRect(Color.FromArgb(28, 76, 206), x, y + 35, SwW, 9);
                }
                string n = Desktop.NomsFonds[i];
                b.DrawText(n, actif ? Theme.Blue : Theme.Text, x + (SwW - n.Length * 8) / 2, y + SwH + 6);
            }
            b.DrawText("Vos images (JPEG, PNG, BMP) : ouvrez-en une dans Images, puis \"Fond d'ecran\".", Theme.TextMuted, 20, cy + SwY + 76);
            b.DrawText("Elle est preparee une seule fois en arriere-plan, puis gardee prete.", Theme.TextMuted, 20, cy + SwY + 94);

            b.DrawText(Shorten(status, (W - 40) / 8), Theme.TextMuted, 20, H - 26);
        }

        private void Ecrans()
        {
            ecrans.Clear();
            var e = Affichage.Etat();
            for (int i = 0; i < e.Count && i < 7; i++) ecrans.Add(e[i]);
            Dirty = true;
        }

        /// <summary>Vignette de fond sous (lx, ly) (coordonnees du contenu), ou -1.</summary>
        private static int Vignette(int lx, int ly)
        {
            if (lx < 20 || ly < SwY || ly >= SwY + SwH) return -1;
            int i = (lx - 20) / Pas;
            if (i >= Desktop.Fonds.Length || (lx - 20) % Pas >= SwW) return -1;
            return i;
        }

        private static string Shorten(string s, int n) => s.Length > n ? s.Substring(0, n - 2) + ".." : s;

        private bool HoverBtn(Button bt, int mx, int my)
        {
            bool h = mx >= 0 && bt.Hit(X, Y + TitleH, mx, my);
            if (h == bt.Hover) return false;
            bt.Hover = h;
            return true;
        }

        public override void OnMouseMove(int mx, int my)
        {
            int s = mx < 0 ? -1 : Vignette(mx - X, my - Y - TitleH);
            if (s != survolFond) { survolFond = s; Dirty = true; }
            if (HoverBtn(btnApply, mx, my) | HoverBtn(btnEcrans, mx, my)) Dirty = true;
        }

        public override void OnMouseDown(int mx, int my)
        {
            int lx = mx - X, ly = my - Y - TitleH;
            btnApply.Pressed = btnApply.Hit(0, 0, lx, ly);
            btnEcrans.Pressed = btnEcrans.Hit(0, 0, lx, ly);
            if (ly >= ListY && ly < ListY + choices.Count * RowH && lx > 16 && lx < W - 16)
                selected = (ly - ListY) / RowH;
        }

        public override void OnMouseUp(int mx, int my)
        {
            int lx = mx - X, ly = my - Y - TitleH;
            if (btnApply.Pressed && btnApply.Hit(0, 0, lx, ly))
            {
                status = Config.SetAndSave("demarrage.cfg", "resolution", choices[selected]);
                Alimentation.Redemarrer();
            }
            int v = Vignette(lx, ly);
            if (v >= 0) { status = "Fond d'ecran : " + Desktop.NomsFonds[v] + " (" + Desktop.SetWallpaper(Desktop.Fonds[v]) + ")"; Dirty = true; }
            if (btnEcrans.Pressed && btnEcrans.Hit(0, 0, lx, ly)) { Ecrans(); status = Affichage.Branchees() + " sortie(s) branchee(s)"; }
            btnApply.Pressed = btnEcrans.Pressed = false;
        }
    }
}
