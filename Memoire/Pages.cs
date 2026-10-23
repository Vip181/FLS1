using System;
using System.Collections.Generic;
using FileSystemOS.Processus;
using FileSystemOS.Utils;

namespace FileSystemOS.Memoire
{
    /// <summary>Attributs d'une page : qui a le droit d'en faire quoi.</summary>
    public static class Attr
    {
        public const int Lecture = 1;        // R
        public const int Ecriture = 2;       // W
        public const int Execution = 4;      // X
        public const int Noyau = 8;          // reservee au systeme
        public const int Partagee = 16;      // plusieurs processus peuvent la lire
        public const int Verrouillee = 32;   // jamais liberee automatiquement

        public const int Donnees = Lecture | Ecriture;              // page de donnees ordinaire
        public const int Code = Lecture | Execution;                // page de code (programme)
        public const int Systeme = Lecture | Ecriture | Noyau;

        public static string Texte(int a)
        {
            string s = "";
            s += (a & Lecture) != 0 ? "r" : "-";
            s += (a & Ecriture) != 0 ? "w" : "-";
            s += (a & Execution) != 0 ? "x" : "-";
            if ((a & Noyau) != 0) s += " noyau";
            if ((a & Partagee) != 0) s += " partagee";
            if ((a & Verrouillee) != 0) s += " verrou";
            return s;
        }
    }

    /// <summary>Une page de 4 Ko attribuee a un processus (et eventuellement a un thread).</summary>
    public class Page
    {
        public int Index;            // numero dans le pool
        public uint Adresse;         // adresse physique du debut
        public int Attributs;
        public Process Proc;         // processus proprietaire (null = noyau)
        public KThread Thread;       // thread proprietaire (facultatif)
        public string Programme;     // nom du programme / de l'usage
        public uint Tour;            // tour d'ordonnanceur de l'attribution

        public string Ligne =>
            "p" + Fmt.Pad(Index.ToString(), 4) + " 0x" + Fmt.Hex(Adresse) + "  " +
            Fmt.Pad(Attr.Texte(Attributs), 18) +
            Fmt.Pad(Proc == null ? "noyau" : "PID " + Proc.Pid + " " + Proc.Name, 22) +
            Fmt.Pad(Thread == null ? "-" : Thread.Name, 12) + Programme;
    }

    /// <summary>
    /// MEMOIRE PAGINEE.
    ///
    /// Une grande reserve est decoupee en pages de 4 Ko. Chaque page attribuee porte :
    ///  - son PROGRAMME (a quoi elle sert) ;
    ///  - son PROCESSUS proprietaire ;
    ///  - son THREAD, quand elle n'appartient qu'a lui ;
    ///  - ses ATTRIBUTS : lecture, ecriture, execution, noyau, partagee, verrouillee.
    ///
    /// Quand un processus se termine, TOUTES ses pages reviennent automatiquement au pool :
    /// un programme ne peut plus fuir de la memoire. Les attributs sont verifies par l'OS
    /// (Ecrire/Lire ci-dessous) : ecrire dans une page de code est refuse.
    ///
    /// Note : c'est une pagination LOGICIELLE. Cosmos garde la table de pages du processeur
    /// en correspondance directe ; changer la vraie MMU casserait le systeme.
    /// </summary>
    public static unsafe class Pages
    {
        public const uint Taille = 4096;
        public static int Total;

        private static byte[] reserve;          // garde la memoire vivante (ramasse-miettes)
        private static byte* pool;
        private static Page[] table;
        private static bool[] libre;
        private static int libres;

        public static uint Attributions, Liberations, Refus;

        public static int Libres => libres;
        public static int Utilisees => Total - libres;
        public static long Octets => (long)Total * Taille;

        public static void Init(int nombreDePages)
        {
            if (table != null) return;
            if (nombreDePages < 16) nombreDePages = 16;
            Total = nombreDePages;
            reserve = new byte[(long)(Total + 1) * Taille];
            fixed (byte* p = reserve) { pool = p; }
            uint a = ((uint)pool + Taille - 1) & ~(Taille - 1);
            pool = (byte*)a;                      // debut aligne sur une page

            table = new Page[Total];
            libre = new bool[Total];
            for (int i = 0; i < Total; i++)
            {
                libre[i] = true;
                table[i] = new Page();
                table[i].Index = i;
                table[i].Adresse = (uint)(pool + (long)i * Taille);
            }
            libres = Total;
        }

