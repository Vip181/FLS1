using System;
using System.Collections.Generic;
using System.IO;
using FileSystemOS.Graphique;
using FileSystemOS.Utils;

namespace FileSystemOS.Systeme
{
    /// <summary>
    /// Fichiers INTERNES de l'OS (\FSOS\Systeme\*.cfg), lus pendant le chargement.
    /// Format : cle=valeur, lignes commencant par # = commentaires.
    /// S'ils manquent, ils sont recrees avec les valeurs par defaut.
    /// </summary>
    public static class Config
    {
        public static readonly string[] Files =
            { "demarrage.cfg", "interface.cfg", "console.cfg", "reseau.cfg", "horloge.cfg" };

        private static readonly List<string> keys = new List<string>();
        private static readonly List<string> values = new List<string>();
        private static readonly List<string> sources = new List<string>();

        private const string Header = "# File System OS - fichier interne charge au demarrage\n" +
                                      "# (c) File System OS Company - Vincent Senut\n";

        public static string Default(string file)
        {
            if (file == "demarrage.cfg")
                return Header + "# Images affichees par etape de chargement (1 = le plus rapide)\nduree_etape=6\n" +
                       "# Resolution : auto (detectee pour cet ordinateur) ou par exemple 1280x720\nresolution=auto\n" +
                       "# Memoire paginee : nombre de pages de 4 Ko (512 = 2 Mo)\npages_memoire=512\n" +
                       "# Connexion automatique : oui = directement au bureau avec le dernier compte utilise\n" +
                       "connexion_auto=oui\n";
            if (file == "interface.cfg")
                return Header + "# Interface graphique et cogestion des threads\n" +
                       "attente_auto=oui\nfenetres_actives_max=4\nrendus_par_tour=2\n" +
                       "# Fond d'ecran : chemin d'une image .bmp (vide = degrade)\nfond_ecran=\n";
            if (file == "console.cfg")
                return Header + "# Console et commandes\nmessage_accueil=Tapez 'aide' pour la liste des commandes.\n";
            if (file == "reseau.cfg")
                return Header + "# Reseau : DHCP (adresse donnee par la box)\ndhcp=oui\n" +
                       "# Pilotes reseau : auto (puces connues seulement) | tous | aucun\npilotes_reseau=auto\n" +
                       "# Pilote EXPERIMENTAL Intel I217/I218/I219 des portables (oui / non)\npilote_i219=non\n";
            if (file == "horloge.cfg")
                return Header + "# Decalage en heures par rapport a l'horloge materielle\n" +
                       "# (ex : 2 en France l'ete si la machine virtuelle est en UTC)\ndecalage_heures=0\n";
            return Header;
        }

        private static void Parse(string file, string text)
        {
            int start = 0;
            while (start <= text.Length)
            {
                int end = text.IndexOf('\n', start);
                if (end < 0) end = text.Length;
                string line = text.Substring(start, end - start).Trim();
                start = end + 1;

                if (line.Length == 0 || line[0] == '#') continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                Set(line.Substring(0, eq).Trim().ToLower(), line.Substring(eq + 1).Trim(), file);
                if (end >= text.Length) break;
            }
        }

        private static void Set(string key, string value, string source)
        {
            int i = FileSystemOS.Utils.Listes.Indice(keys, key);
            if (i >= 0) { values[i] = value; sources[i] = source; return; }
            keys.Add(key); values.Add(value); sources.Add(source);
        }

        /// <summary>Charge (ou cree) tous les fichiers internes. Renvoie un compte rendu.</summary>
        public static string LoadAll()
        {
            keys.Clear(); values.Clear(); sources.Clear();
            string root = InstallerWindow.FindRoot();
            string sys = root == null ? null : root + "\\Systeme";
            int read = 0, created = 0;

            for (int i = 0; i < Files.Length; i++)
            {
                string text = null;
                if (sys != null)
                {
                    string p = sys + "\\" + Files[i];
                    FileSystemOS.Noyau.Suivi.Etape("reglages : " + Files[i]);
                    try
                    {
                        if (FileSystemOS.Utils.FsTools.Exists(p)) { text = TextCodec.Join(TextCodec.ReadLines(p)); read++; }
                        else { FileSystemOS.Utils.FsTools.WriteText(p, Default(Files[i])); created++; }
                    }
                    catch (Exception) { }
                }
                Parse(Files[i], text ?? Default(Files[i]));
            }

            if (sys == null) return "valeurs par defaut (OS pas encore installe)";
            return read + " lu(s), " + created + " cree(s) dans " + sys;
        }

        /// <summary>Change un reglage et le reecrit dans son fichier interne.</summary>
        public static string SetAndSave(string file, string key, string value)
        {
            FileSystemOS.Noyau.Suivi.Etape("reglage " + key + " : en memoire");
            Set(key, value, file);
            FileSystemOS.Noyau.Suivi.Etape("reglage " + key + " : recherche du dossier systeme");
            string root = InstallerWindow.FindRoot();
            if (root == null) return "reglage applique (non enregistre : OS pas installe)";
            string p = root + "\\Systeme\\" + file;
            try
            {
                FileSystemOS.Noyau.Suivi.Etape("reglage " + key + " : lecture de " + file);
                List<string> lines = FileSystemOS.Utils.FsTools.Exists(p) ? TextCodec.ReadLines(p) : new List<string>();
                bool found = false;
                for (int i = 0; i < lines.Count; i++)
                {
                    string l = lines[i].Trim();
                    if (l.StartsWith(key + "=") || l.StartsWith(key + " ="))
                    {
                        lines[i] = key + "=" + value;
                        found = true;
                    }
                }
                if (!found)
                {
                    if (lines.Count > 0 && lines[lines.Count - 1].Length == 0) lines.RemoveAt(lines.Count - 1);
                    lines.Add(key + "=" + value);
                }
                FileSystemOS.Noyau.Suivi.Etape("reglage " + key + " : ecriture de " + file);
                FileSystemOS.Utils.FsTools.WriteBytes(p, TextCodec.Encode(lines));
                return "enregistre dans " + file;
            }
            catch (Exception e) { return "erreur : " + e.Message; }
        }

        public static string Get(string key, string def)
        {
            int i = FileSystemOS.Utils.Listes.Indice(keys, key);
            return i >= 0 ? values[i] : def;
        }

        public static int GetInt(string key, int def)
        {
            string v = Get(key, null);
            if (v == null) return def;
            bool neg = v.StartsWith("-");
            int n = Fmt.ParseInt(neg ? v.Substring(1) : v);
            return n < 0 ? def : (neg ? -n : n);
        }

        public static bool GetBool(string key, bool def)
        {
            string v = Get(key, null);
            if (v == null) return def;
            v = v.ToLower();
            return v == "oui" || v == "1" || v == "vrai" || v == "true";
        }

        public static List<string> Dump()
        {
            var r = new List<string>();
            for (int i = 0; i < keys.Count; i++) r.Add(Fmt.Pad(sources[i], 15) + keys[i] + " = " + values[i]);
            return r;
        }
    }
}
