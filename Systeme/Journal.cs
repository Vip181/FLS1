using System;
using System.Collections.Generic;
using System.IO;
using FileSystemOS.Graphique;
using FileSystemOS.Pilotes;
using FileSystemOS.Utils;

namespace FileSystemOS.Systeme
{
    /// <summary>
    /// JOURNAL DES CONNEXIONS : qui s'est connecte, quand, depuis quelle machine, et comment
    /// l'OS a demarre (disque interne, cle USB / ISO en mode Live).
    ///
    /// Ecrit dans \FSOS\Systeme\journal-connexions.txt des que l'OS est installe sur un disque
    /// (ordinateur ou cle). En mode Live sans disque, il reste en memoire pour la session.
    /// </summary>
    public static class Journal
    {
        public const int MaxLignes = 200;
        public static readonly List<string> Lignes = new List<string>();
        public static string Fichier = "(non enregistre)";

        public static string Horodatage()
        {
            try
            {
                return "20" + Fmt.Two(Cosmos.HAL.RTC.Year) + "-" + Fmt.Two(Cosmos.HAL.RTC.Month) + "-" +
                       Fmt.Two(Cosmos.HAL.RTC.DayOfTheMonth) + " " + Fmt.Two(Cosmos.HAL.RTC.Hour) + ":" +
                       Fmt.Two(Cosmos.HAL.RTC.Minute) + ":" + Fmt.Two(Cosmos.HAL.RTC.Second);
            }
            catch (Exception) { return "date inconnue"; }
        }

        /// <summary>Support de demarrage : disque interne, cle USB / ISO (mode Live), ou machine virtuelle.</summary>
        public static string Support()
        {
            if (Live.Active) return "mode Live (cle USB / ISO)";
            string root = InstallerWindow.FindRoot();
            if (root == null) return "non installe";
            string genre = "disque";
            try { genre = Stockage.Genre(Services.FileSystem.GetTotalSize(root.Substring(0, 3))); }
            catch (Exception) { }
            return "installe sur " + root.Substring(0, 3) + " (" + genre + ")";
        }

        private static string CheminFichier()
        {
            string root = InstallerWindow.FindRoot();
            return root == null ? null : root + "\\Systeme\\journal-connexions.txt";
        }

        /// <summary>Ajoute un evenement et l'enregistre aussitot.</summary>
        public static void Ajouter(string evenement, string utilisateur)
        {
            FileSystemOS.Noyau.Suivi.Etape("journal : " + (evenement ?? ""));
            string ligne = Horodatage() + "  " + Fmt.Pad(evenement, 22) +
                           Fmt.Pad(utilisateur == null ? "-" : utilisateur, 16) +
                           Machine.Marque + " / " + Support();
            Lignes.Add(ligne);
            while (Lignes.Count > MaxLignes) Lignes.RemoveAt(0);
            Enregistrer();
        }

        public static void Enregistrer()
        {
            string p = CheminFichier();
            if (p == null) { Fichier = "(mode Live sans disque : journal en memoire)"; return; }
            try
            {
                var contenu = new List<string>();
                contenu.Add("# Journal des connexions - " + Services.OsName + " " + Services.Version);
                contenu.Add("# date et heure        evenement             utilisateur     machine / support");
                for (int i = 0; i < Lignes.Count; i++) contenu.Add(Lignes[i]);
                FsTools.WriteBytes(p, TextCodec.Encode(contenu));
                Fichier = p;
                Live.SaveIfActive();
            }
            catch (Exception e) { Fichier = "erreur : " + e.Message; }
        }

        /// <summary>Relit le journal existant au demarrage (pour garder l'historique).</summary>
        public static int Charger()
        {
            Lignes.Clear();
            string p = CheminFichier();
            if (p == null) return 0;
            try
            {
                if (!FsTools.Exists(p)) return 0;
                var l = TextCodec.ReadLines(p);
                for (int i = 0; i < l.Count; i++)
                    if (l[i].Length > 0 && l[i][0] != '#') Lignes.Add(l[i]);
                while (Lignes.Count > MaxLignes) Lignes.RemoveAt(0);
                Fichier = p;
            }
            catch (Exception) { }
            return Lignes.Count;
        }

        public static List<string> Dernieres(int n)
        {
            var r = new List<string>();
            int debut = Lignes.Count > n ? Lignes.Count - n : 0;
            for (int i = debut; i < Lignes.Count; i++) r.Add(Lignes[i]);
            if (r.Count == 0) r.Add("(aucune connexion enregistree)");
            return r;
        }
    }
}
