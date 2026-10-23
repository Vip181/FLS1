using System.Collections.Generic;
using System.IO;
using Cosmos.System.FileSystem.Listing;
using Cosmos.System.FileSystem.VFS;

namespace FileSystemOS.Utils
{
    /// <summary>
    /// Ecritures de fichiers de File System OS (et suppressions recursives).
    ///
    /// Le pilote de fichiers de Cosmos ecrit des noms longs invalides (JOURNA~1 sur le SSD,
    /// puis "Argument out of range" a la creation d'un compte). Sur le disque memoire, formate
    /// en FAT32 par File System OS, TOUTES les ecritures (fichier, dossier, suppression) passent
    /// donc par le pilote FAT32 de File System OS (FatDirect). Cosmos ne sert plus qu'a LIRE,
    /// ce qu'il fait correctement. Les autres lecteurs gardent le pilote de Cosmos.
    /// </summary>
    public static class FsTools
    {
        /// <summary>Le montage FatDirect du disque memoire si 'path' s'y trouve, sinon null.</summary>
        private static FileSystemOS.Pilotes.FatDirect Direct(string path, out string relatif)
        {
            relatif = null;
            var v = FileSystemOS.Systeme.Live.Direct;
            string vol = FileSystemOS.Systeme.Live.Volume;
            if (v == null || vol == null || path == null || path.Length < vol.Length) return null;
            if (path.Substring(0, vol.Length).ToLower() != vol.ToLower()) return null;
            relatif = path.Substring(vol.Length);
            while (relatif.StartsWith("\\") || relatif.StartsWith("/")) relatif = relatif.Substring(1);
            v.Oublier();
            return v;
        }

        /// <summary>Cree un dossier et ses parents (comme Directory.CreateDirectory).</summary>
        public static void MakeDir(string path)
        {
            var v = Direct(path, out string rel);
            if (v == null) { if (!Directory.Exists(path)) Directory.CreateDirectory(path); return; }
            string[] morceaux = rel.Split('\\');
            string courant = "";
            for (int i = 0; i < morceaux.Length; i++)
            {
                if (morceaux[i].Length == 0) continue;
                courant = courant.Length == 0 ? morceaux[i] : courant + "\\" + morceaux[i];
                v.CreerDossier(courant);                // ne fait rien si le dossier existe deja
            }
            FileSystemOS.Systeme.SessionSsd.Noter(courant, FileSystemOS.Systeme.SessionSsd.Dossier);
        }

        /// <summary>Le fichier (ou dossier) existe-t-il ?</summary>
        public static bool Exists(string path)
        {
            var v = Direct(path, out string rel);
            if (v != null)
            {
                try { return rel.Length == 0 || v.Existe(rel); }
                catch (System.Exception) { return false; }
            }
            return File.Exists(path) || Directory.Exists(path);
        }

        /// <summary>Contenu d'un fichier.</summary>
        public static byte[] ReadBytes(string path)
        {
            var v = Direct(path, out string rel);
            if (v != null) return v.Lire(rel);
            return File.ReadAllBytes(path);
        }

        public static void DeleteFile(string path)
        {
            var v = Direct(path, out string rel);
            if (v == null) { File.Delete(path); return; }
            v.Supprimer(rel);
            FileSystemOS.Systeme.SessionSsd.Noter(rel, FileSystemOS.Systeme.SessionSsd.Suppression);
        }

        /// <summary>Supprime un dossier VIDE.</summary>
        public static void DeleteDir(string path)
        {
            var v = Direct(path, out string rel);
            if (v == null) { Directory.Delete(path); return; }
            v.Supprimer(rel);
            FileSystemOS.Systeme.SessionSsd.Noter(rel, FileSystemOS.Systeme.SessionSsd.Suppression);
        }
        /// <summary>
        /// Liste tout ce que contient 'dir', le contenu d'un dossier AVANT le dossier lui-meme
        /// (ordre de suppression sur). 'dir' n'est pas inclus.
        /// </summary>
        public static void Collect(string dir, List<string> paths, List<bool> isDir)
        {
            var list = VFSManager.GetDirectoryListing(dir);
            for (int i = 0; i < list.Count; i++)
            {
                string full = list[i].mFullPath;
                if (list[i].mEntryType == DirectoryEntryTypeEnum.Directory)
                {
                    Collect(full, paths, isDir);
                    paths.Add(full);
                    isDir.Add(true);
                }
                else if (list[i].mEntryType == DirectoryEntryTypeEnum.File)
                {
                    paths.Add(full);
                    isDir.Add(false);
                }
            }
        }

        /// <summary>
        /// Ecriture SURE d'un fichier. Le pilote FAT de Cosmos refuse d'ecrire 0 octet
        /// ("Invalid data size.") : un fichier vide est donc seulement CREE, sans ecriture.
        /// </summary>
        public static void WriteBytes(string path, byte[] data)
        {
            var v = Direct(path, out string rel);
            if (v != null)
            {
                v.Ecrire(rel, data ?? new byte[0]);
                FileSystemOS.Systeme.SessionSsd.Noter(rel, FileSystemOS.Systeme.SessionSsd.Fichier);
                return;
            }
            if (data != null && data.Length > 0) { File.WriteAllBytes(path, data); return; }
            if (File.Exists(path)) File.Delete(path);
            VFSManager.CreateFile(path);
        }

        public static void WriteText(string path, string text)
        {
            byte[] b = new byte[text.Length];
            for (int i = 0; i < text.Length; i++) b[i] = text[i] < 128 ? (byte)text[i] : (byte)'?';
            WriteBytes(path, b);
        }

        public static void DeleteEntry(string path, bool dir)
        {
            if (dir) DeleteDir(path);
            else DeleteFile(path);
        }

        /// <summary>Supprime un dossier et tout son contenu. Renvoie le nombre d'elements supprimes.</summary>
        public static int DeleteTree(string dir)
        {
            var paths = new List<string>();
            var isDir = new List<bool>();
            Collect(dir, paths, isDir);
            for (int i = 0; i < paths.Count; i++) DeleteEntry(paths[i], isDir[i]);
            DeleteDir(dir);
            return paths.Count + 1;
        }
    }
}
