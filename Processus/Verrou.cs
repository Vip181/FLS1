using System;
using System.Collections.Generic;

namespace FileSystemOS.Processus
{
    /// <summary>
    /// VERROU (exclusion mutuelle) avec DETECTION D'INTERBLOCAGE.
    ///
    /// Un seul processus tient un verrou a la fois. Avant d'accorder une attente, l'OS suit la
    /// chaine "qui attend qui" : si elle revient au demandeur, c'est un interblocage (deadlock),
    /// et la demande est REFUSEE au lieu de figer les deux processus pour toujours.
    /// </summary>
    public class Verrou
    {
        public readonly string Nom;
        public Process Proprietaire;
        public readonly List<Process> Attente = new List<Process>();
        public uint Prises, Conflits;

        public Verrou(string nom) { Nom = nom; Tous.Add(this); }

        public static readonly List<Verrou> Tous = new List<Verrou>();
        public static uint Interblocages;
        public static string DernierInterblocage = "";

        /// <summary>Le processus que 'p' attend actuellement (null s'il n'attend rien).</summary>
        private static Process Attendu(Process p)
        {
            for (int i = 0; i < Tous.Count; i++)
                if (Tous[i].Attente.Contains(p)) return Tous[i].Proprietaire;
            return null;
        }

        /// <summary>Accorder le verrou a 'p' creerait-il un cycle d'attente ?</summary>
        private bool Cycle(Process p)
        {
            Process courant = Proprietaire;
            for (int pas = 0; pas < 32 && courant != null; pas++)
            {
                if (courant == p) return true;
                courant = Attendu(courant);
            }
            return false;
        }

        /// <summary>Prend le verrou. Renvoie false si occupe ; raison = "interblocage" ou "occupe".</summary>
        public bool Prendre(Process p, out string raison)
        {
            raison = "";
            if (Proprietaire == null)
            {
                Proprietaire = p;
                Prises++;
                return true;
            }
            if (Proprietaire == p) { Prises++; return true; }     // deja a lui

            Conflits++;
            if (Cycle(p))
            {
                Interblocages++;
                DernierInterblocage = "'" + p.Name + "' et '" + Proprietaire.Name + "' sur le verrou " + Nom;
                raison = "interblocage evite : " + DernierInterblocage;
                FileSystemOS.Noyau.Diagnostic.Report("verrou " + Nom, raison);
                return false;
            }
            if (!Attente.Contains(p)) Attente.Add(p);
            p.EtatCourant = Etat.Bloque;
            raison = "occupe par " + Proprietaire.Name;
            return false;
        }

        public void Rendre(Process p)
        {
            if (Proprietaire != p) return;
            Proprietaire = null;
            if (Attente.Count > 0)
            {
                Process suivant = Attente[0];
                Attente.RemoveAt(0);
                Proprietaire = suivant;
                suivant.EtatCourant = Etat.Pret;
                Prises++;
            }
        }

        /// <summary>Un processus disparait : on le retire de partout.</summary>
        public static void Oublier(Process p)
        {
            for (int i = 0; i < Tous.Count; i++)
            {
                Tous[i].Attente.Remove(p);
                if (Tous[i].Proprietaire == p) Tous[i].Rendre(p);
            }
        }

        public static List<string> Etat2()
        {
            var r = new List<string>();
            r.Add("Verrous : " + Tous.Count + ", interblocages evites : " + Interblocages +
                  (DernierInterblocage.Length > 0 ? " (dernier : " + DernierInterblocage + ")" : ""));
            for (int i = 0; i < Tous.Count; i++)
            {
                Verrou v = Tous[i];
                r.Add("  " + v.Nom + " : " + (v.Proprietaire == null ? "libre" : "tenu par " + v.Proprietaire.Name) +
                      (v.Attente.Count > 0 ? ", " + v.Attente.Count + " en attente" : "") +
                      "  (" + v.Prises + " prises, " + v.Conflits + " conflits)");
            }
            return r;
        }
    }
}
