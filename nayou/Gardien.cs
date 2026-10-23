using System;
using System.Drawing;
using Cosmos.System.Graphics;   // Pen, Canvas
using FileSystemOS.Processus;

namespace FileSystemOS.Noyau
{
    /// <summary>
    /// Thread "gardien" du processus "noyau" : mesure chaque seconde la vitesse du systeme
    /// (tours d'ordonnanceur et images par seconde) et adapte le ramasse-miettes.
    /// </summary>
    public class GardienThread : KThread
    {
        private byte lastSec = 255;
        private uint lastTicks, lastFrames;

        public GardienThread() : base("gardien") { }

        private uint dernier;

        public override void Step()
        {
            // L'horloge du PIT (en memoire) plutot que la puce RTC : lire la RTC a chaque tour de
            // l'ordonnanceur martelait ses ports d'entree-sortie des milliers de fois par seconde.
            if (Horloge.Active)
            {
                uint maintenant = Horloge.Millis;
                if (maintenant - dernier < 1000) return;
                if (dernier != 0)
                {
                    Services.Tps = Services.Scheduler.TotalTicks - lastTicks;
                    Services.Fps = Services.Frame - lastFrames;
                }
                dernier = maintenant;
                lastTicks = Services.Scheduler.TotalTicks;
                lastFrames = Services.Frame;
                if (Diagnostic.Overlay) Services.ScreenDirty = true;
                SurveillerGels(maintenant);
                return;
            }

            Watchdog.Etape = "lecture de l'horloge (RTC)";
            byte sec = Cosmos.HAL.RTC.Second;
            Watchdog.Etape = "programme en cours";
            if (sec == lastSec) return;

            if (lastSec != 255)
            {
                Services.Tps = Services.Scheduler.TotalTicks - lastTicks;
                Services.Fps = Services.Frame - lastFrames;
            }
            lastSec = sec;
            lastTicks = Services.Scheduler.TotalTicks;
            lastFrames = Services.Frame;
            if (Diagnostic.Overlay) Services.ScreenDirty = true;   // panneau F12 mis a jour chaque seconde
        }

        // ---- Gels silencieux ----
        // Le gardien d'urgence (minuteur PIT) ne voit un blocage que si l'ordonnanceur s'arrete.
        // Ici, l'ordonnanceur tourne encore, mais le chargement ou l'affichage n'avancent plus
        // (par exemple un thread qui echoue a chaque tour) : on l'ecrit a l'ecran, avec la derniere
        // erreur, au lieu de laisser une image figee sans explication.
        private uint toursVus, toursDepuis, imagesVues, imagesDepuis;
        private bool alerte;

        private void SurveillerGels(uint maintenant)
        {
            if (Services.Booting)
            {
                if (Services.ToursDemarrage != toursVus || toursDepuis == 0) { toursVus = Services.ToursDemarrage; toursDepuis = maintenant; return; }
                if (maintenant - toursDepuis > 15000)
                    Alerte("DEMARRAGE ARRETE", "le chargeur n'avance plus (etape : " + Services.EtapeDemarrage + ")");
                return;
            }
            if (Services.Frame != imagesVues || !Services.ScreenDirty || imagesDepuis == 0)
            {
                imagesVues = Services.Frame;
                imagesDepuis = maintenant;
                alerte = false;
                return;
            }
            if (maintenant - imagesDepuis > 10000)
                Alerte("AFFICHAGE BLOQUE", "aucune image depuis " + (maintenant - imagesDepuis) / 1000 + " s");
        }

        private void Alerte(string titre, string detail)
        {
            if (alerte) return;
            alerte = true;
            try
            {
                var c = Services.Canvas;
                var fond = new Pen(Color.FromArgb(176, 92, 12));
                var texte = new Pen(Color.White);
                c.DrawFilledRectangle(fond, 0, 0, Services.ScreenW, 138);
                c.DrawString(titre + " : " + detail + "   (" + Services.OsName + " " + Services.Version + ")", Services.Font, texte, 12, 10);
                c.DrawString("Derniere erreur : " + (Diagnostic.LastError ?? "aucune") + "   (" + Diagnostic.ErrorCount + " au total)",
                             Services.Font, texte, 12, 34);
                int y = 58;
                for (int i = Diagnostic.Errors.Count - 2; i >= 0 && y <= 82; i--, y += 24)
                    c.DrawString("Avant : " + Diagnostic.Errors[i], Services.Font, texte, 12, y);
                c.DrawString("Le systeme tourne encore. Prenez cet ecran en photo (F12 : details).", Services.Font, texte, 12, 110);
                c.Display();
            }
            catch (Exception) { }
        }
    }
}
