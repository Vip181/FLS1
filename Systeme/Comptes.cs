using System;
using System.Collections.Generic;
using System.IO;
using FileSystemOS.Graphique;
using FileSystemOS.Utils;

namespace FileSystemOS.Systeme
{
    public class Compte
    {
        public string Nom, Sel, Hash;
        public bool Admin;
    }

    /// <summary>
    /// Comptes utilisateurs. Fichier interne \FSOS\Systeme\comptes.cfg, une ligne par compte :
    ///   nom:sel:empreinte:admin|utilisateur
    /// Le mot de passe n'est JAMAIS enregistre : seule l'empreinte SHA-256(sel + mot de passe) l'est.
    /// </summary>
    public static class Comptes
    {
        public static readonly List<Compte> Liste = new List<Compte>();
        public static Compte Actuel;
        private static uint saltCounter;

        private static string FilePath()
        {
            string root = InstallerWindow.FindRoot();
            return root == null ? null : root + "\\Systeme\\comptes.cfg";
        }

        public static string Load()
        {
            Liste.Clear();
            string p = FilePath();
            if (p == null || !FileSystemOS.Utils.FsTools.Exists(p)) return "aucun compte";
            try
            {
                var lines = TextCodec.ReadLines(p);
                for (int i = 0; i < lines.Count; i++)
                {
                    string l = lines[i].Trim();
                    if (l.Length == 0 || l[0] == '#') continue;
                    string[] f = l.Split(':');
                    if (f.Length < 4) continue;
                    var c = new Compte();
                    c.Nom = f[0]; c.Sel = f[1]; c.Hash = f[2]; c.Admin = f[3] == "admin";
                    Liste.Add(c);
                }
            }
            catch (Exception e) { return "erreur : " + e.Message; }
            return Liste.Count + " compte(s)";
        }

        private static void Save()
        {
            string p = FilePath();
            if (p == null) return;
            var lines = new List<string>();
            lines.Add("# Comptes de File System OS - ne pas modifier a la main");
            for (int i = 0; i < Liste.Count; i++)
            {
                Compte c = Liste[i];
                lines.Add(c.Nom + ":" + c.Sel + ":" + c.Hash + ":" + (c.Admin ? "admin" : "utilisateur"));
            }
            FileSystemOS.Utils.FsTools.WriteBytes(p, TextCodec.Encode(lines));
        }

        public static Compte Find(string nom)
        {
            for (int i = 0; i < Liste.Count; i++) if (Liste[i].Nom.ToLower() == nom.ToLower()) return Liste[i];
            return null;
        }

        private static string NewSalt()
        {
            saltCounter++;
            string seed = Cosmos.HAL.RTC.Second + "-" + Cosmos.HAL.RTC.Minute + "-" + Cosmos.HAL.RTC.Hour + "-" +
                          Services.Scheduler.TotalTicks + "-" + Services.Frame + "-" + saltCounter;
            return Sha256.HashHex(seed).Substring(0, 16);
        }

        private static bool ValidName(string n)
        {
            if (n.Length < 1 || n.Length > 16) return false;
            for (int i = 0; i < n.Length; i++)
            {
                char c = n[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-';
                if (!ok) return false;
            }
            return true;
        }

        public static string Create(string nom, string mdp, bool admin)
        {
            if (!ValidName(nom)) return "Nom invalide (1 a 16 lettres, chiffres, _ ou -)";
            if (mdp.Length < 4) return "Mot de passe trop court (4 caracteres minimum)";
            if (Find(nom) != null) return "Ce compte existe deja";
            var c = new Compte();
            FileSystemOS.Noyau.Suivi.Etape("compte : sel (horloge)");
            c.Nom = nom; c.Admin = admin; c.Sel = NewSalt();
            FileSystemOS.Noyau.Suivi.Etape("compte : empreinte SHA-256 du mot de passe");
            c.Hash = Sha256.HashHex(c.Sel + mdp);
            Liste.Add(c);
            try
            {
                FileSystemOS.Noyau.Suivi.Etape("compte : ecriture de comptes.cfg");
                Save();
                FileSystemOS.Noyau.Suivi.Etape("compte : recherche du dossier systeme");
                string root = InstallerWindow.FindRoot();
                if (root != null)
                {
                    string home = root + "\\Utilisateurs\\" + nom;
                    FileSystemOS.Noyau.Suivi.Etape("compte : dossier Utilisateurs");
                    FileSystemOS.Utils.FsTools.MakeDir(root + "\\Utilisateurs");
                    FileSystemOS.Noyau.Suivi.Etape("compte : dossier personnel " + home);
                    FileSystemOS.Utils.FsTools.MakeDir(home);
                }
            }
            catch (Exception e) { return "Compte cree mais non enregistre : " + e.Message; }
            return null; // succes
        }

        public static Compte Check(string nom, string mdp)
        {
            Compte c = Find(nom);
            if (c == null) return null;
            return Sha256.HashHex(c.Sel + mdp) == c.Hash ? c : null;
        }

        public static string ChangePassword(Compte c, string ancien, string nouveau)
        {
            if (Sha256.HashHex(c.Sel + ancien) != c.Hash) return "Ancien mot de passe incorrect";
            if (nouveau.Length < 4) return "Mot de passe trop court (4 caracteres minimum)";
            c.Sel = NewSalt();
            c.Hash = Sha256.HashHex(c.Sel + nouveau);
            Save();
            return null;
        }

        public static string Delete(string nom)
        {
            Compte c = Find(nom);
            if (c == null) return "Compte introuvable";
            if (c == Actuel) return "Impossible de supprimer le compte connecte";
            int admins = 0;
            for (int i = 0; i < Liste.Count; i++) if (Liste[i].Admin) admins++;
            if (c.Admin && admins <= 1) return "Il faut garder au moins un administrateur";
            Liste.Remove(c);
            Save();
            return null;
        }

        /// <summary>Dossier personnel de l'utilisateur connecte.</summary>
        public static string Home()
        {
            string root = InstallerWindow.FindRoot();
            if (root == null || Actuel == null) return null;
            return root + "\\Utilisateurs\\" + Actuel.Nom;
        }
    }
}
