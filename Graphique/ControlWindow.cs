using Sys = Cosmos.System;

namespace FileSystemOS.Graphique
{
    public class ControlWindow : Window
    {
        private readonly Button stop;
        private readonly Button reboot;

        public ControlWindow(int x, int y) : base("Alimentation", x, y, 270, 150, Icons.PowerIcon)
        {
            stop = new Button(Glyphs.Power, "Arreter", 14, 54, 112, 38, Theme.Red, Theme.RedHover);
            reboot = new Button(Glyphs.Refresh, "Redemarrer", 132, 54, 124, 38, Theme.Blue, Theme.BlueHover);
        }

        // Origine du contenu a l'ecran (pour les clics)
        private int CX => X;
        private int CY => Y + TitleH;

        protected override void DrawContent(PixelBuffer b, int cx, int cy, int cw, int ch)
        {
            b.DrawText("Que souhaitez-vous faire ?", Theme.Text, cx + 20, cy + 18);
            stop.Draw(b, cx, cy);
            reboot.Draw(b, cx, cy);
        }

        public override void OnMouseMove(int mx, int my)
        {
            bool s = stop.Hit(CX, CY, mx, my), r = reboot.Hit(CX, CY, mx, my);
            if (s != stop.Hover || r != reboot.Hover)
            {
                stop.Hover = s;
                reboot.Hover = r;
                Dirty = true;
            }
        }

        public override void OnMouseDown(int mx, int my)
        {
            stop.Pressed = stop.Hit(CX, CY, mx, my);
            reboot.Pressed = reboot.Hit(CX, CY, mx, my);
        }

        public override void OnMouseUp(int mx, int my)
        {
            if (stop.Pressed && stop.Hit(CX, CY, mx, my)) FileSystemOS.Systeme.Alimentation.Arreter();
            if (reboot.Pressed && reboot.Hit(CX, CY, mx, my)) FileSystemOS.Systeme.Alimentation.Redemarrer();
            stop.Pressed = false;
            reboot.Pressed = false;
        }
    }
}
