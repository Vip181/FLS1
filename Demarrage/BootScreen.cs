using Cosmos.System.Graphics;
using System.Collections.Generic;
using System.Drawing;
using Cosmos.System.Graphics.Fonts;
using FileSystemOS.Graphique;

namespace FileSystemOS.Demarrage
{
    /// <summary>Ecran de demarrage (et d'arret) : logo V anime, nom, auteur, progression.</summary>
    public class BootScreen
    {
        public static readonly Color Bg = Color.FromArgb(14, 15, 22);
        private static readonly Color Muted = Color.FromArgb(110, 114, 132);
        private static readonly Color Light = Color.FromArgb(205, 208, 220);
        private static readonly Color Track = Color.FromArgb(34, 36, 50);
        private static readonly Color Fill = Color.FromArgb(150, 154, 170);

        private const int LogoSize = 170, BarW = 360;
        private int LogoY => s.H / 2 - 110;   // tout est place par rapport au centre :
        private int BarY => s.H / 2 + 156;    // fonctionne a toutes les resolutions

        private readonly PixelBuffer s = Services.Compositor.Screen;

        public void Begin(string subtitle)
        {
            s.Clear(Bg);
            int w = s.W;
            int y = s.H / 2;
            DrawScaled(Services.OsName, Light, (w - Services.OsName.Length * 24) / 2, y + 16, 3);
            Center(Services.Company, Muted, y + 80);
            Center("par " + Services.Author, Muted, y + 100);
            Center(subtitle, Light, y + 126);
        }

        public void Frame(uint frame, int done, int total, string label, List<string> log)
        {
            VLogo.Draw(s, s.W / 2, LogoY, LogoSize, frame, Bg);

            int bx = (s.W - BarW) / 2;
            s.FillRoundRect(Track, bx, BarY, BarW, 6, 3);
            int fw = total <= 0 ? 0 : BarW * done / total;
            if (fw > 6) s.FillRoundRect(Fill, bx, BarY, fw, 6, 3);

            s.FillRect(Bg, 0, BarY + 14, s.W, 132);
            Center(label, Light, BarY + 18);
            if (log != null)
            {
                int first = log.Count > 4 ? log.Count - 4 : 0;
                for (int i = first; i < log.Count; i++)
                {
                    string l = log[i];
                    if (l.Length > 90) l = l.Substring(0, 90);
                    Center(l, Muted, BarY + 44 + (i - first) * 18);
                }
            }
            // Une erreur pendant le chargement : visible tout de suite (et sur une photo)
            if (FileSystemOS.Noyau.Diagnostic.ErrorCount > 0 && FileSystemOS.Noyau.Diagnostic.LastError != null)
            {
                string e = "Erreur : " + FileSystemOS.Noyau.Diagnostic.LastError;
                if (e.Length > 100) e = e.Substring(0, 100);
                Center(e, Color.FromArgb(255, 125, 115), BarY + 44 + 4 * 18);
            }

            Services.Canvas.DrawImage(Services.Compositor.ScreenBitmap, 0, 0);
            Services.Canvas.Display();
        }

        private void Center(string t, Color c, int y) => s.DrawText(t, c, (s.W - t.Length * 8) / 2, y);

        /// <summary>Texte agrandi (titre) dessine a partir des glyphes de la police.</summary>
        private void DrawScaled(string t, Color c, int x, int y, int scale)
        {
            Font f = Services.Font;
            int fw = f.Width, fh = f.Height, bpr = (fw + 7) / 8;
            byte[] g = f.Data;
            for (int i = 0; i < t.Length; i++)
            {
                int ch = t[i] > 255 ? '?' : t[i];
                for (int row = 0; row < fh; row++)
                    for (int col = 0; col < fw; col++)
                        if ((g[ch * fh * bpr + row * bpr + col / 8] & (0x80 >> (col & 7))) != 0)
                            s.FillRect(c, x + (i * fw + col) * scale, y + row * scale, scale, scale);
            }
        }
    }
}
