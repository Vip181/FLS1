using System;
using System.Collections.Generic;
using System.IO;
using Cosmos.HAL.BlockDevice;
using Cosmos.System.FileSystem.Listing;
using Cosmos.System.FileSystem.VFS;
using FileSystemOS.Pilotes;
using FileSystemOS.Processus;
using FileSystemOS.Utils;

namespace FileSystemOS.Systeme
{
    /// <summary>
    /// SESSION SUR LE SSD, SANS LE PILOTE DE FICHIERS DE COSMOS.
    ///
    /// File System OS travaille dans son disque memoire (mode Live). Au demarrage, si File
    /// System OS est installe sur le SSD (un dossier FSOS sur une partition FAT32), tout ce
    /// dossier est CHARGE en memoire avec le pilote FAT32 de File System OS (FatDirect) :
    /// reglages, comptes, documents, images et programmes.
    ///
    /// A chaque sauvegarde (fichier modifie, compte cree, deconnexion, arret, "Sauvegarder la
    /// session"), seuls les fichiers qui ont CHANGE sont recopies sur le SSD (une empreinte de
    /// chaque fichier est gardee), et ceux qui ont ete supprimes sont effaces du SSD.
    ///
    /// Le pilote de Cosmos ne touche plus jamais au SSD : sur le Dell, il ecrivait des noms
    /// longs invalides (JOURNA~1), et le demarrage en AHCI restait ensuite noir.
    /// </summary>
    public static class SessionSsd
    {
        public static FatDirect Volume;            // la partition ou File System OS est installe
        public static string Etat = "aucune installation sur le SSD";
        public static bool Pret;                   // session chargee : les sauvegardes vont sur le SSD
        public static string VersionNoyauSsd;      // version du noyau installe sur le SSD (installer-ssd.ps1), ou null

        private const string Racine = "FSOS";
        private static readonly List<string> chemins = new List<string>();   // fichiers tels que sur le SSD
        private static readonly List<uint> empreintes = new List<uint>();
        private static bool enCours;

        // ================================================================
        //  Trouver l'installation
        // ================================================================

        /// <summary>La partition FAT32 d'un SSD qui contient un dossier FSOS (celle nommee FSOS d'abord).</summary>
        public static FatDirect Trouver()
        {
            FatDirect secours = null;
            int total = Ahci.Disques.Count + Nvme.Disques.Count;
            for (int k = 0; k < total; k++)
            {
                BlockDevice d;
                string nom;
                if (k < Ahci.Disques.Count) { d = Ahci.Disques[k]; nom = "sata" + k; }
                else { d = Nvme.Disques[k - Ahci.Disques.Count]; nom = "nvme" + (k - Ahci.Disques.Count); }

                List<PartitionInfo> parts;
                try { parts = Gpt.Lire(d, out bool gpt); }
                catch (Exception) { continue; }
                for (int i = 0; i < parts.Count; i++)
                {
                    if (parts[i].Type.StartsWith("EFI")) continue;          // demarrage de Windows : jamais
                    FatDirect v = null;
                    try { v = FatDirect.Monter(d, parts[i].Debut, nom, out string e); } catch (Exception) { }
                    if (v == null) continue;
                    bool installe = false;
                    try { installe = v.Existe(Racine); } catch (Exception) { }
                    if (!installe) continue;
                    if (v.EstFsos) return v;
                    if (secours == null) secours = v;
                }
            }
            return secours;
        }

        // ================================================================
        //  Chargement (au demarrage, ou juste apres l'installation)
        // ================================================================

        public static string Charger()
        {
            Pret = false;
            FileSystemOS.Noyau.Suivi.Etape("session : recherche de l'installation sur le SSD");
            Volume = Trouver();
            VersionNoyauSsd = Volume != null ? LireVersionNoyau(Volume) : null;
            if (Volume == null) { Etat = "aucune installation sur le SSD"; return Etat; }
            if (!Live.Active || Live.Volume == null)
            {
                Etat = "disque memoire indisponible : session du SSD non chargee (" + Live.Status + ")";
                return Etat;
            }

            chemins.Clear();
            empreintes.Clear();
            int n;
            try
            {
                Volume.Oublier();
                n = VersMemoire(Racine, TextCodec.PathJoin(Live.Volume, Racine), "");
            }
            catch (Exception e)
            {
                Etat = "session du SSD : erreur de chargement (" + e.Message + ")";
                return Etat;
            }
            Volume.Ecriture = true;
            Pret = true;
            Etat = "session chargee depuis le SSD (" + Volume.NomDisque + ", " + n + " fichier(s))" +
                   (VersionNoyauSsd != null ? ", noyau du SSD : " + VersionNoyauSsd : "");
            return Etat;
        }

