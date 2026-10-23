using System;
using Cosmos.HAL.BlockDevice;

namespace FileSystemOS.Systeme
{
    /// <summary>
    /// LECTURE DE LA TABLE DE PARTITIONS, faite par File System OS AVANT de confier un disque
    /// a Cosmos.
    ///
    /// Pourquoi : Cosmos ne connait que le FAT. Si on lui donne un disque Windows (GPT + NTFS),
    /// son montage peut tourner en rond et l'OS reste bloque sur "montage des disques".
    /// On regarde donc nous-memes le premier secteur : s'il n'y a aucune partition FAT,
    /// le disque est garde de cote (toujours accessible pour l'installation) mais il n'est pas
    /// donne a Cosmos.
    /// </summary>
    public static class Partitions
    {
        public const int Aucune = 0, Fat = 1, Gpt = 2, Autre = 3, Illisible = 4;

        public static string Nom(int t)
        {
            switch (t)
            {
                case Fat: return "FAT (lisible)";
                case Gpt: return "GPT (disque Windows / Linux moderne)";
                case Autre: return "partitions non FAT (NTFS, ext4...)";
                case Illisible: return "table illisible";
                default: return "aucune partition";
            }
        }

        /// <summary>Type FAT au sens MBR ?</summary>
        private static bool EstFat(byte t) =>
            t == 0x01 || t == 0x04 || t == 0x06 || t == 0x0B || t == 0x0C || t == 0x0E || t == 0x11 ||
            t == 0x14 || t == 0x16 || t == 0x1B || t == 0x1C || t == 0x1E;

        /// <summary>Analyse le premier secteur du disque.</summary>
        public static int Analyser(BlockDevice d, out string detail)
        {
            detail = "";
            byte[] mbr = new byte[512];
            try { d.ReadBlock(0, 1, ref mbr); }
            catch (Exception e) { detail = e.Message; return Illisible; }

            if (mbr[510] != 0x55 || mbr[511] != 0xAA) { detail = "pas de signature de table"; return Aucune; }

            bool gpt = false, fat = false, autre = false;
            for (int i = 0; i < 4; i++)
            {
                byte type = mbr[446 + i * 16 + 4];
                if (type == 0x00) continue;
                if (type == 0xEE) { gpt = true; continue; }     // table GPT protegee
                if (EstFat(type)) fat = true; else autre = true;
                detail += (detail.Length > 0 ? ", " : "") + "type 0x" + Utils.Fmt.Hex(type, 2);
            }

            if (fat) return Fat;
            if (gpt) { detail = "table GPT"; return Gpt; }
            if (autre) return Autre;
            return Aucune;
        }
    }
}
