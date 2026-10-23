using System;
using Cosmos.System.Graphics;
using Cosmos.System.Graphics.Fonts;
using FileSystemOS.Demarrage;
using FileSystemOS.Graphique;
using FileSystemOS.Processus;
using Sys = Cosmos.System;

namespace FileSystemOS
{
    /// <summary>
    /// File System OS - File System OS Company - Vincent Senut
    ///
    /// Le noyau ne prepare que le strict minimum (ecran, souris, ordonnanceur),
    /// puis lance le processus "demarrage" : il affiche le logo V anime et charge
    /// un par un les modules du systeme (Demarrage/Modules), chacun avec ses
    /// processus et ses threads.
    /// </summary>
    public class Kernel : Sys.Kernel
    {
        protected override void BeforeRun()
        {
            // 1. ECRAN D'ABORD : on ouvre un affichage utilisable avant toute autre chose,
            //    pour que chaque etape suivante soit visible (plus d'ecran noir muet).
            Systeme.Amorce.Ouvrir();

            // 2. Disque memoire (mode Live) : grosse reservation, on la rend facultative
            Systeme.Amorce.Faire("disque memoire (mode Live)", () => Systeme.Live.Create());
            Systeme.Amorce.Faire("disques SATA (AHCI)", () =>
            {
                int n = Pilotes.Ahci.Detecter();
                Systeme.Amorce.Etape("    " + (n > 0 ? n + " disque(s) SATA : " + Pilotes.Ahci.Journal : Pilotes.Ahci.Journal));
            });
            Systeme.Amorce.Faire("disques NVMe (nouvelle generation)", () =>
            {
                int n = Pilotes.Nvme.Detecter();
                Systeme.Amorce.Etape("    " + (n > 0 ? n + " disque(s) NVMe : " + Pilotes.Nvme.Journal : Pilotes.Nvme.Journal));
            });
            Systeme.Amorce.Faire("montage des disques", () => Demarrage.Modules.FichiersModule.Mount());
            Systeme.Amorce.Faire("preparation du mode Live", () => Systeme.Amorce.Etape("    " + Systeme.Live.Prepare()));
            Systeme.Amorce.Faire("session sur le SSD", () =>
            {
                FileSystemOS.Noyau.Suivi.Commencer();
                string etat = Systeme.SessionSsd.Charger();
                FileSystemOS.Noyau.Suivi.Finir();
                Systeme.Amorce.Etape("    " + etat);
            });
            Systeme.Amorce.Faire("fichiers internes", () => Systeme.Config.LoadAll());

            // 3. Resolution demandee dans demarrage.cfg (si elle differe de celle de l'amorce)
            Systeme.Amorce.Faire("resolution de l'ecran", () =>
            {
                string voulu = Systeme.Config.Get("resolution", "auto");
                Canvas c = Systeme.Materiel.OpenScreen(voulu);
                if (c != null) Services.Canvas = c;
            });

            if (Services.Canvas == null)
            {
                Systeme.EcranSecours.Diagnostic();      // vraiment aucun ecran : diagnostic texte
                return;
            }

            if (!Systeme.Amorce.Faire("buffers graphiques", () => Services.Compositor = new Compositor())) return;

            Systeme.Amorce.Faire("souris", () =>
            {
                Sys.MouseManager.ScreenWidth = (uint)Services.ScreenW;
                Sys.MouseManager.ScreenHeight = (uint)Services.ScreenH;
                Sys.MouseManager.X = (uint)(Services.ScreenW / 2);
                Sys.MouseManager.Y = (uint)(Services.ScreenH / 2);
                MouseCursor.Build();
            });

            Systeme.Amorce.Faire("ordonnanceur", () =>
            {
                Services.Scheduler = new Scheduler();
                Process boot = Services.Scheduler.Spawn("demarrage", true, Prio.Haute);
                boot.Start(new BootThread(BootSequence.Stages()));
            });

            Systeme.Amorce.Faire("horloge du systeme (tranches de temps)", () => Processus.Horloge.Start());
            Systeme.Amorce.Faire("chien de garde", () => FileSystemOS.Noyau.Watchdog.Start());
            Systeme.Amorce.Etape("Interface graphique...");
        }

        protected override void Run()
        {
            Services.Scheduler.Tick();

            // Ramasse-miettes a la fin du tour, hors de tout thread (methode conseillee par Cosmos)
            if (Services.GcRequest)
            {
                Services.GcRequest = false;
                FileSystemOS.Noyau.Watchdog.Etape = "nettoyage de la memoire (ramasse-miettes)";
                FileSystemOS.Noyau.Watchdog.Lent = true;
                Cosmos.Core.Memory.Heap.Collect();
                FileSystemOS.Noyau.Watchdog.Lent = false;
                FileSystemOS.Noyau.Watchdog.Etape = "ordonnanceur";
            }
        }
    }
}
