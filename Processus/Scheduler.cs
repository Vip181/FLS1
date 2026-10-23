using System.Collections.Generic;

namespace FileSystemOS.Processus
{
    /// <summary>
    /// Thread cooperatif : Step() fait une petite tranche de travail puis rend la main.
    /// (Cosmos n'a pas encore de threads preemptifs stables, d'ou ce modele.)
    /// </summary>
    public abstract class KThread
    {
        private static int nextTid = 1;

        public readonly int Tid;
        public readonly string Name;
        public bool Alive = true;
        public uint Ticks;
        public uint Errors;          // exceptions rattrapees par l'ordonnanceur
        public int EtatCourant = Etat.Nouveau;
        public uint TempsCpu;        // millisecondes consommees
        public uint Depassements;    // tranches de temps depassees
        public uint PireTranche;     // plus longue tranche observee (ms)

        /// <summary>
        /// EN ATTENTE : l'ordonnanceur ne l'execute plus, sans le detruire (ses donnees, ses
        /// fenetres et sa memoire restent). Mis par l'utilisateur, ou automatiquement quand le
        /// thread plante : il est alors mis de cote au lieu de replanter a chaque tour.
        /// </summary>
        public bool EnAttente;
        public string RaisonAttente = "";

        /// <summary>Attribue une page de memoire a CE thread (rendue quand il se termine).</summary>
        public FileSystemOS.Memoire.Page Allouer(string usage, int attributs) =>
            FileSystemOS.Memoire.Pages.Allouer(usage, attributs, Owner, this);
        public Process Owner;

        protected KThread(string name)
        {
            Tid = nextTid++;
            Name = name;
        }

        public abstract void Step();
    }

    /// <summary>Un processus = un nom, un PID et un ou plusieurs threads.</summary>
    public class Process
    {
        private static int nextPid = 1;

        public readonly int Pid;
        public readonly string Name;
        public readonly bool Critical;
        public readonly List<KThread> Threads = new List<KThread>();
        public bool Alive = true;
        public bool Suspended;   // gele : l'ordonnanceur ne l'execute plus (buffer d'attente)
        public int Priority = Prio.Normale;
        public int EtatCourant = Etat.Nouveau;
        public string Utilisateur = "systeme";    // proprietaire (compte connecte)
        public uint TempsCpu;                     // millisecondes consommees
        public uint Commutations;                 // fois ou il a recu le processeur

        public Process(string name, bool critical)
        {
            Pid = nextPid++;
            Name = name;
            Critical = critical;
        }

        public KThread Start(KThread t)
        {
            t.Owner = this;
            Threads.Add(t);
            return t;
        }

        /// <summary>Pages de memoire attribuees a ce processus (voir Memoire/Pages.cs).</summary>
        public int PagesUtilisees => FileSystemOS.Memoire.Pages.ComptePour(this);

        public FileSystemOS.Memoire.Page Allouer(string usage, int attributs) =>
            FileSystemOS.Memoire.Pages.Allouer(usage, attributs, this, null);

        public void Kill()
        {
            Alive = false;
            EtatCourant = Etat.Termine;
            for (int i = 0; i < Threads.Count; i++) Threads[i].Alive = false;
            FileSystemOS.Memoire.Pages.LibererProcessus(this);   // la memoire revient au systeme
        }
    }

    /// <summary>Priorites : Haute et Normale a chaque tour, Basse un tour sur quatre.</summary>
    public static class Prio
    {
        public const int Haute = 0, Normale = 1, Basse = 2;
        public static string Name(int p) => p == Haute ? "haute" : (p == Basse ? "basse" : "normale");
    }

    /// <summary>Ordonnanceur round-robin a priorites : chaque thread vivant avance d'un pas par tour.</summary>
    public class Scheduler
    {
        public readonly List<Process> Processes = new List<Process>();
        public uint TotalTicks;

        /// <summary>Tranche de temps accordee a un thread avant qu'on note un depassement (ms).</summary>
        public static uint Quantum = 8;
        public uint Commutations;            // changements de contexte depuis le demarrage
        public uint TempsOccupe;             // millisecondes passees dans les threads

        public Process Spawn(string name, bool critical)
        {
            var p = new Process(name, critical);
            p.EtatCourant = Etat.Pret;
            var u = FileSystemOS.Systeme.Comptes.Actuel;
            p.Utilisateur = critical ? "systeme" : (u == null ? "systeme" : u.Nom);
            Processes.Add(p);
            return p;
        }

        public Process Spawn(string name, bool critical, int priority)
        {
            Process p = Spawn(name, critical);
            p.Priority = priority;
            return p;
        }

