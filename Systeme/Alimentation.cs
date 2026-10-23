using Cosmos.System.Graphics;
using FileSystemOS.Demarrage;
using FileSystemOS.Processus;


namespace FileSystemOS.Systeme
{
    /// <summary>
    /// Extinction et redemarrage. Les fenetres, la console et les commandes ne coupent
    /// plus la machine directement : elles DEMANDENT, et le processus "alimentation"
    /// affiche l'ecran d'arret (logo V) avant d'eteindre proprement.
    /// </summary>
    public static class Alimentation
    {
        public const int Aucune = 0, Arret = 1, Redemarrage = 2;
        public static int Demande;

        public static void Arreter() { if (Demande == Aucune) Demande = Arret; }
        public static void Redemarrer() { if (Demande == Aucune) Demande = Redemarrage; }
    }

    public class AlimentationThread : KThread
    {
        private BootScreen screen;
        private uint frame;
        private string liveMsg = "Fermeture des processus...";
        private const int Frames = 20;      // environ 1 s d'ecran d'arret, puis on coupe

        public AlimentationThread() : base("veille") { }

        public override void Step()
        {
            if (Alimentation.Demande == Alimentation.Aucune) return;

            if (screen == null)
            {
                Services.ShuttingDown = true;   // le compositeur s'arrete
                Services.Memory.Write(Alimentation.Demande == Alimentation.Arret ? "Arret du systeme" : "Redemarrage");
                Journal.Ajouter(Alimentation.Demande == Alimentation.Arret ? "arret" : "redemarrage",
                                Comptes.Actuel?.Nom);
                screen = new BootScreen();
                screen.Begin(Alimentation.Demande == Alimentation.Arret ? "Arret en cours" : "Redemarrage en cours");
                if (Live.Active) liveMsg = "Session Live : " + Live.Save();
            }

            frame++;
            screen.Frame(frame, (int)frame, Frames, liveMsg, null);
            if (frame < Frames) return;

            bool redemarrage = Alimentation.Demande == Alimentation.Redemarrage;
            if (redemarrage) Redemarrage.Maintenant();
            else Redemarrage.Eteindre();

            // Si on arrive ici, la machine n'est pas partie : on le dit, et on recommence
            // toutes les 2 secondes au lieu de rester fige sur "Redemarrage en cours".
            Redemarrage.Echec(Services.Canvas, Services.Font, redemarrage);
            frame = 0;          // on recommence le compte a rebours : nouvel essai dans ~1 s
        }
    }
}
