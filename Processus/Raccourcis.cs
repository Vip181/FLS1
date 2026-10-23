using System;
using Cosmos.System;
using FileSystemOS.Graphique;
using FileSystemOS.Systeme;

namespace FileSystemOS.Processus
{
    /// <summary>
    /// RACCOURCIS CLAVIER DU SYSTEME, traites avant les fenetres :
    ///   Impr. ecran        capture d'ecran (BMP dans \FSOS\Utilisateurs\...\Captures)
    ///   Maj + Impr. ecran  capture puis ouverture dans l'application Images
    ///   touche Windows     ouvre / ferme le menu Demarrer
    ///   Ctrl + Alt + Suppr gestionnaire des taches
    ///   F12                panneau de diagnostic
    ///   Ctrl + Alt + L     verrouiller la session
    /// </summary>
    public static class Raccourcis
    {
        public static string Message = "";
        public static uint MessageMillis;

        private static bool A(KeyEvent k, ConsoleModifiers m) => (k.Modifiers & m) == m;

        /// <summary>Renvoie vrai si la touche a ete consommee par le systeme.</summary>
        public static bool Traiter(KeyEvent k)
        {
            bool ctrl = A(k, ConsoleModifiers.Control);
            bool alt = A(k, ConsoleModifiers.Alt);
            bool maj = A(k, ConsoleModifiers.Shift);

            // --- Impr. ecran : capture ---
            // Si ta version de Cosmos nomme cette touche autrement, remplace par ConsoleKeyEx.PrtScrn
            // Ctrl + Alt + P : meme chose, si le clavier n'envoie pas Impr. ecran
            if (k.Key == ConsoleKeyEx.PrintScreen || (ctrl && alt && k.Key == ConsoleKeyEx.P))
            {
                Afficher(Capture.Prendre(maj));
                return true;
            }

            // --- Touche Windows : menu Demarrer ---
            // Touche Windows : LWin / RWin (si absente de ta version, utilise Ctrl + Alt + M)
            if (k.Key == ConsoleKeyEx.LWin || k.Key == ConsoleKeyEx.RWin || (ctrl && alt && k.Key == ConsoleKeyEx.M))
            {
                if (!Services.LoggedIn) return true;
                Services.Taskbar.BasculerMenu();
                return true;
            }

            // --- Menu Demarrer ouvert : les touches servent a la recherche ---
            if (Services.Taskbar != null && Services.Taskbar.MenuOpen && !ctrl && !alt && Services.Taskbar.Touche(k)) return true;

            // --- Ctrl + Alt + Suppr : gestionnaire des taches ---
            if (ctrl && alt && k.Key == ConsoleKeyEx.Delete)
            {
                if (!Services.LoggedIn) return true;
                Services.Windows.Open(Services.TacheWin);
                return true;
            }

            // --- Ctrl + Alt + L : verrouiller ---
            if (ctrl && alt && k.Key == ConsoleKeyEx.L)
            {
                if (Services.LoggedIn) Session.Lock();
                return true;
            }

            if (k.Key == ConsoleKeyEx.F12) { FileSystemOS.Noyau.Diagnostic.Toggle(); return true; }
            return false;
        }

        private static void Afficher(string texte)
        {
            Message = texte;
            MessageMillis = Horloge.Millis;
            Services.ScreenDirty = true;
            try { Services.ConsoleWin.Print(texte); } catch (Exception) { }
        }

        /// <summary>Le message de capture reste visible 3 secondes.</summary>
        public static bool MessageVisible => Message.Length > 0 && Horloge.Millis - MessageMillis < 3000;
    }
}
