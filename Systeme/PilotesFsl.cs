using System;
using System.Collections.Generic;
using FileSystemOS.Fsl;
using FileSystemOS.Utils;

namespace FileSystemOS.Systeme
{
    /// <summary>
    /// PILOTES ECRITS EN FSL. Chaque pilote est decrit par un fichier .config place dans
    /// \FSOS\Systeme\Pilotes :
    ///
    ///     # Pilote du haut-parleur interne
    ///     nom = HautParleur
    ///     description = Fait sonner le haut-parleur du PC
    ///     programme = HautParleur.fsl
    ///     ports = 0x42, 0x43, 0x61
    ///     demarrage = oui
    ///
    /// Le programme FSL tourne dans son propre processus "pilote-nom". Il est le seul a pouvoir
    /// utiliser port_lire / port_ecrire, et seulement sur les ports listes dans son .config :
    /// un pilote du haut-parleur ne peut pas toucher au disque. S'il plante, il est mis en
    /// attente comme toute application, sans rien abimer d'autre.
    /// </summary>
    public static class PilotesFsl
    {
        public class Pilote
        {
            public string Nom = "", Description = "", Programme = "", Config = "";
            public readonly List<int> Ports = new List<int>();
            public bool Demarrage;
            public int Pid = -1;
            public string Etat = "arrete";
        }

        public static readonly List<Pilote> Liste = new List<Pilote>();
        private static bool demarres;

        public static string Dossier()
        {
            string vol = Live.Volume ?? "0:\\";
            return TextCodec.PathJoin(TextCodec.PathJoin(TextCodec.PathJoin(vol, "FSOS"), "Systeme"), "Pilotes");
        }

        /// <summary>Relit tous les fichiers .config du dossier des pilotes.</summary>
        public static string Charger()
        {
            // Garde les pilotes en marche (meme nom) : on ne perd pas leur PID
            var avant = new List<Pilote>(Liste);
            Liste.Clear();
            string dossier = Dossier();
            if (!FsTools.Exists(dossier)) return "aucun dossier de pilotes (" + dossier + ")";
            var fichiers = new List<string>();
            try
            {
                var l = Cosmos.System.FileSystem.VFS.VFSManager.GetDirectoryListing(dossier);
                for (int i = 0; i < l.Count; i++)
                    if (l[i].mEntryType == Cosmos.System.FileSystem.Listing.DirectoryEntryTypeEnum.File) fichiers.Add(l[i].mFullPath);
            }
            catch (Exception e) { return "lecture du dossier des pilotes impossible : " + e.Message; }
            int n = 0;
            for (int i = 0; i < fichiers.Count; i++)
            {
                string f = fichiers[i];
                if (!f.ToLower().EndsWith(".config")) continue;
                string chemin = f.IndexOf(':') >= 0 ? f : TextCodec.PathJoin(dossier, f);
                Pilote p = Lire(chemin);
                if (p == null) continue;
                for (int k = 0; k < avant.Count; k++)
                    if (avant[k].Nom == p.Nom) { p.Pid = avant[k].Pid; p.Etat = avant[k].Etat; }
                Liste.Add(p);
                n++;
            }
            return n + " pilote(s) trouve(s)";
        }

        /// <summary>Lit un fichier .config : lignes "cle = valeur", # pour les commentaires.</summary>
        public static Pilote Lire(string chemin)
        {
            List<string> lignes;
            try { lignes = TextCodec.ReadLines(chemin); }
            catch { return null; }
            var p = new Pilote { Config = chemin, Nom = SansExtension(TextCodec.FileName(chemin)) };
            for (int i = 0; i < lignes.Count; i++)
            {
                string l = lignes[i].Trim();
                if (l.Length == 0 || l[0] == '#' || l.StartsWith("//")) continue;
                int eg = l.IndexOf('=');
                if (eg <= 0) continue;
                string cle = l.Substring(0, eg).Trim().ToLower(), val = l.Substring(eg + 1).Trim();
                if (cle == "nom") p.Nom = val;
                else if (cle == "description") p.Description = val;
                else if (cle == "programme") p.Programme = val;
                else if (cle == "demarrage") p.Demarrage = val.ToLower() == "oui" || val.ToLower() == "vrai";
                else if (cle == "ports")
                {
                    string[] m = val.Split(',');
                    for (int k = 0; k < m.Length; k++)
                    {
                        int v = Nombre(m[k].Trim());
                        if (v >= 0 && v <= 0xFFFF) p.Ports.Add(v);
                    }
                }
            }
            if (p.Programme.Length == 0) p.Programme = p.Nom + ".fsl";
            return p;
        }

        private static string SansExtension(string n)
        {
            int d = n.LastIndexOf('.');
            return d > 0 ? n.Substring(0, d) : n;
        }