        // ================================================================
        //  Enregistrement AU FIL DE L'EAU (thread "synchro")
        //
        //  Avant : a chaque action (deplacer une icone, ecrire un fichier...), TOUTE la session
        //  etait relue et comparee, puis les fichiers changes reecrits sur le SSD, le tout dans
        //  la meme action : l'interface et la souris se figeaient. Maintenant, FsTools NOTE chaque
        //  fichier ecrit, dossier cree ou element supprime ; le thread "synchro" les recopie un
        //  par un, 1,5 s apres la derniere modification. Rien n'est relu inutilement.
        // ================================================================
        public const byte Fichier = 1, Dossier = 2, Suppression = 3;
        private static readonly List<string> aFaire = new List<string>();
        private static readonly List<byte> genres = new List<byte>();
        private static uint derniereModif;

        public static int EnAttente => aFaire.Count;

        /// <summary>Appele par FsTools apres chaque ecriture dans le disque memoire (chemin depuis sa racine).</summary>
        public static void Noter(string rel, byte genre)
        {
            if (!Pret || rel == null) return;
            if (rel != Racine && !rel.StartsWith(Racine + "\\")) return;
            // Deja note : on le deplace a la fin (l'ordre des operations est ainsi respecte)
            int i = Listes.Indice(aFaire, rel);
            if (i >= 0) { aFaire.RemoveAt(i); genres.RemoveAt(i); }
            aFaire.Add(rel);
            genres.Add(genre);
            derniereModif = Horloge.Millis;
        }

        /// <summary>Un fichier a la fois, quand les modifications se sont calmees.</summary>
        public static void Pas()
        {
            if (!Pret || Volume == null || aFaire.Count == 0) return;
            if (FileSystemOS.Noyau.Watchdog.EnAlerte) return;
            if (Horloge.Active && Horloge.Millis - derniereModif < 1500) return;
            Suivant();
        }

        /// <summary>Tout ce qui reste, tout de suite (arret, redemarrage, "Sauvegarder la session").</summary>
        public static string ToutEnregistrer()
        {
            if (!Pret || Volume == null) return "pas de session sur le SSD";
            FileSystemOS.Noyau.Watchdog.Lent = true;
            int n = 0, erreurs = 0;
            while (aFaire.Count > 0)
            {
                if (!Suivant()) erreurs++;
                n++;
            }
            FileSystemOS.Noyau.Watchdog.Lent = false;
            Etat = "session enregistree sur le SSD (" + n + " operation(s)" + (erreurs > 0 ? ", " + erreurs + " erreur(s)" : "") + ")";
            return Etat;
        }

        private static bool Suivant()
        {
            string rel = aFaire[0];
            byte g = genres[0];
            aFaire.RemoveAt(0);
            genres.RemoveAt(0);
            string avant = FileSystemOS.Noyau.Watchdog.Etape;
            FileSystemOS.Noyau.Watchdog.Etape = "SSD : " + rel;
            bool ok = true;
            try { Appliquer(rel, g); }
            catch (Exception e) { Etat = "SSD : " + rel + " non enregistre (" + e.Message + ")"; ok = false; }
            FileSystemOS.Noyau.Watchdog.Etape = avant;
            return ok;
        }

        private static void Appliquer(string rel, byte g)
        {
            Volume.Oublier();
            if (g == Suppression)
            {
                if (Volume.Existe(rel)) Volume.Supprimer(rel);
                return;
            }
            if (g == Dossier) { CreerChemin(rel); return; }

            // Fichier : on relit son contenu ACTUEL en memoire (il a pu changer depuis la note)
            var ram = Live.Direct;
            if (ram == null) return;
            ram.Oublier();
            if (!ram.Existe(rel))
            {
                if (Volume.Existe(rel)) Volume.Supprimer(rel);
                return;
            }
            byte[] data = ram.Lire(rel);
            int k = -1;
            for (int i = 0; i < rel.Length; i++) if (rel[i] == '\\') k = i;
            if (k > 0) CreerChemin(rel.Substring(0, k));
            Volume.Ecrire(rel, data);
        }

