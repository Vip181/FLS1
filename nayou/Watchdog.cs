using Cosmos.System.Graphics.Fonts;
using System;
using System.Drawing;
using Cosmos.System.Graphics;   // Pen, Canvas
using FileSystemOS.Processus;

namespace FileSystemOS.Noyau
{
    /// <summary>
    /// CHIEN DE GARDE : une horloge materielle (PIT) le reveille chaque seconde, meme si un
    /// thread est bloque dans une boucle. Si l'ordonnanceur n'avance plus pendant 3 secondes,
    /// il affiche en rouge le processus et le thread fautifs.
    /// (Si l'ecran reste fige SANS bandeau rouge, c'est une erreur du processeur, pas un blocage.)
    /// </summary>
    public static class Watchdog
    {
        public static Process CurrentProcess;     // mis a jour par l'ordonnanceur avant chaque thread
        public static KThread CurrentThread;

        /// <summary>
        /// Ce que le systeme fait EXACTEMENT a cet instant (programme, ordonnanceur, nettoyage de
        /// la memoire, attente du SSD...). Affiche dans le bandeau rouge et note dans le journal :
        /// c'est ce qui permet de savoir ou un blocage s'est produit.
        /// </summary>
        public static string Etape = "demarrage";
        public const string EtapeDisque = "attente du SSD";
        public static byte DisqueCommande;
        public static ulong DisqueSecteur;

        /// <summary>Operation longue autorisee (ramasse-miettes) : on patiente plus avant d'alerter.</summary>
        public static bool Lent;

        /// <summary>Vrai pendant une alerte de blocage : on n'ecrit alors rien sur le SSD.</summary>
        public static bool EnAlerte;
        private const int SeuilLent = 20;

        /// <summary>Secondes de blocage avant le redemarrage automatique (0 = jamais).</summary>
        public static int RedemarrageAuto = 5;
        public static bool Actif = true;

        private static uint lastTicks;
        private static int still;
        private static bool redemarre;
        private const int Seuil = 3;          // secondes avant d'afficher le bandeau rouge

        public static void Start()
        {
            try { Cosmos.HAL.Global.PIT.RegisterTimer(new Cosmos.HAL.PIT.PITTimer(Check, 1000000000UL, true)); }
            catch (Exception) { }
        }

        private static void Check()
        {
            var s = Services.Scheduler;
            if (s == null || !Actif) return;

            // L'ordonnanceur avance : tout va bien
            if (s.TotalTicks != lastTicks) { lastTicks = s.TotalTicks; still = 0; EnAlerte = false; return; }

            still++;
            int seuil = Lent ? SeuilLent : Seuil;
            if (still < seuil) return;

            EnAlerte = true;
            int restant = RedemarrageAuto - (still - seuil);
            Afficher(restant);

            // Plus rien ne repond : on redemarre nous-memes (le systeme ne peut plus le faire)
            if (RedemarrageAuto > 0 && restant <= 0 && !redemarre)
            {
                redemarre = true;
                try { FileSystemOS.Systeme.Journal.Ajouter("redemarrage apres blocage : " + Ou() + " - " + Description(), null); }
                catch (Exception) { }
                FileSystemOS.Systeme.Redemarrage.Maintenant();
                Afficher(-1);     // si on est encore la, la machine refuse de repartir
            }
        }

        private static string Ou() =>
            (CurrentProcess != null ? CurrentProcess.Name : "?") + " / " + (CurrentThread != null ? CurrentThread.Name : "?");

        private static string Description()
        {
            if (Etape != EtapeDisque) return Etape;
            return Etape + " (commande " + FileSystemOS.Utils.Fmt.Hex(DisqueCommande, 2) + ", secteur " + DisqueSecteur + ")";
        }

        private static void Afficher(int restant)
        {
            try
            {
                var c = Services.Canvas;
                var fond = new Pen(Color.FromArgb(150, 20, 24));
                var texte = new Pen(Color.White);
                c.DrawFilledRectangle(fond, 0, 0, Services.ScreenW, 110);
                c.DrawString("BLOCAGE dans : " + Ou() + "   (" + Services.OsName + " " + Services.Version + ")",
                             Services.Font, texte, 12, 10);
                c.DrawString("Etape : " + Description(), Services.Font, texte, 12, 82);
                c.DrawString("Le systeme ne repond plus depuis " + still + " s. Prenez cet ecran en photo.",
                             Services.Font, texte, 12, 34);
                string bas = restant > 0 ? "Redemarrage automatique dans " + restant + " s..."
                           : (restant == 0 ? "Redemarrage en cours..."
                                           : "Le redemarrage a echoue (" + FileSystemOS.Systeme.Redemarrage.Derniere +
                                             ") : appuyez sur le bouton d'alimentation.");
                c.DrawString(bas, Services.Font, texte, 12, 58);
                c.Display();
            }
            catch (Exception) { }
        }
    }
}
