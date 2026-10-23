using System.Drawing;
using FileSystemOS.Systeme;

namespace FileSystemOS.Graphique
{
    /// <summary>Fenetre Energie : duree de fonctionnement, charge du systeme, etat de la batterie.</summary>
    public class EnergieWindow : Window
    {
        private uint dernier;

        public EnergieWindow(int x, int y) : base("Energie", x, y, 520, 300, Icons.BatteryIcon) { }

        public override void Tick()
        {
            if (Hidden) return;
            uint s = FileSystemOS.Processus.Horloge.Millis / 1000;
            if (s == dernier) return;
            dernier = s;
            Dirty = true;
        }

        protected override void DrawContent(PixelBuffer b, int cx, int cy, int cw, int ch)
        {
            // Jauge de charge du systeme
            int charge = Energie.Charge();
            b.DrawText("Charge du systeme", Theme.Text, 20, cy + 14);
            b.FillRoundRect(Theme.LightTrack, 20, cy + 36, W - 40, 22, 8);
            int larg = (W - 44) * charge / 100;
            if (larg > 4) b.FillRoundRect(charge > 80 ? Theme.Red : Theme.DotGreen, 22, cy + 38, larg, 18, 7);
            string pct = charge + " %";
            b.DrawText(pct, Theme.Text, (W - pct.Length * 8) / 2, cy + 39);

            var e = Energie.Etat();
            for (int i = 0; i < e.Count; i++)
            {
                Color col = i >= 4 ? Theme.TextMuted : Theme.Text;
                if (i == 4) col = Theme.Red;
                string l = e[i];
                int max = (W - 40) / 8;
                if (l.Length > max) l = l.Substring(0, max);
                b.DrawText(l, col, 20, cy + 76 + i * 18);
            }
        }
    }
}
