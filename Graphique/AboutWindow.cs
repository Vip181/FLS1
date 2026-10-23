using System.Drawing;
using FileSystemOS.Demarrage;
using FileSystemOS.Systeme;

namespace FileSystemOS.Graphique
{
    /// <summary>Fenetre "A propos" : createur, editeur, avertissements, materiel detecte.</summary>
    public class AboutWindow : Window
    {
        private const int LogoSize = 90;
        private uint frame;

        public AboutWindow(int x, int y) : base("A propos", x, y, 500, 430, Icons.AboutIcon) { }

        protected override Color ContentBg => BootScreen.Bg;

        private int LogoCx => W / 2;
        private int LogoCy => TitleH + 70;

        protected override void DrawContent(PixelBuffer b, int cx, int cy, int cw, int ch)
        {
            VLogo.Draw(b, LogoCx, LogoCy, LogoSize, frame, BootScreen.Bg);

            Color light = Color.FromArgb(205, 208, 220), muted = Color.FromArgb(120, 124, 142);
            int y = cy + 140;
            Center(b, Services.OsName + "  version " + Services.Version, light, y);
            Center(b, Services.Company, muted, y + 22);

            Center(b, "Createur", muted, y + 54);
            Center(b, Services.Author, light, y + 72);

            b.FillRect(Color.FromArgb(40, 42, 58), 30, y + 102, W - 60, 1);
            Center(b, "Copie interdite de l'OS.", Theme.ConsoleError, y + 114);
            Center(b, "OS Open Source : veuillez mentionner la version", light, y + 136);
            Center(b, "et l'OS en cas de toute modification.", light, y + 154);
            b.FillRect(Color.FromArgb(40, 42, 58), 30, y + 182, W - 60, 1);

            Center(b, Services.ScreenW + "x" + Services.ScreenH + "  -  " + Materiel.RamMo + " Mo  -  " + Materiel.Video, muted, y + 194);
        }

        private void Center(PixelBuffer b, string t, Color c, int y)
        {
            if (t.Length > (W - 20) / 8) t = t.Substring(0, (W - 20) / 8);
            b.DrawText(t, c, (W - t.Length * 8) / 2, y);
        }

        /// <summary>Anime seulement la zone du logo (pas de rendu complet de la fenetre).</summary>
        public override void Tick()
        {
            frame++;
            if ((frame & 1) != 0 || Dirty || Hidden) return;
            VLogo.Draw(Buffer, LogoCx, LogoCy, LogoSize, frame, BootScreen.Bg);
            Services.ScreenDirty = true;
        }
    }
}
