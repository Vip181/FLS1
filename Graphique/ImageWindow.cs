using System;
using System.Drawing;
using Cosmos.System;
using FileSystemOS.Utils;

namespace FileSystemOS.Graphique
{
    /// <summary>Visionneuse d'images (.bmp) : affichage ajuste, definir comme fond d'ecran.</summary>
    public class ImageWindow : Window
    {
        private const int ToolH = 36, StatusH = 22;
        private readonly Button btnWall;
        private PixelBuffer image;
        private string path = "";
        private string status = "Ouvrez une image (.png, .jpg, .gif ou .bmp) depuis l'explorateur.";
        private GifLecteur gif;                 // GIF anime en cours de lecture
        private uint prochaineImage;

        public ImageWindow(int x, int y) : base("Images", x, y, 560, 440, Icons.ImageIcon)
        {
            btnWall = new Button(Glyphs.Check, "Fond d'ecran", 8, 5, 150, 26, Theme.Blue, Theme.BlueHover);
        }

        protected override Color ContentBg => Theme.ConsoleBg;

        /// <summary>Affiche une image deja en memoire (capture d'ecran sans disque).</summary>
        public void Afficher(PixelBuffer img, string titre)
        {
            if (image != null) { image.Release(); image = null; }
            gif = null;
            image = img;
            path = "";
            status = titre + "   " + img.W + " x " + img.H + " pixels";
            Title = "Images - " + titre;
            Services.Taskbar.Invalidate();
            Dirty = true;
        }

        public void Load(string p)
        {
            if (image != null) { image.Release(); image = null; }
            gif = null;
            path = p;
            try
            {
                if (p.ToLower().EndsWith(".gif"))
                {
                    gif = new GifLecteur(FsTools.ReadBytes(p));
                    image = new PixelBuffer(TextCodec.FileName(p), gif.W, gif.H);
                    image.Clear(Color.Black);
                    prochaineImage = FileSystemOS.Processus.Horloge.Millis + (uint)gif.Suivante(image);
                }
                else image = ImageLoader.Load(p);
                status = TextCodec.FileName(p) + "   " + image.W + " x " + image.H + " pixels" + (gif != null ? "   (GIF anime)" : "");
            }
            catch (Exception e) { status = "Erreur : " + e.Message; }
            Title = "Images - " + TextCodec.FileName(p);
            Services.Taskbar.Invalidate();
            Dirty = true;
        }

        public override void Tick()
        {
            if (gif == null || image == null || Hidden) return;
            uint t = FileSystemOS.Processus.Horloge.Millis;
            if (t < prochaineImage) return;
            try { prochaineImage = t + (uint)gif.Suivante(image); }
            catch (Exception e) { status = "Erreur dans l'animation : " + e.Message; gif = null; }
            Dirty = true;
        }

        protected override void DrawContent(PixelBuffer b, int cx, int cy, int cw, int ch)
        {
            b.FillRect(Theme.ToolbarBg, 0, cy, W, ToolH);
            btnWall.Draw(b, 0, cy);

            int top = cy + ToolH, h = H - top - StatusH;
            if (image != null) b.DrawFit(image, 8, top + 8, W - 16, h - 16);
            else b.DrawText("Aucune image", Theme.TextMuted, (W - 12 * 8) / 2, top + h / 2 - 8);

            b.FillRect(Theme.StatusBg, 0, H - StatusH, W, StatusH);
            string st = status.Length > (W - 20) / 8 ? status.Substring(0, (W - 20) / 8) : status;
            b.DrawText(st, Theme.TextMuted, 10, H - StatusH + 3);
        }

        public override void OnMouseMove(int mx, int my)
        {
            bool h = mx >= 0 && btnWall.Hit(X, Y + TitleH, mx, my);
            if (h != btnWall.Hover) { btnWall.Hover = h; Dirty = true; }
        }

        public override void OnMouseDown(int mx, int my) => btnWall.Pressed = btnWall.Hit(X, Y + TitleH, mx, my);

        public override void OnMouseUp(int mx, int my)
        {
            if (btnWall.Pressed && btnWall.Hit(X, Y + TitleH, mx, my))
            {
                if (image == null) status = "Aucune image a utiliser.";
                else if (path.Length == 0) status = "Enregistrez d'abord l'image sur un disque.";
                else status = "Fond d'ecran (prepare une fois en arriere-plan) : " + Desktop.SetWallpaper(path);
            }
            btnWall.Pressed = false;
        }
    }
}
