using System;
using System.Collections.Generic;
using System.Drawing;
using Cosmos.System;
using FileSystemOS.Memoire;
using FileSystemOS.Processus;
using FileSystemOS.Systeme;
using FileSystemOS.Utils;

namespace FileSystemOS.Graphique
{
    /// <summary>
    /// GESTIONNAIRE DES TACHES (Ctrl + Alt + Suppr).
    /// Liste les processus avec leur utilisateur, leur etat, leur temps processeur, leurs pages
    /// et leurs threads. Permet d'arreter un processus, de le mettre en attente, de le reprendre,
    /// et donne acces au verrouillage, au redemarrage et a l'arret.
    /// </summary>
    public class TacheWindow : Window
    {
        private const int ToolH = 36, HeadH = 22, RowH = 20, StatusH = 22;

        private readonly Button btnFin, btnAttente, btnRafraichir, btnVerrouiller, btnRedemarrer;
        private readonly ScrollBar sb = new ScrollBar();
        private readonly List<Process> lignes = new List<Process>();
        private int selection = -1, survol = -1;
        private string status = "";
        private uint dernier;

        public TacheWindow(int x, int y) : base("Gestionnaire des taches", x, y, 700, 480, Icons.TaskIcon)
        {
            btnFin = new Button(Glyphs.Trash, "Arreter", 8, 5, 118, 26, Theme.Red, Theme.RedHover);
            btnAttente = new Button(Glyphs.Wait, "En attente", 132, 5, 140, 26, Theme.Blue, Theme.BlueHover);
            btnRafraichir = new Button(Glyphs.Refresh, "", 278, 5, 32, 26, Theme.Blue, Theme.BlueHover);
            btnVerrouiller = new Button(Glyphs.Check, "Verrouiller", 316, 5, 140, 26, Theme.Blue, Theme.BlueHover);
            btnRedemarrer = new Button(Glyphs.Power, "Redemarrer", 462, 5, 150, 26, Theme.Red, Theme.RedHover);
        }

        private int ListY => TitleH + ToolH + HeadH;
        private int ListH => H - ListY - StatusH;
        private int SbX => W - ScrollBar.Width - 4;

        public override void OnOpen()
        {
            Recharger();
            status = "Selectionnez un processus, puis Arreter ou En attente.";
        }

        private void Recharger()
        {
            lignes.Clear();
            var p = Services.Scheduler.Processes;
            for (int i = 0; i < p.Count; i++) lignes.Add(p[i]);
            sb.Total = lignes.Count;
            sb.Visible = ListH / RowH;
            sb.Clamp();
            Dirty = true;
        }

        /// <summary>Mise a jour du temps processeur une fois par seconde.</summary>
        public override void Tick()
        {
            if (Hidden) return;
            uint sec = Horloge.Millis / 1000;
            if (sec == dernier) return;
            dernier = sec;
            Recharger();
        }

        protected override void DrawContent(PixelBuffer b, int cx, int cy, int cw, int ch)
        {
            b.FillRect(Theme.ToolbarBg, 0, cy, W, ToolH);
            btnFin.Draw(b, 0, cy);
            btnAttente.Draw(b, 0, cy);
            btnRafraichir.Draw(b, 0, cy);
            btnVerrouiller.Draw(b, 0, cy);
            btnRedemarrer.Draw(b, 0, cy);

            // Resume du systeme
            var s = Services.Scheduler;
            string resume = s.Processes.Count + " processus   " + Services.Fps + " images/s   " +
                            s.Commutations + " commutations   pages " + Pages.Utilisees + "/" + Pages.Total;
            b.FillRect(Theme.HeaderBg, 0, cy + ToolH, W, HeadH);
            b.DrawText(resume, Theme.TextMuted, 10, cy + ToolH + 3);

            // En-tetes
            int y0 = ListY - TitleH + cy;
            b.FillRect(Theme.Separator, 0, y0 - 1, W, 1);
            b.DrawText("PID", Theme.TextMuted, 10, y0 + 2);
            b.DrawText("processus", Theme.TextMuted, 60, y0 + 2);
            b.DrawText("utilisateur", Theme.TextMuted, 230, y0 + 2);
            b.DrawText("etat", Theme.TextMuted, 350, y0 + 2);
            b.DrawText("cpu", Theme.TextMuted, 440, y0 + 2);
            b.DrawText("pages", Theme.TextMuted, 520, y0 + 2);
            b.DrawText("fils", Theme.TextMuted, 590, y0 + 2);

            sb.Visible = (ListH - RowH) / RowH;
            sb.Total = lignes.Count;
            sb.Clamp();
            for (int i = 0; i < sb.Visible; i++)
            {
                int idx = sb.Value + i;
                if (idx >= lignes.Count) break;
                Process p = lignes[idx];
                int y = y0 + RowH + i * RowH;

                if (idx == selection) b.FillRect(Theme.RowSelected, 4, y, SbX - 8, RowH - 2);
                else if (idx == survol) b.FillRect(Theme.RowHover, 4, y, SbX - 8, RowH - 2);

                Color col = p.Critical ? Theme.TextMuted : Theme.Text;
                bool erreur = ThreadEnAttente(p);
                if (p.EtatCourant == Etat.Bloque || erreur) col = Theme.Red;
                else if (p.Suspended) col = Theme.Blue;

                b.DrawText(p.Pid.ToString(), col, 10, y + 2);
                b.DrawText(Court(p.Name, 20), col, 60, y + 2);
                b.DrawText(Court(p.Utilisateur, 13), col, 230, y + 2);
                b.DrawText(p.Suspended ? "en attente" : (erreur ? "attente (erreur)" : Etat.Nom(p.EtatCourant)), col, 350, y + 2);
                b.DrawText(p.TempsCpu + " ms", col, 440, y + 2);
                b.DrawText(p.PagesUtilisees.ToString(), col, 520, y + 2);
                b.DrawText(p.Threads.Count + (p.Critical ? "  systeme" : ""), col, 590, y + 2);
            }
            sb.Draw(b, SbX, y0 + RowH, ListH - RowH);

            b.FillRect(Theme.StatusBg, 0, H - StatusH, W, StatusH);
            b.DrawText(Court(status, (W - 20) / 8), status.StartsWith("Erreur") ? Theme.Red : Theme.TextMuted, 10, H - StatusH + 3);
        }

