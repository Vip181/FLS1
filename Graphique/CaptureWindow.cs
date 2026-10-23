using System.Drawing;
using Cosmos.System;
using FileSystemOS.Processus;
using FileSystemOS.Utils;

namespace FileSystemOS.Graphique
{
    /// <summary>
    /// APPLICATION CAPTURE D'ECRAN.
    ///   - Capturer maintenant      : photo de l'ecran entier (BMP 24 bits)
    ///   - Capturer dans 3 s        : la fenetre se cache, puis la photo est prise sans elle
    ///   - Enregistrer une video    : GIF anime, 2 images par seconde ; le meme bouton l'arrete
    /// Les fichiers vont dans le dossier Captures de l'utilisateur. La touche Impr. ecran
    /// (ou Ctrl + Alt + P) prend aussi une photo, depuis n'importe ou.
    /// </summary>
    public class CaptureWindow : Window
    {
        private readonly Button maintenant, differee, video;
        private uint derniereSeconde;

        public CaptureWindow(int x, int y) : base("Capture d'ecran", x, y, 440, 300, Icons.CaptureIcon)
        {
            maintenant = new Button(Glyphs.Check, "Capturer maintenant", 20, 20, 190, 34, Theme.Blue, Theme.BlueHover);
            differee = new Button("Capturer dans 3 s", 230, 20, 190, 34, Theme.Blue, Theme.BlueHover);
            video = new Button("Enregistrer une video", 20, 70, 400, 38, Theme.Red, Theme.RedHover);
        }

        public override void Tick()
        {
            if (Hidden) return;
            uint s = Horloge.Millis / 1000;
            if (s == derniereSeconde) return;            // compteur de l'enregistrement, une fois par seconde
            derniereSeconde = s;
            Dirty = true;
        }

        protected override void DrawContent(PixelBuffer b, int cx, int cy, int cw, int ch)
        {
            video.Label = Enregistreur.Actif ? "Arreter l'enregistrement" : "Enregistrer une video";
            maintenant.Draw(b, 0, cy);
            differee.Draw(b, 0, cy);
            video.Draw(b, 0, cy);

            int y = cy + 126;
            if (Enregistreur.Actif)
            {
                b.FillRoundRect(Theme.Red, 20, y + 2, 12, 12, 6);
                b.DrawText("Enregistrement : " + Enregistreur.Secondes + " s, " + Enregistreur.Images +
                           " images, " + Fmt.Size(Enregistreur.Taille), Theme.Red, 40, y);
                y += 24;
            }
            Ligne(b, Enregistreur.Message, Theme.Text, ref y);
            if (Capture.Dernier.Length > 0) Ligne(b, "Dernier fichier : " + Capture.Dernier, Theme.TextMuted, ref y);
            string d = Capture.Dossier();
            Ligne(b, "Dossier : " + (d ?? "aucun disque accessible"), Theme.TextMuted, ref y);
            b.DrawText("Raccourci : Impr. ecran, ou Ctrl + Alt + P", Theme.TextMuted, 20, cy + ch - 26);
        }

        /// <summary>Texte coupe sur plusieurs lignes si besoin.</summary>
        private void Ligne(PixelBuffer b, string t, Color c, ref int y)
        {
            int max = (W - 40) / 8;
            int lignes = 0;
            while (t.Length > 0 && lignes < 3)
            {
                int n = t.Length < max ? t.Length : max;
                b.DrawText(t.Substring(0, n), c, 20, y);
                t = t.Substring(n);
                y += 18;
                lignes++;
            }
        }

        public override void OnMouseMove(int mx, int my)
        {
            bool a = maintenant.Hit(X, Y + TitleH, mx, my), d = differee.Hit(X, Y + TitleH, mx, my), v = video.Hit(X, Y + TitleH, mx, my);
            if (a != maintenant.Hover || d != differee.Hover || v != video.Hover)
            {
                maintenant.Hover = a; differee.Hover = d; video.Hover = v;
                Dirty = true;
            }
        }

        public override void OnMouseDown(int mx, int my)
        {
            maintenant.Pressed = maintenant.Hit(X, Y + TitleH, mx, my);
            differee.Pressed = differee.Hit(X, Y + TitleH, mx, my);
            video.Pressed = video.Hit(X, Y + TitleH, mx, my);
        }

        public override void OnMouseUp(int mx, int my)
        {
            if (maintenant.Pressed && maintenant.Hit(X, Y + TitleH, mx, my))
                Enregistreur.Message = Capture.Prendre(false);
            else if (differee.Pressed && differee.Hit(X, Y + TitleH, mx, my))
                Enregistreur.CaptureDifferee(this, 3000);
            else if (video.Pressed && video.Hit(X, Y + TitleH, mx, my))
            {
                if (Enregistreur.Actif) Enregistreur.Arreter();
                else Enregistreur.Demarrer();
            }
            maintenant.Pressed = false; differee.Pressed = false; video.Pressed = false;
            Dirty = true;
        }

        public override void OnKey(KeyEvent k)
        {
            if (k.Key == ConsoleKeyEx.Enter) { Enregistreur.Message = Capture.Prendre(false); Dirty = true; }
        }
    }
}
