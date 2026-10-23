using System;
using System.Collections.Generic;
using Cosmos.HAL.BlockDevice;
using FileSystemOS.Utils;

namespace FileSystemOS.Pilotes
{
    /// <summary>Une partition lue sur le disque, qu'elle vienne d'une table MBR ou GPT.</summary>
    public class PartitionInfo
    {
        public int Numero;
        public ulong Debut, Secteurs;
        public string Type, Nom;
        public bool Fat;
        public long Octets => (long)Secteurs * 512;
    }

    /// <summary>
    /// LECTURE DES TABLES DE PARTITIONS (MBR et GPT), faite par File System OS.
    ///
    /// Cosmos ne sait lire que les tables MBR simples. Les disques modernes (comme le SSD du
    /// portable) utilisent GPT : une table de 128 entrees placee au secteur 2, avec le type de
    /// chaque partition sous forme d'identifiant unique (GUID) et son nom en UTF-16.
    /// Cette classe lit les deux formats et donne la liste exacte des partitions.
    /// </summary>
    public static class Gpt
    {
        // Identifiants de type les plus courants (premiers octets du GUID, en petit-boutiste)
        private static readonly uint EspaceEfi = 0xC12A7328;     // EFI System Partition (FAT32)
        private static readonly uint MicrosoftDonnees = 0xEBD0A0A2; // Microsoft basic data (NTFS souvent)
        private static readonly uint MicrosoftReserve = 0xE3C9E316;
        private static readonly uint Recuperation = 0xDE94BBA4;
        private static readonly uint LinuxDonnees = 0x0FC63DAF;
        private static readonly uint LinuxSwap = 0x0657FD6D;

        public static string TypeGpt(uint premier)
        {
            if (premier == EspaceEfi) return "EFI (FAT32)";
            if (premier == MicrosoftDonnees) return "Donnees Windows";
            if (premier == MicrosoftReserve) return "Microsoft reserve";
            if (premier == Recuperation) return "Recuperation Windows";
            if (premier == LinuxDonnees) return "Linux (ext4)";
            if (premier == LinuxSwap) return "Linux swap";
            if (premier == 0) return "(vide)";
            return "type " + Fmt.Hex(premier);
        }

        public static string TypeMbr(byte t)
        {
            switch (t)
            {
                case 0x01: return "FAT12";
                case 0x04: case 0x06: case 0x0E: return "FAT16";
                case 0x0B: case 0x0C: return "FAT32";
                case 0x07: return "NTFS / exFAT";
                case 0x83: return "Linux";
                case 0x82: return "Linux swap";
                case 0xEE: return "table GPT";
                case 0xEF: return "EFI";
                default: return "type 0x" + Fmt.Hex(t, 2);
            }
        }

        private static uint U32(byte[] b, int p) => (uint)(b[p] | (b[p + 1] << 8) | (b[p + 2] << 16) | (b[p + 3] << 24));
        private static ulong U64(byte[] b, int p) => U32(b, p) | ((ulong)U32(b, p + 4) << 32);

        /// <summary>Liste les partitions d'un disque (MBR ou GPT).</summary>
        public static List<PartitionInfo> Lire(BlockDevice d, out bool estGpt)
        {
            var r = new List<PartitionInfo>();
            estGpt = false;
            byte[] s = new byte[512];
            try { d.ReadBlock(0, 1, ref s); } catch (Exception) { return r; }
            if (s[510] != 0x55 || s[511] != 0xAA) return r;

            bool protege = false;
            for (int i = 0; i < 4; i++) if (s[446 + i * 16 + 4] == 0xEE) protege = true;

            if (!protege)
            {
                for (int i = 0; i < 4; i++)
                {
                    byte type = s[446 + i * 16 + 4];
                    if (type == 0) continue;
                    var p = new PartitionInfo();
                    p.Numero = i + 1;
                    p.Debut = U32(s, 446 + i * 16 + 8);
                    p.Secteurs = U32(s, 446 + i * 16 + 12);
                    p.Type = TypeMbr(type);
                    p.Nom = "";
                    p.Fat = type == 0x01 || type == 0x04 || type == 0x06 || type == 0x0B || type == 0x0C || type == 0x0E;
                    r.Add(p);
                }
                return r;
            }

            // --- Table GPT : en-tete au secteur 1, entrees a partir du secteur indique ---
            estGpt = true;
            byte[] tete = new byte[512];
            try { d.ReadBlock(1, 1, ref tete); } catch (Exception) { return r; }
            if (tete[0] != 'E' || tete[1] != 'F' || tete[2] != 'I' || tete[3] != ' ') return r;

            ulong debutEntrees = U64(tete, 72);
            uint nbEntrees = U32(tete, 80);
            uint tailleEntree = U32(tete, 84);
            if (tailleEntree < 128 || tailleEntree > 512 || nbEntrees > 512) return r;

            int parSecteur = (int)(512 / tailleEntree);
            byte[] sect = new byte[512];
            int numero = 0;
            for (uint i = 0; i < nbEntrees; i++)
            {
                if (i % parSecteur == 0)
                {
                    try { d.ReadBlock(debutEntrees + i / (uint)parSecteur, 1, ref sect); }
                    catch (Exception) { break; }
                }
                int o = (int)(i % parSecteur) * (int)tailleEntree;
                uint premier = U32(sect, o);
                if (premier == 0 && U32(sect, o + 4) == 0) continue;

                var p = new PartitionInfo();
                p.Numero = ++numero;
                p.Debut = U64(sect, o + 32);
                ulong fin = U64(sect, o + 40);
                p.Secteurs = fin >= p.Debut ? fin - p.Debut + 1 : 0;
                p.Type = TypeGpt(premier);
                p.Fat = premier == EspaceEfi;

                // Nom de la partition : 36 caracteres UTF-16
                char[] nom = new char[36];
                int k = 0;
                for (int c = 0; c < 36; c++)
                {
                    int ch = sect[o + 56 + c * 2] | (sect[o + 56 + c * 2 + 1] << 8);
                    if (ch == 0) break;
                    nom[k++] = ch < 128 ? (char)ch : '?';
                }
                p.Nom = new string(nom, 0, k);
                r.Add(p);
            }
            return r;
        }

        public static List<string> Rapport(BlockDevice d, string titre)
        {
            var r = new List<string>();
            var parts = Lire(d, out bool gpt);
            r.Add(titre + " : table " + (gpt ? "GPT" : "MBR") + ", " + parts.Count + " partition(s)");
            for (int i = 0; i < parts.Count; i++)
            {
                PartitionInfo p = parts[i];
                r.Add("  " + p.Numero + ". " + Fmt.Pad(p.Type, 22) + Fmt.Pad(Fmt.Size(p.Octets), 10) +
                      " secteur " + p.Debut + (p.Nom.Length > 0 ? "  \"" + p.Nom + "\"" : "") +
                      (p.Fat ? "  [lisible]" : ""));
            }
            return r;
        }
    }
}