        private static void CreerChemin(string rel)
        {
            string[] m = rel.Split('\\');
            string c = "";
            for (int i = 0; i < m.Length; i++)
            {
                if (m[i].Length == 0) continue;
                c = c.Length == 0 ? m[i] : c + "\\" + m[i];
                Volume.CreerDossier(c);          // ne fait rien si le dossier existe deja
            }
        }

        /// <summary>Version ecrite par installer-ssd.ps1 dans \boot\noyau-version.txt (null si absent).</summary>
        private static string LireVersionNoyau(FatDirect v)
        {
            try
            {
                if (!v.Existe("boot\\noyau-version.txt")) return null;
                byte[] b = v.Lire("boot\\noyau-version.txt");
                string s = "";
                for (int i = 0; i < b.Length; i++) if (b[i] >= 32 && b[i] < 127) s += (char)b[i];
                s = s.Trim();
                return s.Length > 0 && s != "inconnue" ? s : null;
            }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// Juste apres l'installation : la session en memoire vient d'etre copiee sur cette
        /// partition. On s'y rattache sans rien recopier ; le premier enregistrement ecrira
        /// une fois chaque fichier, puis seulement ce qui change.
        /// </summary>
        public static void Adopter(BlockDevice d, ulong debut, string nomDisque)
        {
            FileSystemOS.Noyau.Suivi.Etape("session : montage de la partition");
            var v = FatDirect.Monter(d, debut, nomDisque, out string erreur);
            if (v == null) { Etat = "session du SSD : montage impossible (" + erreur + ")"; return; }
            v.Ecriture = true;
            Volume = v;
            chemins.Clear();
            empreintes.Clear();
            VersionNoyauSsd = LireVersionNoyau(v);
            Pret = Live.Active && Live.Volume != null;
            Etat = Pret ? "session rattachee au SSD (" + nomDisque + ")" : "disque memoire indisponible";
        }

        private static int VersMemoire(string source, string dest, string relatif)
        {
            FsTools.MakeDir(dest);
            int n = 0;
            var liste = Volume.Lister(source);
            for (int i = 0; i < liste.Count; i++)
            {
                EntreeFat e = liste[i];
                if (e.Nom.Length == 0 || e.Nom == "." || e.Nom == ".." || e.Volume) continue;
                string src = source + "\\" + e.Nom;
                string dst = TextCodec.PathJoin(dest, e.Nom);
                string rel = relatif + "\\" + e.Nom;
                if (e.Dossier) n += VersMemoire(src, dst, rel);
                else
                {
                    FileSystemOS.Noyau.Suivi.Etape("SSD -> memoire : lecture de " + rel);
                    byte[] data = Volume.Lire(src);
                    FileSystemOS.Noyau.Suivi.Etape("SSD -> memoire : ecriture de " + rel + " (" + data.Length + " octets)");
                    FsTools.WriteBytes(dst, data);
                    Retenir(rel, Empreinte(data));
                    n++;
                }
            }
            return n;
        }

        // ================================================================
        //  Enregistrement (seulement ce qui a change)
        // ================================================================

        public static string Sauver()
        {
            if (!Pret || Volume == null || Live.Volume == null) return "pas de session sur le SSD";
            if (FileSystemOS.Noyau.Watchdog.EnAlerte) return "systeme bloque : enregistrement annule";
            if (enCours) return "enregistrement deja en cours";

            enCours = true;
            string avant = FileSystemOS.Noyau.Watchdog.Etape;
            FileSystemOS.Noyau.Watchdog.Etape = "enregistrement de la session sur le SSD";
            FileSystemOS.Noyau.Watchdog.Lent = true;
            int ecrits = 0, effaces = 0;
            try
            {
                FileSystemOS.Noyau.Suivi.Etape("SSD : comparaison des fichiers de la session");
                Volume.Oublier();
                var vus = new List<string>();
                if (Live.Direct != null)
                {
                    // Disque memoire lu par le pilote de File System OS : aucun appel au pilote de Cosmos
                    Live.Direct.Oublier();
                    ecrits = VersSsdDirect(Live.Direct, Racine, Racine, "", vus);
                }
                else ecrits = VersSsd(TextCodec.PathJoin(Live.Volume, Racine), Racine, "", vus);

                // Fichiers supprimes dans File System OS : on les efface aussi du SSD
                for (int i = chemins.Count - 1; i >= 0; i--)
                {
                    if (Listes.Contient(vus, chemins[i])) continue;
                    if (EncoreEnMemoire(chemins[i])) continue;          // garde-fou : il existe toujours
                    FileSystemOS.Noyau.Suivi.Etape("SSD : effacement de " + chemins[i]);
                    try { Volume.Supprimer(Racine + chemins[i]); effaces++; } catch (Exception) { }
                    chemins.RemoveAt(i);
                    empreintes.RemoveAt(i);
                }
                Etat = "session enregistree sur le SSD (" + ecrits + " fichier(s) ecrit(s), " + effaces + " efface(s))";
            }
            catch (Exception e) { Etat = "session du SSD : erreur d'enregistrement (" + e.Message + ")"; }
            FileSystemOS.Noyau.Watchdog.Lent = false;
            FileSystemOS.Noyau.Watchdog.Etape = avant;
            enCours = false;
            return Etat;
        }

        private static int VersSsd(string source, string dest, string relatif, List<string> vus)
        {
            if (!Volume.Existe(dest)) Volume.CreerDossier(dest);
            int n = 0;
            var liste = VFSManager.GetDirectoryListing(source);
            for (int i = 0; i < liste.Count; i++)
            {
                string nom = liste[i].mName;
                string rel = relatif + "\\" + nom;
                string cible = dest + "\\" + nom;
                if (liste[i].mEntryType == DirectoryEntryTypeEnum.Directory)
                    n += VersSsd(liste[i].mFullPath, cible, rel, vus);
                else if (liste[i].mEntryType == DirectoryEntryTypeEnum.File)
                {
                    vus.Add(rel);
                    byte[] data = File.ReadAllBytes(liste[i].mFullPath);
                    uint emp = Empreinte(data);
                    int k = Listes.Indice(chemins, rel);
                    if (k >= 0 && empreintes[k] == emp) continue;          // inchange : rien a ecrire
                    Volume.Ecrire(cible, data);
                    Retenir(rel, emp);
                    n++;
                }
            }
            return n;
        }

        /// <summary>Meme chose que VersSsd, mais en lisant le disque memoire avec FatDirect.</summary>
        private static int VersSsdDirect(FatDirect ram, string source, string dest, string relatif, List<string> vus)
        {
            if (!Volume.Existe(dest)) Volume.CreerDossier(dest);
            int n = 0;
            var liste = ram.Lister(source);
            for (int i = 0; i < liste.Count; i++)
            {
                EntreeFat e = liste[i];
                if (e.Nom.Length == 0 || e.Volume) continue;
                string rel = relatif + "\\" + e.Nom;
                string cible = dest + "\\" + e.Nom;
                if (e.Dossier) { n += VersSsdDirect(ram, source + "\\" + e.Nom, cible, rel, vus); continue; }
                vus.Add(rel);
                byte[] data = ram.Lire(source + "\\" + e.Nom);
                uint emp = Empreinte(data);
                int k = Listes.Indice(chemins, rel);
                if (k >= 0 && empreintes[k] == emp) continue;              // inchange : rien a ecrire
                FileSystemOS.Noyau.Suivi.Etape("SSD : ecriture de " + rel + " (" + data.Length + " octets)");
                Volume.Ecrire(cible, data);
                Retenir(rel, emp);
                n++;
            }
            return n;
        }

        /// <summary>Le fichier existe-t-il encore dans le disque memoire ? (dans le doute : oui)</summary>
        private static bool EncoreEnMemoire(string rel)
        {
            try
            {
                if (Live.Direct != null) { Live.Direct.Oublier(); return Live.Direct.Existe(Racine + rel); }
                return File.Exists(Live.Volume + Racine + rel);
            }
            catch (Exception) { return true; }
        }

        private static void Retenir(string rel, uint emp)
        {
            int k = Listes.Indice(chemins, rel);
            if (k >= 0) empreintes[k] = emp;
            else { chemins.Add(rel); empreintes.Add(emp); }
        }

        /// <summary>Empreinte FNV-1a sur 32 bits : change des qu'un seul octet change.</summary>
        private static uint Empreinte(byte[] d)
        {
            uint h = 2166136261;
            for (int i = 0; i < d.Length; i++) { h ^= d[i]; h *= 16777619; }
            return h ^ (uint)d.Length;
        }
    }
}