        /// <summary>Attribue une page. Renvoie null si la reserve est pleine.</summary>
        public static Page Allouer(string programme, int attributs, Process proc, KThread thread)
        {
            if (table == null) return null;
            for (int i = 0; i < Total; i++)
            {
                if (!libre[i]) continue;
                libre[i] = false;
                libres--;
                Page p = table[i];
                p.Attributs = attributs;
                p.Proc = proc;
                p.Thread = thread;
                p.Programme = programme;
                p.Tour = Services.Scheduler == null ? 0 : Services.Scheduler.TotalTicks;
                Effacer(p);
                Attributions++;
                return p;
            }
            Refus++;
            return null;
        }

        /// <summary>Attribue plusieurs pages d'un coup (null si pas assez de place).</summary>
        public static List<Page> Allouer(int nombre, string programme, int attributs, Process proc, KThread thread)
        {
            if (nombre > libres) { Refus++; return null; }
            var r = new List<Page>();
            for (int i = 0; i < nombre; i++)
            {
                Page p = Allouer(programme, attributs, proc, thread);
                if (p == null) { for (int k = 0; k < r.Count; k++) Liberer(r[k]); return null; }
                r.Add(p);
            }
            return r;
        }

        private static void Effacer(Page p)
        {
            byte* d = (byte*)p.Adresse;
            for (uint i = 0; i < Taille; i++) d[i] = 0;
        }

        public static void Liberer(Page p)
        {
            if (p == null || table == null || libre[p.Index]) return;
            libre[p.Index] = true;
            libres++;
            p.Proc = null;
            p.Thread = null;
            p.Programme = "";
            p.Attributs = 0;
            Liberations++;
        }

        /// <summary>Rend toutes les pages d'un processus (appele quand il se termine).</summary>
        public static int LibererProcessus(Process proc)
        {
            if (table == null || proc == null) return 0;
            int n = 0;
            for (int i = 0; i < Total; i++)
            {
                if (libre[i] || table[i].Proc != proc) continue;
                if ((table[i].Attributs & Attr.Verrouillee) != 0) continue;
                Liberer(table[i]);
                n++;
            }
            return n;
        }

        /// <summary>Rend les pages d'un thread (les pages du processus restent).</summary>
        public static int LibererThread(KThread t)
        {
            if (table == null || t == null) return 0;
            int n = 0;
            for (int i = 0; i < Total; i++)
            {
                if (libre[i] || table[i].Thread != t) continue;
                Liberer(table[i]);
                n++;
            }
            return n;
        }

        // ---- Acces controle par les attributs ----
        public static bool Ecrire(Page p, uint offset, byte valeur)
        {
            if (p == null || offset >= Taille) return false;
            if ((p.Attributs & Attr.Ecriture) == 0) { Refus++; return false; }
            ((byte*)p.Adresse)[offset] = valeur;
            return true;
        }

        public static int Lire(Page p, uint offset)
        {
            if (p == null || offset >= Taille) return -1;
            if ((p.Attributs & Attr.Lecture) == 0) { Refus++; return -1; }
            return ((byte*)p.Adresse)[offset];
        }

        // ---- Etat ----
        public static int ComptePour(Process proc)
        {
            if (table == null) return 0;
            int n = 0;
            for (int i = 0; i < Total; i++) if (!libre[i] && table[i].Proc == proc) n++;
            return n;
        }

        public static List<string> Etat()
        {
            var r = new List<string>();
            if (table == null) { r.Add("memoire paginee non initialisee"); return r; }
            r.Add("Pages de " + Taille + " octets : " + Utilisees + " utilisees / " + Total +
                  "  (" + Fmt.Size(Octets) + " de reserve)");
            r.Add("Attributions " + Attributions + ", liberations " + Liberations + ", refus " + Refus);
            if (Services.Scheduler != null)
            {
                var procs = Services.Scheduler.Processes;
                for (int i = 0; i < procs.Count; i++)
                {
                    int n = ComptePour(procs[i]);
                    if (n > 0) r.Add("  PID " + Fmt.Pad(procs[i].Pid.ToString(), 4) + Fmt.Pad(procs[i].Name, 16) + n + " page(s)");
                }
            }
            int noyau = 0;
            for (int i = 0; i < Total; i++) if (!libre[i] && table[i].Proc == null) noyau++;
            if (noyau > 0) r.Add("  noyau                 " + noyau + " page(s)");
            return r;
        }

        public static List<string> Detail(int maximum)
        {
            var r = new List<string>();
            if (table == null) return r;
            for (int i = 0; i < Total && r.Count < maximum; i++)
                if (!libre[i]) r.Add(table[i].Ligne);
            if (r.Count == 0) r.Add("aucune page attribuee");
            return r;
        }
    }
}
