using System;
using System.Collections.Generic;
using System.Drawing;
using FileSystemOS.Graphique;
using FileSystemOS.Systeme;

namespace FileSystemOS.Noyau
{
    /// <summary>
    /// Diagnostic : les erreurs des threads sont rattrapees (l'OS ne plante plus)
    /// et affichees. Touche F12 = panneau de diagnostic complet par-dessus le bureau.
    /// </summary>
    public static class Diagnostic
    {
        public static bool Overlay;
        public static string LastError;
        public static uint ErrorCount;
        public static readonly List<string> Errors = new List<string>();

        public static void Report(string source, string message)
        {
            ErrorCount++;
            LastError = source + " : " + message;
            if (Errors.Count == 0 || Errors[Errors.Count - 1] != LastError)
            {
                Errors.Add(LastError);
                while (Errors.Count > 8) Errors.RemoveAt(0);
                if (Services.Memory != null) Services.Memory.Write("ERREUR " + LastError);
            }
            Services.ScreenDirty = true;
        }

        public static void Toggle()
        {
            Overlay = !Overlay;
            Services.ScreenDirty = true;
        }

        private static readonly Color Bg = Color.FromArgb(8, 9, 14);
        private static readonly Color Txt = Color.FromArgb(220, 224, 236);
        private static readonly Color Dim = Color.FromArgb(130, 136, 156);
        private static readonly Color Err = Color.FromArgb(255, 110, 110);
        private static readonly Color Ok = Color.FromArgb(110, 230, 150);

        private static void A(List<string> l, List<Color> c, string t, Color col) { l.Add(t); c.Add(col); }

        /// <summary>Dessine le bandeau d'erreur ou le panneau complet dans la scene.</summary>
        public static void Draw(PixelBuffer s)
        {
            int x = 12, y = TopBar.H + 8;
            if (!Overlay)
            {
                if (LastError == null) return;
                string l = "Erreur : " + LastError + "   (F12 = diagnostic)";
                int max = (s.W - 40) / 8;
                if (l.Length > max) l = l.Substring(0, max);
                s.FillRect(Color.FromArgb(120, 20, 24), 0, y - 6, s.W, 26);
                s.DrawText(l, Txt, x, y - 1);
                return;
            }

            var lines = new List<string>();
            var cols = new List<Color>();

            A(lines, cols, "DIAGNOSTIC  -  " + Services.OsName + " " + Services.Version + "   (F12 pour fermer)", Ok);
            A(lines, cols, "Ecran : " + Services.ScreenW + "x" + Services.ScreenH + " (" + Materiel.Choix + ")  Machine : " +
                (Materiel.Virtuel ? "virtuelle" : "physique") + "  Video : " + Materiel.Video, Txt);
            A(lines, cols, "Memoire : " + Materiel.RamMo + " Mo   Live : " + Live.Status, Txt);
            A(lines, cols, "Session : " + (Services.LoggedIn ? "connecte (" + (Comptes.Actuel != null ? Comptes.Actuel.Nom : "?") + ")" : "ecran de connexion") +
                "   Comptes : " + Comptes.Liste.Count, Txt);
            A(lines, cols, "Ordonnanceur : " + (Services.Scheduler == null ? 0 : Services.Scheduler.Commutations) +
                " commutations, tranche " + FileSystemOS.Processus.Scheduler.Quantum + " ms, horloge " +
                (FileSystemOS.Processus.Horloge.Active ? FileSystemOS.Processus.Horloge.Millis + " ms" : "inactive") +
                ", interblocages evites " + FileSystemOS.Processus.Verrou.Interblocages, Txt);
            A(lines, cols, "Tours/s : " + Services.Tps + "   Images/s : " + Services.Fps + "   Cogestion : " + Cogestion.Renders +
                " rendus, " + Cogestion.Pending + " en attente   Erreurs : " + ErrorCount, Txt);

            var wins = Services.Windows == null ? null : Services.Windows.All;
            A(lines, cols, "Fenetres : " + (wins == null ? 0 : wins.Count), Ok);
            if (wins != null)
                for (int i = 0; i < wins.Count && i < 10; i++)
                {
                    Window w = wins[i];
                    string st = w.Suspended ? "attente" : (w.Minimized ? "reduite" : "visible");
                    string pr = w.Proc == null ? "pas de processus" :
                                "PID " + w.Proc.Pid + (w.Proc.Alive ? (w.Proc.Suspended ? " gele" : " actif") : " MORT");
                    A(lines, cols, "  " + w.Title + "  [" + st + "]  " + w.X + "," + w.Y + " " + w.W + "x" + w.H + "  " + pr +
                        (w.Dirty ? "  a dessiner" : ""), Dim);
                }

            try
            {
                var pg = FileSystemOS.Memoire.Pages.Etat();
                for (int i = 0; i < pg.Count && i < 8; i++) A(lines, cols, (i == 0 ? "" : "  ") + pg[i], i == 0 ? Ok : Dim);
            }
            catch (Exception) { }

            A(lines, cols, "Erreurs recentes :", Ok);
            if (Errors.Count == 0) A(lines, cols, "  aucune", Dim);
            for (int i = 0; i < Errors.Count; i++) A(lines, cols, "  " + Errors[i], Err);

            A(lines, cols, "Fin du demarrage :", Ok);
            int first = Services.BootLog.Count > 6 ? Services.BootLog.Count - 6 : 0;
            for (int i = first; i < Services.BootLog.Count; i++) A(lines, cols, "  " + Services.BootLog[i], Dim);

            int w2 = s.W - 2 * x;
            int maxc = (w2 - 20) / 8;
            s.FillRect(Bg, x, y, w2, lines.Count * 18 + 16);
            s.DrawRect(Ok, x, y, w2, lines.Count * 18 + 16);
            for (int i = 0; i < lines.Count; i++)
            {
                string l = lines[i].Length > maxc ? lines[i].Substring(0, maxc) : lines[i];
                s.DrawText(l, cols[i], x + 10, y + 8 + i * 18);
            }
        }
    }
}