        public void Tick()
        {
            for (int i = 0; i < Processes.Count; i++)
            {
                Process p = Processes[i];
                if (!p.Alive) continue;
                if (p.Suspended) { p.EtatCourant = Etat.Gele; continue; }
                if (p.Priority == Prio.Basse && (TotalTicks & 3) != 0) { p.EtatCourant = Etat.Pret; continue; }
                if (p.EtatCourant == Etat.Bloque) continue;                        // attend un verrou
                for (int j = 0; j < p.Threads.Count; j++)
                {
                    KThread t = p.Threads[j];
                    if (!t.Alive) continue;
                    if (t.EnAttente) { t.EtatCourant = Etat.Gele; continue; }

                    // COMMUTATION DE CONTEXTE : le processeur passe a ce thread
                    uint debut = Horloge.Millis;
                    p.EtatCourant = Etat.Elu;
                    t.EtatCourant = Etat.Elu;
                    p.Commutations++;
                    Commutations++;
                    // Un thread qui plante ne fait plus planter tout l'OS : l'erreur est notee
                    FileSystemOS.Noyau.Watchdog.CurrentProcess = p;
                    FileSystemOS.Noyau.Watchdog.CurrentThread = t;
                    FileSystemOS.Noyau.Watchdog.Etape = "programme en cours";
                    try { t.Step(); }
                    catch (System.Exception e)
                    {
                        t.Errors++;
                        FileSystemOS.Noyau.Diagnostic.Report(p.Name + "/" + t.Name, e.Message);
                        // MISE EN ATTENTE AUTOMATIQUE : un thread d'application qui plante est mis
                        // de cote (au lieu de replanter a chaque tour) ; on peut le reprendre ou
                        // l'arreter (Gestionnaire des taches, ou "reprendre PID" dans la Console).
                        if (!p.Critical)
                        {
                            t.EnAttente = true;
                            t.RaisonAttente = e.Message;
                            FileSystemOS.Noyau.Diagnostic.Report("ordonnanceur",
                                "'" + p.Name + "/" + t.Name + "' mis en attente apres une erreur (reprendre " + p.Pid + ")");
                        }
                        // Programme corrompu : la sentinelle l'arrete avant qu'il n'abime le systeme
                        if (t.Errors >= FileSystemOS.Systeme.Sentinelle.ErreursMax && !p.Critical)
                        {
                            FileSystemOS.Systeme.Sentinelle.Arreter(p,
                                "le thread '" + t.Name + "' a echoue " + t.Errors + " fois (" + e.Message + ")");
                            break;
                        }
                    }
                    FileSystemOS.Noyau.Watchdog.Etape = "ordonnanceur (entre deux programmes)";
                    // Fin de la tranche : on mesure ce qu'elle a coute
                    uint duree = Horloge.Millis - debut;
                    t.TempsCpu += duree;
                    p.TempsCpu += duree;
                    TempsOccupe += duree;
                    if (duree > t.PireTranche) t.PireTranche = duree;
                    if (duree > Quantum)
                    {
                        t.Depassements++;
                        // Un thread trop gourmand passe en priorite basse : les autres respirent
                        if (t.Depassements > 20 && !p.Critical && p.Priority != Prio.Basse)
                        {
                            p.Priority = Prio.Basse;
                            FileSystemOS.Noyau.Diagnostic.Report("ordonnanceur",
                                "'" + p.Name + "/" + t.Name + "' depasse sa tranche : priorite abaissee");
                        }
                    }
                    t.EtatCourant = Etat.Pret;
                    t.Ticks++;
                }
                if (p.EtatCourant == Etat.Elu) p.EtatCourant = Etat.Pret;
            }

            for (int i = Processes.Count - 1; i >= 0; i--)
                if (!Processes[i].Alive)
                {
                    Process mort = Processes[i];
                    mort.EtatCourant = Etat.Termine;
                    FileSystemOS.Memoire.Pages.LibererProcessus(mort);
                    Verrou.Oublier(mort);          // il rend ses verrous : plus de blocage
                    Ipc.Oublier(mort);             // sa boite aux lettres disparait
                    Processes.RemoveAt(i);
                }

            TotalTicks++;
        }

        public bool Kill(int pid, out string message)
        {
            for (int i = 0; i < Processes.Count; i++)
            {
                Process p = Processes[i];
                if (p.Pid != pid) continue;
                if (p.Critical)
                {
                    message = "Erreur : '" + p.Name + "' est un processus systeme.";
                    return false;
                }
                var moi = FileSystemOS.Systeme.Comptes.Actuel;
                if (moi != null && !moi.Admin && p.Utilisateur != moi.Nom)
                {
                    message = "Erreur : '" + p.Name + "' appartient a " + p.Utilisateur + ".";
                    return false;
                }
                p.Kill();
                message = "Processus '" + p.Name + "' (PID " + pid + ") arrete.";
                return true;
            }
            message = "Erreur : aucun processus avec le PID " + pid;
            return false;
        }
    }
}
