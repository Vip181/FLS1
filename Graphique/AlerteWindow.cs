using System.Drawing;
using Cosmos.System;

namespace FileSystemOS.Graphique
{
    /// <summary>Fenetre d'alerte (programme corrompu, erreur grave) avec un bouton OK.</summary>
    public class AlerteWindow : Window
    {
        private readonly Button ok;
        private string titre = "", sujet = "", detail = "";

        public AlerteWindow(int x, int y) : base("Alerte", x, y, 460, 230, Icons.AlertIcon)
        {
            ok = new Button(Glyphs.Check, "J'ai compris", 150, 130, 170, 30, Theme.Blue, Theme.BlueHover);
        }

        public void Montrer(string t, string s, string d)
        {
            titre = t; sujet = s; detail = d;
            Title = t;
            X = (Services.ScreenW - W) / 2;
            Y = (Services.ScreenH - H) / 2;
            Dirty = true;
        }

        protected override void DrawContent(PixelBuffer b, int cx, int cy, int cw, int ch)
        {
            b.FillRect(Color.FromArgb(255, 238, 238), 0, cy, W, 54);
            b.FillRect(Theme.Red, 0, cy, 5, 54);
            Glyphs.Alerte.DrawTinted(b, 20, cy + 16, Theme.Red);
            b.DrawText(titre, Theme.Red, 56, cy + 10);
            b.DrawText(Court(sujet, (W - 70) / 8), Theme.Text, 56, cy + 30);

            b.DrawText("Raison :", Theme.TextMuted, 20, cy + 70);
            int max = (W - 40) / 8;
            string d = detail;
            int ligne = 0;
            while (d.Length > 0 && ligne < 2)
            {
                int n = d.Length < max ? d.Length : max;
                b.DrawText(d.Substring(0, n), Theme.Text, 20, cy + 88 + ligne * 16);
                d = d.Substring(n);
                ligne++;
            }
            ok.Draw(b, 0, cy);
        }

        private static string Court(string s, int n) => s.Length > n ? s.Substring(0, n - 1) + "." : s;

        public override void OnMouseMove(int mx, int my)
        {
            bool h = mx >= 0 && ok.Hit(X, Y + TitleH, mx, my);
            if (h != ok.Hover) { ok.Hover = h; Dirty = true; }
        }

        public override void OnMouseDown(int mx, int my) => ok.Pressed = ok.Hit(X, Y + TitleH, mx, my);

        public override void OnMouseUp(int mx, int my)
        {
            if (ok.Pressed && ok.Hit(X, Y + TitleH, mx, my)) Services.Windows.Close(this);
            ok.Pressed = false;
        }

        public override void OnKey(KeyEvent k)
        {
            if (k.Key == ConsoleKeyEx.Enter || k.Key == ConsoleKeyEx.Escape) Services.Windows.Close(this);
        }
    }
}
