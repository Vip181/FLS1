using System;
using System.Collections.Generic;
using FileSystemOS.Fsl;
using FileSystemOS.Processus;

namespace FileSystemOS.Systeme
{
    /// <summary>
    /// SENTINELLE : surveille les programmes et arrete ceux qui sont CORROMPUS,
    /// sans jamais faire tomber l'OS.
    ///
    /// Trois niveaux :
    ///  1. AVANT de lancer : le bytecode est verifie (en-tete, instructions connues, sauts
    ///     dans les bornes, numeros de variables valides). Un fichier abime est refuse.
    ///  2. PENDANT : chaque erreur d'un thread est comptee. Au-dela de la limite, le processus
    ///     est arrete et mis en quarantaine.
    ///  3. APRES : le programme fautif est inscrit dans une liste, avec la raison.
    /// </summary>
    public static class Sentinelle
    {
        public const int ErreursMax = 5;

        public class Incident
        {
            public string Programme, Raison;
            public string Heure;
        }

        public static readonly List<Incident> Incidents = new List<Incident>();
        public static uint Arretes, Refuses;
        public static string Dernier = "";

        /// <summary>Verifie un programme compile. Renvoie null si tout va bien, sinon la raison.</summary>
        public static string Verifier(FslProgram p, string nom)
        {
            if (p == null) return "programme vide";
            if (p.Code.Count == 0) return "aucune instruction";
            if (p.Code.Count != p.Lines.Count) return "bytecode incoherent (lignes manquantes)";

            // Table des fonctions du programme
            for (int k = 0; k < p.Fonctions.Count; k++)
            {
                FonctionFsl f = p.Fonctions[k];
                if (f.Entree < 0 || f.Fin > p.Code.Count || f.Entree >= f.Fin) return "fonction '" + f.Nom + "' mal placee";
                if (f.Locales.Count != f.Types.Count || f.NbParams > f.Locales.Count) return "fonction '" + f.Nom + "' incoherente";
            }

            int i = 0;
            while (i < p.Code.Count)
            {
                int op = p.Code[i];
                if (op < Op.Halt || op > Op.Retour) return "instruction inconnue (" + op + ") a la position " + i;

                // Instructions propres aux fonctions : seulement dans le corps d'une fonction
                if (op == Op.LoadL || op == Op.StoreL || op == Op.Retour)
                {
                    FonctionFsl dans = null;
                    for (int k = 0; k < p.Fonctions.Count; k++)
                        if (i >= p.Fonctions[k].Entree && i < p.Fonctions[k].Fin) dans = p.Fonctions[k];
                    if (dans == null) return Op.Noms[op] + " en dehors d'une fonction (position " + i + ")";
                    if (op != Op.Retour)
                    {
                        if (i + 2 > p.Code.Count) return "instruction coupee a la fin du fichier";
                        int s = p.Code[i + 1];
                        if (s < 0 || s >= dans.Locales.Count) return "variable locale inexistante dans '" + dans.Nom + "'";
                    }
                    i += Op.Taille(op);
                    continue;
                }
                if (op == Op.AppelF)
                {
                    if (i + 3 > p.Code.Count) return "instruction coupee a la fin du fichier";
                    int f = p.Code[i + 1], n = p.Code[i + 2];
                    if (f < 0 || f >= p.Fonctions.Count) return "fonction du programme inexistante (numero " + f + ")";
                    if (n != p.Fonctions[f].NbParams) return "mauvais nombre d'arguments pour " + p.Fonctions[f].Nom;
                    i += 3;
                    continue;
                }

                int taille = 1;
                if (op == Op.Appel)
                {
                    if (i + 3 > p.Code.Count) return "instruction coupee a la fin du fichier";
                    int f = p.Code[i + 1], n = p.Code[i + 2];
                    if (f < 0 || f >= Natives.Noms.Length) return "fonction inexistante (numero " + f + ")";
                    if (n != Natives.NbArgs[f]) return "mauvais nombre d'arguments pour " + Natives.Noms[f];
                    i += 3;
                    continue;
                }
                if (op == Op.PushInt || op == Op.Jmp || op == Op.Jz) taille = 2;
                else if (op == Op.PushStr || op == Op.Load || op == Op.Store) taille = 2;

                if (i + taille > p.Code.Count) return "instruction coupee a la fin du fichier";

                if (taille == 2)
                {
                    int arg = p.Code[i + 1];
                    if ((op == Op.Jmp || op == Op.Jz) && (arg < 0 || arg > p.Code.Count))
                        return "saut hors du programme (position " + arg + ")";
                    if (op == Op.PushStr && (arg < 0 || arg >= p.Strings.Count))
                        return "texte inexistant (numero " + arg + ")";
                    if ((op == Op.Load || op == Op.Store) && (arg < 0 || arg >= p.VarNames.Count))
                        return "variable inexistante (numero " + arg + ")";
                }
                i += taille;
            }
            return null;
        }

        public static void Refuser(string programme, string raison)
        {
            Refuses++;
            Noter(programme, "refuse : " + raison);
        }

        /// <summary>Un processus a trop d'erreurs : on l'arrete proprement.</summary>
        public static void Arreter(Process p, string raison)
        {
            Arretes++;
            Noter(p.Name, raison);
            p.Kill();
        }

        private static void Noter(string programme, string raison)
        {
            var inc = new Incident();
            inc.Programme = programme;
            inc.Raison = raison;
            inc.Heure = Journal.Horodatage();
            Incidents.Add(inc);
            while (Incidents.Count > 20) Incidents.RemoveAt(0);

            Dernier = "PROGRAMME CORROMPU : " + programme + " - " + raison;
            try
            {
                Services.AlerteWin.Montrer("Programme corrompu", programme, raison);
                Services.Windows.Open(Services.AlerteWin);
            }
            catch (Exception) { }
            try { Journal.Ajouter("programme corrompu", programme); } catch (Exception) { }
        }

        public static List<string> Etat()
        {
            var r = new List<string>();
            r.Add("Sentinelle : " + Refuses + " programme(s) refuse(s), " + Arretes + " arrete(s), limite " +
                  ErreursMax + " erreurs par thread");
            for (int i = 0; i < Incidents.Count; i++)
                r.Add("  " + Incidents[i].Heure + "  " + Incidents[i].Programme + " : " + Incidents[i].Raison);
            if (Incidents.Count == 0) r.Add("  aucun incident");
            return r;
        }
    }
}