        private static string Court(string s, int n) => s.Length > n ? s.Substring(0, n - 1) + "." : s;

        private Process Choisi => selection >= 0 && selection < lignes.Count ? lignes[selection] : null;

        private void Arreter()
        {
            Process p = Choisi;
            if (p == null) { status = "Selectionnez d'abord un processus."; return; }
            Services.Scheduler.Kill(p.Pid, out string m);
            status = m;
            selection = -1;
            Recharger();
        }

        private static bool ThreadEnAttente(Process p)
        {
            for (int i = 0; i < p.Threads.Count; i++) if (p.Threads[i].Alive && p.Threads[i].EnAttente) return true;
            return false;
        }

        private void Attente()
        {
            Process p = Choisi;
            if (p == null) { status = "Selectionnez d'abord un processus."; return; }
            if (p.Critical) { status = "Erreur : '" + p.Name + "' est un processus systeme."; return; }
            // Un thread mis de cote apres une erreur : ce bouton le reprend
            if (ThreadEnAttente(p))
            {
                for (int i = 0; i < p.Threads.Count; i++) { p.Threads[i].EnAttente = false; p.Threads[i].Errors = 0; }
                p.Suspended = false;
                status = "'" + p.Name + "' repris apres son erreur";
                Recharger();
                return;
            }
            p.Suspended = !p.Suspended;
            status = "'" + p.Name + "' " + (p.Suspended ? "mis en attente (gele)" : "repris");
            Recharger();
        }

        // ---- Souris ----
        private bool Hover(Button bt, int lx, int ly)
        {
            bool h = lx >= 0 && bt.Hit(0, 0, lx, ly);
            if (h == bt.Hover) return false;
            bt.Hover = h;
            return true;
        }

        public override void OnMouseMove(int mx, int my)
        {
            int lx = mx < 0 ? -1 : mx - X, ly = my - Y - TitleH;
            bool ch = Hover(btnFin, lx, ly) | Hover(btnAttente, lx, ly) | Hover(btnRafraichir, lx, ly) |
                      Hover(btnVerrouiller, lx, ly) | Hover(btnRedemarrer, lx, ly);
            int s = -1;
            if (mx >= 0 && my - Y >= ListY + RowH && my - Y < ListY + ListH)
                s = sb.Value + (my - Y - ListY - RowH) / RowH;
            if (s >= lignes.Count) s = -1;
            if (s != survol || ch) { survol = s; Dirty = true; }
        }

        public override void OnMouseDown(int mx, int my)
        {
            int lx = mx - X, ly = my - Y - TitleH;
            if (sb.MouseDown(lx, my - Y, SbX, ListY + RowH, ListH - RowH)) return;
            btnFin.Pressed = btnFin.Hit(0, 0, lx, ly);
            btnAttente.Pressed = btnAttente.Hit(0, 0, lx, ly);
            btnRafraichir.Pressed = btnRafraichir.Hit(0, 0, lx, ly);
            btnVerrouiller.Pressed = btnVerrouiller.Hit(0, 0, lx, ly);
            btnRedemarrer.Pressed = btnRedemarrer.Hit(0, 0, lx, ly);

            if (my - Y >= ListY + RowH && my - Y < ListY + ListH && lx < SbX)
            {
                int s = sb.Value + (my - Y - ListY - RowH) / RowH;
                selection = s < lignes.Count ? s : -1;
                if (selection >= 0) status = "'" + lignes[selection].Name + "' selectionne.";
            }
        }

        public override void OnMouseDrag(int mx, int my) => sb.MouseDrag(my - Y, ListY + RowH, ListH - RowH);

        public override void OnMouseUp(int mx, int my)
        {
            int lx = mx - X, ly = my - Y - TitleH;
            if (btnFin.Pressed && btnFin.Hit(0, 0, lx, ly)) Arreter();
            if (btnAttente.Pressed && btnAttente.Hit(0, 0, lx, ly)) Attente();
            if (btnRafraichir.Pressed && btnRafraichir.Hit(0, 0, lx, ly)) { Recharger(); status = "Liste mise a jour."; }
            if (btnVerrouiller.Pressed && btnVerrouiller.Hit(0, 0, lx, ly)) Session.Lock();
            if (btnRedemarrer.Pressed && btnRedemarrer.Hit(0, 0, lx, ly)) Alimentation.Redemarrer();
            btnFin.Pressed = btnAttente.Pressed = btnRafraichir.Pressed = false;
            btnVerrouiller.Pressed = btnRedemarrer.Pressed = false;
            sb.MouseUp();
        }

        public override void OnKey(KeyEvent k)
        {
            switch (k.Key)
            {
                case ConsoleKeyEx.UpArrow: if (selection > 0) selection--; break;
                case ConsoleKeyEx.DownArrow: if (selection < lignes.Count - 1) selection++; break;
                case ConsoleKeyEx.Delete: Arreter(); break;
                case ConsoleKeyEx.F5: Recharger(); break;
            }
        }
    }
}
