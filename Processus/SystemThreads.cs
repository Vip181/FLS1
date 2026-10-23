using Cosmos.Core.Memory;
using Cosmos.System;
using FileSystemOS.Graphique;
using FileSystemOS.Utils;

namespace FileSystemOS.Processus
{
    // =========== Processus "systeme" ===========

    /// <summary>Lit la souris et le clavier, les envoie au gestionnaire de fenetres.</summary>
    public class InputThread : KThread
    {
        private int lastX = -1, lastY = -1;
        public InputThread() : base("entree") { }

        public override void Step()
        {
            int mx = (int)MouseManager.X;
            int my = (int)MouseManager.Y;
            if (mx != lastX || my != lastY)
            {
                lastX = mx; lastY = my;
                Services.CursorDirty = true; // seul le curseur a bouge : mise a jour legere
            }

            bool down = (MouseManager.MouseState & MouseState.Left) == MouseState.Left;
            bool droit = (MouseManager.MouseState & MouseState.Right) == MouseState.Right;

            // Assistant d'installation ouvert (avant la connexion) : il recoit tout
            if (AssistantInstallation.Active)
            {
                AssistantInstallation.Souris(mx, my, down);
                AssistantInstallation.Tick();
                while (KeyboardManager.TryReadKey(out KeyEvent k))
                {
                    if (k.Key == ConsoleKeyEx.PrintScreen) { Raccourcis.Traiter(k); continue; }
                    AssistantInstallation.Touche(k);
                }
                return;
            }

            // Page de connexion ouverte : elle recoit tout, rien d'autre n'est accessible
            if (PageConnexion.Active)
            {
                PageConnexion.Souris(mx, my, down);
                PageConnexion.Tick();
                while (KeyboardManager.TryReadKey(out KeyEvent k))
                {
                    if (k.Key == ConsoleKeyEx.PrintScreen) { Raccourcis.Traiter(k); continue; }
                    PageConnexion.Touche(k);
                }
                return;
            }

            Services.Windows.UpdateMouse(mx, my, down, droit);

            while (KeyboardManager.TryReadKey(out KeyEvent key))
            {
                if (Raccourcis.Traiter(key)) continue;      // Impr.ecran, Windows, Ctrl+Alt+Suppr, F12
                Services.Windows.DispatchKey(key);
            }
        }
    }

    /// <summary>Enregistre sur le SSD, un fichier a la fois, ce qui a change dans la session.</summary>
    public class SynchroThread : KThread
    {
        public SynchroThread() : base("synchro") { }
        public override void Step() => FileSystemOS.Systeme.SessionSsd.Pas();
    }

    public class GcThread : KThread
    {
        private int counter;
        private uint dernier;
        public GcThread() : base("gc") { }

        public override void Step()
        {
            // Le ramasse-miettes ne tourne PLUS ici (en plein milieu de l'ordonnanceur, des objets
            // encore utilises par les fonctions en cours pouvaient etre liberes). On le demande
            // seulement : le noyau l'execute a la fin de son tour, quand rien n'est en cours.
            // Toutes les 3 secondes (avant : tous les 100 tours, soit plusieurs fois par seconde)
            if (Horloge.Active)
            {
                if (Horloge.Millis - dernier >= 3000) { dernier = Horloge.Millis; Services.GcRequest = true; }
            }
            else if (++counter >= 2000) { counter = 0; Services.GcRequest = true; }
        }
    }

    // =========== Processus "affichage" ===========

    public class CompositorThread : KThread
    {
        public CompositorThread() : base("compositeur") { }
        public override void Step()
        {
            if (Services.Booting || Services.ShuttingDown) return;   // ecran de chargement / d'arret
            Services.Compositor.Frame();
        }
    }

    // =========== Processus "interface" ===========

    public class TaskbarThread : KThread
    {
        public TaskbarThread() : base("barre") { }

        public override void Step()
        {
            var t = Services.Taskbar;
            if (!t.BarDirty) return;
            t.BarDirty = false;
            t.RenderBar();
            Services.ScreenDirty = true;
        }
    }

    /// <summary>
    /// Prepare le fond d'ecran en arriere-plan, par tranches de 15 ms : les vagues, ou une image
    /// (decodage, mise a la taille de l'ecran, copie toute prete). Ne fait rien le reste du temps.
    /// </summary>
    public class FondThread : KThread
    {
        public FondThread() : base("fond") { }

        public override void Step() => FileSystemOS.Graphique.Desktop.AvancerFond();
    }

    public class MenuThread : KThread
    {
        public MenuThread() : base("menu") { }

        public override void Step()
        {
            var t = Services.Taskbar;
            if (!t.MenuOpen || !t.MenuDirty) return;
            t.MenuDirty = false;
            t.RenderMenu();
            Services.ScreenDirty = true;
        }
    }

    // =========== Processus "attente" ===========

    /// <summary>Redessine la barre du haut quand elle a change.</summary>
    public class TopBarThread : KThread
    {
        public TopBarThread() : base("barre-haut") { }

        public override void Step()
        {
            var t = Services.TopBar;
            if (!t.Dirty) return;
            t.Dirty = false;
            t.Render();
            Services.ScreenDirty = true;
        }
    }

    /// <summary>Thread du buffer d'attente : calcule les miniatures petit a petit.</summary>
    public class WaitThread : KThread
    {
        private int counter;
        public WaitThread() : base("tampon") { }

        public override void Step()
        {
            if (Services.Wait.Work()) Services.TopBar.Dirty = true;
            if (++counter >= 120)
            {
                counter = 0;
                Services.Wait.Balance();            // evite de saturer le noyau
                Services.TopBar.Dirty = true;       // compteurs a jour
            }
        }
    }

    // =========== Un processus par fenetre ===========

    /// <summary>Redessine le buffer de SA fenetre quand elle a change.</summary>
    public class WindowThread : KThread
    {
        private readonly Window win;
        private bool lastFocus;

        public WindowThread(Window w) : base("rendu") { win = w; }

        public override void Step()
        {
            bool focus = Services.Windows.Focused == win;
            if (focus != lastFocus) { lastFocus = focus; win.Dirty = true; }

            win.Tick();

            // Cogestion : on ne dessine pas ici, on depose une demande de rendu
            if (win.Dirty && !win.Hidden) FileSystemOS.Noyau.Cogestion.Request(win);
        }
    }

    // =========== Processus "horloge" ===========

    public class ClockThread : KThread
    {
        private int counter;
        public ClockThread() : base("rtc") { }

        public override void Step()
        {
            if (++counter < 30) return;
            counter = 0;
            int h = ((Cosmos.HAL.RTC.Hour + Services.ClockOffset) % 24 + 24) % 24;
            string t = Fmt.Two(h) + ":" +
                       Fmt.Two(Cosmos.HAL.RTC.Minute) + ":" +
                       Fmt.Two(Cosmos.HAL.RTC.Second);
            if (t != Services.ClockText)
            {
                Services.ClockText = t;
                Services.Taskbar.Invalidate();
            }
        }
    }

    /// <summary>Thread de demonstration (commande 'demo').</summary>
    public class CounterThread : KThread
    {
        public uint Value;
        public CounterThread() : base("compteur") { }
        public override void Step() { Value++; }
    }
}