        /// <summary>"0x42" ou "66" ; -1 si ce n'est pas un nombre.</summary>
        private static int Nombre(string s)
        {
            if (s.Length == 0) return -1;
            int v = 0;
            if (s.StartsWith("0x") || s.StartsWith("0X"))
            {
                if (s.Length == 2) return -1;
                for (int i = 2; i < s.Length; i++)
                {
                    char c = s[i];
                    int d = c >= '0' && c <= '9' ? c - '0' : (c >= 'a' && c <= 'f' ? c - 'a' + 10 : (c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1));
                    if (d < 0 || v > 0xFFFF) return -1;
                    v = v * 16 + d;
                }
                return v;
            }
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] < '0' || s[i] > '9' || v > 0xFFFF) return -1;
                v = v * 10 + (s[i] - '0');
            }
            return v;
        }

        public static Pilote Trouver(string nom)
        {
            for (int i = 0; i < Liste.Count; i++) if (Liste[i].Nom.ToLower() == nom.ToLower()) return Liste[i];
            return null;
        }

        private static bool EnMarche(Pilote p)
        {
            if (p.Pid < 0) return false;
            var l = Services.Scheduler.Processes;
            for (int i = 0; i < l.Count; i++) if (l[i].Pid == p.Pid && l[i].Alive) return true;
            return false;
        }

        public static string Demarrer(Pilote p)
        {
            if (EnMarche(p)) return "le pilote " + p.Nom + " est deja en marche (PID " + p.Pid + ")";
            string prog = p.Programme.IndexOf(':') >= 0 ? p.Programme : TextCodec.PathJoin(Dossier(), p.Programme);
            if (!FsTools.Exists(prog)) { p.Etat = "programme absent"; return "programme introuvable : " + prog; }
            FslProgram code;
            try { code = prog.ToLower().EndsWith(".fsc") ? FslProgram.Load(prog) : Launcher.CompileFile(prog); }
            catch (Exception e) { p.Etat = "erreur de compilation"; return p.Nom + " : " + e.Message; }
            if (!Launcher.Lancer(code, TextCodec.FileName(prog), "pilote-" + p.Nom.ToLower(), p.Ports, p.Nom, out string m))
            {
                p.Etat = "refuse";
                return p.Nom + " : " + m;
            }
            p.Pid = Launcher.Pid;
            p.Etat = "en marche";
            Journal.Ajouter("pilote " + p.Nom + " demarre (ports : " + Ports(p) + ")", "systeme");
            return "pilote " + p.Nom + " demarre (PID " + p.Pid + ", ports " + Ports(p) + ")";
        }

        public static string Arreter(Pilote p)
        {
            if (!EnMarche(p)) { p.Etat = "arrete"; return "le pilote " + p.Nom + " n'est pas en marche"; }
            Services.Scheduler.Kill(p.Pid, out string m);
            p.Etat = "arrete";
            return "pilote " + p.Nom + " arrete (" + m + ")";
        }

        /// <summary>Au premier bureau : demarre les pilotes marques "demarrage = oui".</summary>
        public static void DemarrerAuLancement()
        {
            if (demarres) return;
            demarres = true;
            try
            {
                Charger();
                for (int i = 0; i < Liste.Count; i++)
                    if (Liste[i].Demarrage) Journal.Ajouter(Demarrer(Liste[i]), "systeme");
            }
            catch (Exception e) { FileSystemOS.Noyau.Diagnostic.Report("pilotes", e.Message); }
        }

        public static string Ports(Pilote p)
        {
            if (p.Ports.Count == 0) return "aucun";
            string r = "";
            for (int i = 0; i < p.Ports.Count; i++) r += (i > 0 ? ", " : "") + "0x" + Hex(p.Ports[i]);
            return r;
        }

        private static string Hex(int v)
        {
            const string ch = "0123456789ABCDEF";
            string r = "";
            do { r = ch[v & 15] + r; v >>= 4; } while (v > 0);
            return r.Length < 2 ? "0" + r : r;
        }

        public static string EtatDe(Pilote p) => EnMarche(p) ? "en marche (PID " + p.Pid + ")" : (p.Etat == "en marche" ? "termine" : p.Etat);

        // ---- Creation de pilotes : modele et exemples ----

        public static string Nouveau(string nom)
        {
            string d = Dossier();
            FsTools.MakeDir(d);
            string cfg = TextCodec.PathJoin(d, nom + ".config"), prog = TextCodec.PathJoin(d, nom + ".fsl");
            if (FsTools.Exists(cfg)) return "le pilote " + nom + " existe deja";
            Ecrire(cfg,
                "# Pilote " + nom + "\n" +
                "nom = " + nom + "\n" +
                "description = A decrire\n" +
                "programme = " + nom + ".fsl\n" +
                "# Ports materiels que ce pilote a le droit d'utiliser (separes par des virgules)\n" +
                "ports = \n" +
                "# oui : demarre automatiquement avec le bureau\n" +
                "demarrage = non\n");
            Ecrire(prog,
                "// Pilote " + nom + " : il tourne en arriere-plan dans son propre processus.\n" +
                "// Fonctions reservees aux pilotes : port_lire(port), port_ecrire(port, valeur),\n" +
                "// et leurs versions 16 et 32 bits. Seuls les ports du fichier " + nom + ".config sont permis.\n" +
                "journal(\"pilote " + nom + " pret\");\n" +
                "tantque (vrai) {\n" +
                "    // ... surveiller ou piloter le materiel ici ...\n" +
                "    attendre(1000);\n" +
                "}\n");
            return "pilote cree : " + cfg + " et " + prog;
        }

        public static string Exemples()
        {
            string d = Dossier();
            FsTools.MakeDir(d);
            Ecrire(TextCodec.PathJoin(d, "HautParleur.config"), ConfigHautParleur);
            Ecrire(TextCodec.PathJoin(d, "HautParleur.fsl"), ProgHautParleur);
            Ecrire(TextCodec.PathJoin(d, "HorlogeCmos.config"), ConfigHorloge);
            Ecrire(TextCodec.PathJoin(d, "HorlogeCmos.fsl"), ProgHorloge);
            Charger();
            return "exemples installes dans " + d + " : HautParleur et HorlogeCmos";
        }

        private static void Ecrire(string chemin, string texte)
        {
            byte[] o = new byte[texte.Length];
            for (int i = 0; i < texte.Length; i++) o[i] = texte[i] < 256 ? (byte)texte[i] : (byte)'?';
            FsTools.WriteBytes(chemin, o);
        }

        public const string ConfigHautParleur =
            "# Haut-parleur interne du PC (puce PIT, canal 2)\n" +
            "nom = HautParleur\n" +
            "description = Joue trois notes au demarrage du bureau\n" +
            "programme = HautParleur.fsl\n" +
            "ports = 0x42, 0x43, 0x61\n" +
            "demarrage = non\n";

        public const string ProgHautParleur =
            "// Pilote du haut-parleur interne : la puce PIT (port 0x43 et 0x42) produit la frequence,\n" +
            "// le port 0x61 branche le haut-parleur. Sur certains portables, le son passe par la carte son\n" +
            "// et ce haut-parleur reste muet : le pilote fonctionne quand meme, sans bruit.\n" +
            "fonction rien bip(entier frequence, entier duree) {\n" +
            "    entier diviseur = 1193180 / frequence;\n" +
            "    port_ecrire(0x43, 0xB6);\n" +
            "    port_ecrire(0x42, diviseur % 256);\n" +
            "    port_ecrire(0x42, diviseur / 256);\n" +
            "    entier etat = port_lire(0x61);\n" +
            "    port_ecrire(0x61, etat - etat % 4 + 3);\n" +
            "    attendre(duree);\n" +
            "    port_ecrire(0x61, etat - etat % 4);\n" +
            "}\n" +
            "\n" +
            "journal(\"haut-parleur : trois notes\");\n" +
            "bip(523, 150);\n" +
            "bip(659, 150);\n" +
            "bip(784, 250);\n";

        public const string ConfigHorloge =
            "# Horloge materielle du PC (puce CMOS / RTC)\n" +
            "nom = HorlogeCmos\n" +
            "description = Lit l'heure directement dans la puce CMOS et l'affiche dans une fenetre\n" +
            "programme = HorlogeCmos.fsl\n" +
            "ports = 0x70, 0x71\n" +
            "demarrage = non\n";

        public const string ProgHorloge =
            "// Pilote de l'horloge CMOS : on choisit un registre sur le port 0x70, on le lit sur 0x71.\n" +
            "// Les valeurs sont en BCD (0x59 veut dire 59) : la fonction bcd les convertit.\n" +
            "fonction entier bcd(entier v) {\n" +
            "    retourner (v / 16) * 10 + v % 16;\n" +
            "}\n" +
            "\n" +
            "fonction entier registre(entier numero) {\n" +
            "    port_ecrire(0x70, numero);\n" +
            "    retourner bcd(port_lire(0x71));\n" +
            "}\n" +
            "\n" +
            "fonction texte deux(entier n) {\n" +
            "    si (n < 10) { retourner \"0\" + vers_texte(n); }\n" +
            "    retourner vers_texte(n);\n" +
            "}\n" +
            "\n" +
            "entier f = fenetre(\"Horloge CMOS\", 260, 110);\n" +
            "entier heure = etiquette(f, \"--:--:--\", 20, 20);\n" +
            "entier info = etiquette(f, \"lue dans la puce, sans Cosmos\", 20, 50);\n" +
            "booleen continuer = vrai;\n" +
            "tantque (continuer) {\n" +
            "    ecrire(heure, deux(registre(4)) + \":\" + deux(registre(2)) + \":\" + deux(registre(0)));\n" +
            "    texte e = evenement();\n" +
            "    si (e == \"fermeture:\" + vers_texte(f)) { continuer = faux; }\n" +
            "    attendre(500);\n" +
            "}\n";
    }
}
