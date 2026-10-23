using System.Collections.Generic;
using Cosmos.System;
using FileSystemOS.Processus;

namespace FileSystemOS.Graphique
{
    /// <summary>
    /// Gestionnaire de fenetres.
    ///  - Windows : ordre de profondeur (dernier = premier plan).
    ///  - All     : ordre d'ouverture -> ordre des onglets.
    /// Ouvrir une fenetre lui cree un processus + un thread de rendu ;
    /// la fermer (ou tuer son processus avec 'kill') la retire.
    /// </summary>
    public class WindowManager
    {
        public readonly List<Window> Windows = new List<Window>();
        public readonly List<Window> All = new List<Window>();

        private Window dragging;
        private Window pressed;
        private int dragDx, dragDy;
        private bool wasDown;

        public Window Focused
        {
            get
            {
                for (int i = Windows.Count - 1; i >= 0; i--)
                    if (!Windows[i].Hidden) return Windows[i];
                return null;
            }
        }

        private void Changed()
        {
            Services.ScreenDirty = true;
            Services.Taskbar.Invalidate();
        }

        // ---- Cycle de vie ----
        public void Open(Window w)
        {
            if (w.Proc == null || !w.Proc.Alive)
            {
                w.Proc = Services.Scheduler.Spawn("fen-" + w.Title.ToLower(), false);
                w.Proc.Start(new WindowThread(w));
            }
            if (!All.Contains(w))
            {
                All.Add(w);
                Windows.Add(w);
            }
            if (w.Suspended) Services.Wait.Resume(w);
            w.Minimized = false;
            ClampToScreen(w);
            w.OnOpen();
            w.Dirty = true;
            BringToFront(w);
        }

        public void Close(Window w)
        {
            Services.Wait.Forget(w);
            FileSystemOS.Noyau.Cogestion.Forget(w);
            Windows.Remove(w);
            All.Remove(w);
            if (w.Proc != null && w.Proc.Alive) w.Proc.Kill();
            if (pressed == w) pressed = null;
            if (dragging == w) dragging = null;
            Changed();
        }

        public void Minimize(Window w)
        {
            w.Minimized = true;
            if (pressed == w) pressed = null;
            if (dragging == w) dragging = null;
            Changed();
        }

        public void BringToFront(Window w)
        {
            Windows.Remove(w);
            Windows.Add(w);
            Changed();
        }

        public void ToggleFromTaskbar(Window w)
        {
            if (w.Suspended) Services.Wait.Resume(w);
            else if (w.Minimized) { w.Minimized = false; w.Dirty = true; BringToFront(w); }
            else if (w == Focused) Minimize(w);
            else BringToFront(w);
        }

        /// <summary>Ferme les fenetres dont le processus a ete tue (commande 'kill').</summary>
        public void Sync()
        {
            for (int i = All.Count - 1; i >= 0; i--)
            {
                Window w = All[i];
                if (w.Proc == null || !w.Proc.Alive) Close(w);
            }
        }

        private Window TopAt(int mx, int my)
        {
            for (int i = Windows.Count - 1; i >= 0; i--)
            {
                Window w = Windows[i];
                if (!w.Hidden && w.Contains(mx, my)) return w;
            }
            return null;
        }

        // ---- Souris ----
        private bool wasRight, bureauAppui;

        public void UpdateMouse(int mx, int my, bool down, bool droit = false)
        {
            var bar = Services.Taskbar;
            var top = Services.TopBar;
            bar.UpdateHover(mx, my);
            top.UpdateHover(mx, my);

            bool click = down && !wasDown;
            bool clicDroit = droit && !wasRight;

            // Menu du clic droit du bureau : il passe avant tout le reste
            if (Bureau.MenuOuvert)
            {
                Bureau.SurvolMenu(mx, my);
                if (click && Bureau.ClicMenu(mx, my)) { wasDown = down; wasRight = droit; return; }
                if (clicDroit) Bureau.FermerMenu();
            }

            if (click && (bar.HandleClick(mx, my) || top.HandleClick(mx, my)))
            {
                wasDown = down;
                wasRight = droit;
                return;
            }

            if (click)
            {
                Window w = TopAt(mx, my);
                if (w != null)
                {
                    int k = w.TitleButtonAt(mx, my);
                    if (w.Fixed && k >= 0) { }   // connexion : boutons de titre inactifs
                    else if (k == Window.BtnClose) Close(w);
                    else if (k == Window.BtnMinimize) Minimize(w);
                    else if (k == Window.BtnWait) Services.Wait.Suspend(w);
                    else
                    {
                        BringToFront(w);
                        if (w.HitTitle(mx, my))
                        {
                            dragging = w;
                            dragDx = mx - w.X;
                            dragDy = my - w.Y;
                        }
                        else
                        {
                            pressed = w;
                            w.OnMouseDown(mx, my);
                            w.Dirty = true;
                        }
                    }
                }
                else if (!bar.IsOver(mx, my) && !top.IsOver(mx, my))
                {
                    // Clic sur le fond : icones du bureau
                    Bureau.Appui(mx, my);
                    bureauAppui = true;
                }
            }
            else if (down)
            {
                if (dragging != null) MoveTo(dragging, mx - dragDx, my - dragDy);
                else if (pressed != null) { pressed.OnMouseDrag(mx, my); pressed.Dirty = true; }
                else if (bureauAppui) Bureau.Glisser(mx, my);
            }
            else if (wasDown)
            {
                if (pressed != null) { pressed.OnMouseUp(mx, my); pressed.Dirty = true; }
                if (bureauAppui) Bureau.Relache(mx, my);
                pressed = null;
                dragging = null;
                bureauAppui = false;
            }

            // Clic droit sur le fond (hors fenetres et barres) : menu du bureau
            if (clicDroit && !Bureau.MenuOuvert && TopAt(mx, my) == null && !bar.IsOver(mx, my) && !top.IsOver(mx, my))
                Bureau.OuvrirMenu(mx, my);
            wasRight = droit;

            Window over = (bar.IsOver(mx, my) || top.IsOver(mx, my)) ? null : TopAt(mx, my);
            for (int i = 0; i < Windows.Count; i++)
            {
                Window w = Windows[i];
                if (w.Hidden) continue;
                int hx = w == over ? mx : -1;
                w.UpdateTitleHover(hx, my);
                w.OnMouseMove(hx, my);
            }

            wasDown = down;
        }

        /// <summary>Ramene la fenetre dans l'ecran (utile en 800x600).</summary>
        private void ClampToScreen(Window w)
        {
            int maxX = Services.ScreenW - w.W - 6;
            int maxY = Services.ScreenH - Desktop.TaskbarH - w.H - 6;
            if (w.X > maxX) w.X = maxX < 0 ? 0 : maxX;
            if (w.Y > maxY) w.Y = maxY < TopBar.H ? TopBar.H : maxY;
            if (w.Y < TopBar.H) w.Y = TopBar.H;
        }

        private void MoveTo(Window w, int x, int y)
        {
            int maxX = Services.ScreenW - w.W - 6;
            int maxY = Services.ScreenH - Desktop.TaskbarH - w.H - 6;
            w.X = x < 0 ? 0 : (x > maxX ? maxX : x);
            w.Y = y < TopBar.H ? TopBar.H : (y > maxY ? maxY : y);
            Services.ScreenDirty = true; // deplacer ne redessine pas la fenetre, juste l'ecran
        }

        public void DispatchKey(KeyEvent key)
        {
            Window f = Focused;
            if (f == null) return;
            f.OnKey(key);
            f.Dirty = true;
        }
    }
}
