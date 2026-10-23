using Cosmos.System;
using Cosmos.System.Graphics;

namespace FileSystemOS.Graphique
{
    /// <summary>
    /// Compositeur a deux niveaux pour rester fluide :
    ///  - SCENE  : fond + fenetres + barres. Recomposee seulement si une fenetre/barre a change.
    ///  - ECRAN  : scene + curseur. Si SEULE la souris bouge, on restaure juste la petite zone
    ///             sous l'ancien curseur et on dessine le nouveau (au lieu de tout recomposer).
    /// Fenetres a coins arrondis et ombre douce (assombrissement, sans transparence couteuse).
    /// </summary>
    public class Compositor
    {
        private const int Radius = 8;

        public readonly Bitmap ScreenBitmap;
        public readonly PixelBuffer Screen;
        public readonly PixelBuffer Scene;

        private int lastCx = -100, lastCy = -100;

        public Compositor()
        {
            ScreenBitmap = new Bitmap((uint)Services.ScreenW, (uint)Services.ScreenH, ColorDepth.ColorDepth32);
            Screen = new PixelBuffer("ecran", Services.ScreenW, Services.ScreenH, ScreenBitmap.rawData);
            if (ScreenBitmap.rawData.Length != Services.ScreenW * Services.ScreenH)
                FileSystemOS.Noyau.Diagnostic.Report("ecran", "image de Cosmos de " + ScreenBitmap.rawData.Length +
                    " pixels pour " + Services.ScreenW + "x" + Services.ScreenH);
            Scene = new PixelBuffer("scene", Services.ScreenW, Services.ScreenH);
        }

        private void ComposeScene()
        {
            Scene.Blit(Desktop.Background, 0, 0);
            Bureau.Dessiner(Scene);                 // icones du bureau, sous les fenetres

            var list = Services.Windows.Windows;
            for (int i = 0; i < list.Count; i++)
            {
                Window w = list[i];
                if (w.Hidden) continue;
                // Ombre douce : deux passes d'assombrissement decalees
                Scene.Darken(w.X + 2, w.Y + 6, w.W + 4, w.H + 2);
                Scene.Darken(w.X + 1, w.Y + 3, w.W + 2, w.H);
                Scene.BlitRounded(w.Buffer, w.X, w.Y, Radius);
            }

            Scene.Blit(Services.TopBar.Buffer, 0, 0);

            var bar = Services.Taskbar;
            Scene.Blit(bar.BarBuffer, 0, bar.Y0);
            bar.DessinerInfobulle(Scene);           // nom de l'icone survolee du dock
            if (bar.MenuOpen)
            {
                // Menu plein, pose sur la barre : une bande d'ombre a droite, puis une simple copie
                Scene.Darken(bar.MenuX + Taskbar.MenuW, bar.MenuY + 8, 4, bar.MenuH - 8);
                Scene.Blit(bar.MenuBuffer, bar.MenuX, bar.MenuY);
            }

            Bureau.DessinerMenu(Scene);             // menu du clic droit, au-dessus de tout
            PageConnexion.Dessiner(Scene);          // page de connexion : couvre tout l'ecran
            AssistantInstallation.Dessiner(Scene);  // assistant d'installation : avant la connexion

            if (FileSystemOS.Processus.Raccourcis.MessageVisible)
            {
                string m = FileSystemOS.Processus.Raccourcis.Message;
                int max = (Services.ScreenW - 40) / 8;
                if (m.Length > max) m = m.Substring(0, max);
                int w = m.Length * 8 + 24, x = (Services.ScreenW - w) / 2, y = Services.ScreenH - 90;
                Scene.FillRoundRect(Theme.Taskbar, x, y, w, 30, 8);
                Scene.DrawText(m, Theme.TitleText, x + 12, y + 7);
            }

            FileSystemOS.Noyau.Diagnostic.Draw(Scene);   // bandeau d'erreur / panneau F12
        }

        public void Frame()
        {
            Services.Windows.Sync();
            if (!Services.ScreenDirty && !Services.CursorDirty) return;

            int cx = (int)MouseManager.X, cy = (int)MouseManager.Y;

            if (Services.ScreenDirty)
            {
                ComposeScene();
                Screen.Blit(Scene, 0, 0);                   // toute la scene
            }
            else
            {
                // Seul le curseur a bouge : on efface l'ancien en restaurant la scene dessous
                Screen.CopyRegion(Scene, lastCx, lastCy, MouseCursor.W, MouseCursor.H);
            }

            MouseCursor.Draw(Screen, cx, cy);
            lastCx = cx;
            lastCy = cy;

            Services.ScreenDirty = false;
            Services.CursorDirty = false;

            Services.Canvas.DrawImage(ScreenBitmap, 0, 0);
            Services.Canvas.Display();
            Services.Frame++;
        }
    }
}
