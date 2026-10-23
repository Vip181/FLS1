using System.Collections.Generic;
using FileSystemOS.Graphique;

namespace FileSystemOS.Systeme
{
    /// <summary>Ouverture / verrouillage de session : rien n'est accessible sans connexion.</summary>
    public static class Session
    {
        private static readonly List<Window> hidden = new List<Window>();
        private static bool firstLogin = true;

        /// <summary>Cache toutes les fenetres et affiche la page de connexion plein ecran.</summary>
        public static void ShowLogin() => AfficherConnexion("demarrage");

        /// <summary>
        /// Au demarrage : DIRECTEMENT AU BUREAU avec le dernier compte utilise (reglage
        /// connexion_auto=oui dans demarrage.cfg, actif par defaut). Il faut qu'un compte existe :
        /// au tout premier demarrage, la page de creation du compte s'affiche d'abord.
        /// Le verrouillage et la deconnexion demandent toujours le mot de passe.
        /// </summary>
        public static bool ConnexionAuto()
        {
            Comptes.Load();
            if (Comptes.Liste.Count == 0) return false;
            if (!Config.GetBool("connexion_auto", true)) return false;
            Compte c = Comptes.Find(Config.Get("dernier_utilisateur", ""));
            for (int i = 0; i < Comptes.Liste.Count && c == null; i++)
                if (Comptes.Liste[i].Admin) c = Comptes.Liste[i];
            if (c == null) c = Comptes.Liste[0];
            Journal.Ajouter("connexion automatique", c.Nom);
            FileSystemOS.Noyau.Suivi.Commencer();
            Start(c);
            FileSystemOS.Noyau.Suivi.Finir();
            return true;
        }

        private static void AfficherConnexion(string raison)
        {
            Services.LoggedIn = false;
            hidden.Clear();
            var all = Services.Windows.All;
            for (int i = 0; i < all.Count; i++)
                if (!all[i].Hidden) { hidden.Add(all[i]); all[i].Minimized = true; }
            FileSystemOS.Graphique.Bureau.FermerMenu();
            FileSystemOS.Graphique.PageConnexion.Ouvrir(raison);
            Services.Taskbar.Invalidate();
            Services.ScreenDirty = true;
        }

        /// <summary>
        /// DECONNEXION : contrairement au verrouillage, la session est fermee. Les fenetres de
        /// l'utilisateur sont fermees, ses programmes arretes, et la page de connexion s'affiche
        /// avec les champs vides : le prochain utilisateur ecrit son nom et son mot de passe.
        /// </summary>
        public static void Deconnecter()
        {
            if (Comptes.Actuel != null) Journal.Ajouter("deconnexion", Comptes.Actuel.Nom);
            Live.SaveIfActive();

            var wm = Services.Windows;
            for (int i = wm.All.Count - 1; i >= 0; i--)
            {
                Window w = wm.All[i];
                if (!w.Fixed) wm.Close(w);
            }
            // Programmes de l'utilisateur (FSL, jeux...) : arretes ; les processus systeme restent
            var procs = Services.Scheduler.Processes;
            for (int i = 0; i < procs.Count; i++)
                if (!procs[i].Critical && Comptes.Actuel != null && procs[i].Utilisateur == Comptes.Actuel.Nom) procs[i].Kill();

            Comptes.Actuel = null;
            FileSystemOS.Graphique.Bureau.Icones.Clear();
            firstLogin = true;                 // la prochaine connexion rouvre les fenetres de depart
            AfficherConnexion("deconnexion");
            hidden.Clear();
        }

        public static void Start(Compte c)
        {
            bool reprise = Comptes.Actuel == c && !firstLogin;
            Comptes.Actuel = c;
            FileSystemOS.Noyau.Suivi.Etape("session : journal de connexion");
            Journal.Ajouter(reprise ? "deverrouillage" : "connexion", c.Nom);
            Services.LoggedIn = true;
            // Le compte choisi sera propose (ou ouvert) au prochain demarrage
            if (!reprise && Config.Get("dernier_utilisateur", "") != c.Nom)
            {
                FileSystemOS.Noyau.Suivi.Etape("session : dernier utilisateur dans demarrage.cfg");
                Config.SetAndSave("demarrage.cfg", "dernier_utilisateur", c.Nom);
                FileSystemOS.Noyau.Suivi.Etape("session : enregistrement sur le SSD");
                Live.SaveIfActive();
            }
            FileSystemOS.Graphique.PageConnexion.Fermer();
            FileSystemOS.Noyau.Suivi.Etape("session : chargement du bureau");
            try { FileSystemOS.Graphique.Bureau.Charger(); } catch (System.Exception) { }
            if (Services.Memory != null) Services.Memory.Write("Connexion : " + c.Nom);

            var wm = Services.Windows;
            if (firstLogin)
            {
                firstLogin = false;
                FileSystemOS.Noyau.Suivi.Etape("session : fenetre Alimentation");
                wm.Open(Services.PowerWin);
                FileSystemOS.Noyau.Suivi.Etape("session : fenetre Console");
                wm.Open(Services.ConsoleWin);
                FileSystemOS.Noyau.Suivi.Etape("session : fenetre Explorateur");
                wm.Open(Services.ExplorerWin);
                FileSystemOS.Noyau.Suivi.Etape("session : pilotes FSL");
                PilotesFsl.DemarrerAuLancement();
                FileSystemOS.Noyau.Suivi.Etape("session : guide du FSL");
                GuideFsl.CreerSiAbsent();
                FileSystemOS.Noyau.Suivi.Etape("session : ouverte");
                // Noyau du SSD different de celui qui tourne (demarrage sur une cle plus recente ?)
                if (SessionSsd.VersionNoyauSsd != null && SessionSsd.VersionNoyauSsd != Services.Version)
                    Services.AlerteWin.Montrer("Mise a jour du SSD",
                        "Ce demarrage : " + Services.Version + "   -   noyau sur le SSD : " + SessionSsd.VersionNoyauSsd,
                        "Pour mettre le SSD a jour : sous Windows, relancez Outils\\installer-ssd.ps1");
            }
            else
            {
                for (int i = 0; i < hidden.Count; i++)
                    if (wm.All.Contains(hidden[i])) wm.ToggleFromTaskbar(hidden[i]);
                hidden.Clear();
            }
            Services.Taskbar.Invalidate();
            Services.ScreenDirty = true;
        }

        public static void Lock()
        {
            if (Comptes.Actuel != null) Journal.Ajouter("verrouillage", Comptes.Actuel.Nom);
            AfficherConnexion("verrouillage");
        }
    }
}
