using System;
using System.Collections.Generic;
using System.IO;
using Cosmos.HAL.BlockDevice;
using Cosmos.System.FileSystem.Listing;
using Cosmos.System.FileSystem.VFS;
using FileSystemOS.Graphique;
using FileSystemOS.Utils;

namespace FileSystemOS.Systeme
{
    /// <summary>
    /// MODE LIVE : utiliser File System OS sans l'installer (depuis l'ISO ou la cle USB).
    ///  - Un disque en memoire (RamDisk) recoit l'environnement \FSOS complet.
    ///  - Les fichiers crees pendant la session y sont stockes.
    ///  - S'il existe un disque FAT32 (disque virtuel, 2e disque...), la session y est
    ///    SAUVEGARDEE dans \FSOS-Live a l'arret et RESTAUREE au demarrage suivant.
    /// </summary>
    public static class Live
    {
        public static RamDisk Disk;
        public static string Volume;      // ex : "1:\"
        public static bool Active;        // vrai si l'OS n'est installe sur aucun disque
        public static uint SizeMb;
        public static string Status = "inactif";
        public static string Format = "";

        /// <summary>
        /// Montage du disque memoire par le pilote FAT32 de File System OS. TOUTES les ecritures
        /// sur le disque memoire passent par lui (FsTools) : le pilote de Cosmos ecrit des noms
        /// longs invalides, il ne sert plus qu'a lire. Null en FAT16 (ancien comportement).
        /// </summary>
        public static FileSystemOS.Pilotes.FatDirect Direct;

        private static readonly string[] SavedDirs = { "Documents", "Images", "Applications", "Utilisateurs" };

        /// <summary>A appeler AVANT le montage des disques.</summary>
        public static void Create()
        {
            uint ram = 0;
            try { ram = Cosmos.Core.CPU.GetAmountOfRAM(); } catch (Exception) { }
            if (ram > 0 && ram < 256) { Status = "memoire insuffisante pour le mode Live (" + ram + " Mo)"; return; }
            // 2 Go ou plus : 288 Mo, soit un FAT32 a grappes de 4 Ko (le format le plus courant) et de
            // la place pour la session du SSD. Sinon, reservation reduite comme avant.
            SizeMb = ram >= 2048 ? 288u : (ram >= 1024 ? 48u : 24u);
            try
            {
                // FAT32 d'abord : sur le Dell, Cosmos ne creait pas le dossier \FSOS dans le FAT16
                try
                {
                    Disk = new RamDisk(SizeMb);
                    Direct = Disk.FormatFat32();
                    Format = "FAT32, ecritures par le pilote de File System OS";
                }
                catch (Exception e32)
                {
                    Direct = null;
                    Disk = new RamDisk(SizeMb);           // disque neuf (tout a zero) pour le FAT16
                    Disk.FormatFat16();
                    Format = "FAT16 (FAT32 impossible : " + e32.Message + ")";
                }
                BlockDevice.Devices.Add(Disk);
            }
            catch (Exception e) { Disk = null; Status = "disque memoire impossible : " + e.Message; }
        }

        private static void Locate()
        {
            Volume = null;
            if (!Services.FsReady || Disk == null) return;
            var vols = VFSManager.GetVolumes();
            for (int i = 0; i < vols.Count; i++)
            {
                try { if (File.Exists(TextCodec.PathJoin(vols[i].mFullPath, "LIVE.TAG"))) { Volume = vols[i].mFullPath; return; } }
                catch (Exception) { }
            }
            // Repli : le lecteur dont la taille correspond a celle du disque memoire
            for (int i = 0; i < vols.Count; i++)
            {
                try
                {
                    long size = Services.FileSystem.GetTotalSize(vols[i].mFullPath);
                    if (size > Disk.Bytes * 3 / 4 && size <= Disk.Bytes) { Volume = vols[i].mFullPath; return; }
                }
                catch (Exception) { }
            }
        }

        public static bool IsLiveVolume(string vol) => Volume != null && vol == Volume;

        /// <summary>Premier lecteur FAT qui n'est PAS le disque memoire (pour sauvegarder).</summary>
        public static string SaveTarget()
        {
            if (!Services.FsReady) return null;
            var vols = VFSManager.GetVolumes();
            for (int i = 0; i < vols.Count; i++)
                if (vols[i].mFullPath != Volume) return TextCodec.PathJoin(vols[i].mFullPath, "FSOS-Live");
            return null;
        }

        /// <summary>A appeler APRES le montage : decide du mode et prepare \FSOS en memoire.</summary>
        public static string Prepare()
        {
            Locate();
            if (Volume == null) { if (Disk != null) Status = "disque memoire non monte"; return Status; }

            string root = InstallerWindow.FindRoot();
            Active = root == null || root.StartsWith(Volume);
            if (!Active) { Status = "OS installe : disque memoire " + Volume + " disponible (temporaire)"; return Status; }

            string fsos = TextCodec.PathJoin(Volume, "FSOS");
            try { MakeDir(fsos); }
            catch (Exception e)
            {
                Status = "LIVE sur " + Volume + " (" + Format + ") : impossible de creer \\FSOS (" + e.Message + ")";
                return Status;
            }
            if (!Directory.Exists(fsos))
            {
                Status = "LIVE sur " + Volume + " (" + Format + ") : le dossier \\FSOS n'apparait pas apres sa creation";
                return Status;
            }
            MakeDir(fsos + "\\Systeme");
            MakeDir(fsos + "\\Applications");
            MakeDir(fsos + "\\Documents");
            MakeDir(fsos + "\\Images");
            MakeDir(fsos + "\\Utilisateurs");
            MakeDir(fsos + "\\Systeme\\Pilotes");
            Write(fsos + "\\Systeme\\Pilotes\\HautParleur.config", PilotesFsl.ConfigHautParleur);
            Write(fsos + "\\Systeme\\Pilotes\\HautParleur.fsl", PilotesFsl.ProgHautParleur);
            Write(fsos + "\\Systeme\\Pilotes\\HorlogeCmos.config", PilotesFsl.ConfigHorloge);
            Write(fsos + "\\Systeme\\Pilotes\\HorlogeCmos.fsl", PilotesFsl.ProgHorloge);
            Write(fsos + "\\Systeme\\version.txt", Samples.Version);
            Write(fsos + "\\Documents\\Bienvenue.txt", Samples.Welcome);
            Write(fsos + "\\Applications\\Bonjour.fsl", Samples.Hello);
            Write(fsos + "\\Applications\\Compteur.fsl", Samples.Counter);
            Write(fsos + "\\Applications\\Nul.fsl", Samples.Nul);
            Write(fsos + "\\Applications\\Fenetre.fsl", Samples.Fenetre);
            Write(fsos + "\\Applications\\Balle.fsl", Samples.Balle);
            Write(fsos + "\\Applications\\Calculs.fsl", Samples.Calculs);
            for (int i = 0; i < Config.Files.Length; i++)
                Write(fsos + "\\Systeme\\" + Config.Files[i], Config.Default(Config.Files[i]));

            int restored = Restore();
            string target = SaveTarget();
            Status = "LIVE sur " + Volume + " (" + SizeMb + " Mo, " + Format + ")" +
                     (target == null ? " - aucun disque pour sauvegarder"
                                     : " - sauvegarde dans " + target + (restored > 0 ? ", " + restored + " fichier(s) restaure(s)" : ""));
            return Status;
        }

        private static void MakeDir(string p) => FileSystemOS.Utils.FsTools.MakeDir(p);
        private static void Write(string p, string c) { if (!File.Exists(p)) FileSystemOS.Utils.FsTools.WriteText(p, c); }

        private static int CopyTree(string src, string dst)
        {
            if (!Directory.Exists(src)) return 0;
            MakeDir(dst);
            int n = 0;
            var list = VFSManager.GetDirectoryListing(src);
            for (int i = 0; i < list.Count; i++)
            {
                string to = TextCodec.PathJoin(dst, list[i].mName);
                if (list[i].mEntryType == DirectoryEntryTypeEnum.Directory) n += CopyTree(list[i].mFullPath, to);
                else if (list[i].mEntryType == DirectoryEntryTypeEnum.File)
                {
                    FileSystemOS.Utils.FsTools.WriteBytes(to, File.ReadAllBytes(list[i].mFullPath));
                    n++;
                }
            }
            return n;
        }

        /// <summary>Copie la session (documents, images, applications, comptes) sur le disque FAT.</summary>
        public static string Save()
        {
            if (!Active) return "pas en mode Live";
            if (SessionSsd.Pret) return SessionSsd.ToutEnregistrer();
            string target = SaveTarget();
            if (target == null) return "aucun disque FAT pour sauvegarder (fichiers perdus a l'arret)";
            try
            {
                string fsos = TextCodec.PathJoin(Volume, "FSOS");
                int n = 0;
                for (int i = 0; i < SavedDirs.Length; i++)
                    n += CopyTree(fsos + "\\" + SavedDirs[i], target + "\\" + SavedDirs[i]);
                string acc = fsos + "\\Systeme\\comptes.cfg";
                if (File.Exists(acc)) { MakeDir(target); FileSystemOS.Utils.FsTools.WriteBytes(target + "\\comptes.cfg", File.ReadAllBytes(acc)); n++; }
                return n + " fichier(s) sauvegarde(s) dans " + target;
            }
            catch (Exception e) { return "erreur : " + e.Message; }
        }

        /// <summary>Sauvegarde immediate si on est en mode Live avec un disque disponible.</summary>
        public static void SaveIfActive()
        {
            // Session du SSD : chaque modification est deja notee, le thread "synchro" s'en charge
            if (SessionSsd.Pret) return;
            if (Active && SaveTarget() != null) Save();
        }

        private static int Restore()
        {
            string target = SaveTarget();
            if (target == null || !Directory.Exists(target)) return 0;
            try
            {
                string fsos = TextCodec.PathJoin(Volume, "FSOS");
                int n = 0;
                for (int i = 0; i < SavedDirs.Length; i++)
                    n += CopyTree(target + "\\" + SavedDirs[i], fsos + "\\" + SavedDirs[i]);
                if (File.Exists(target + "\\comptes.cfg"))
                {
                    FileSystemOS.Utils.FsTools.WriteBytes(fsos + "\\Systeme\\comptes.cfg", File.ReadAllBytes(target + "\\comptes.cfg"));
                    n++;
                }
                return n;
            }
            catch (Exception) { return 0; }
        }
    }
}
