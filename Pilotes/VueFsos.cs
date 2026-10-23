using System;
using System.Collections.Generic;
using Cosmos.HAL.BlockDevice;
using FileSystemOS.Utils;

namespace FileSystemOS.Pilotes
{
    /// <summary>
    /// LA PARTITION FSOS DEVIENT UN VRAI LECTEUR.
    ///
    /// Un disque Windows (GPT + NTFS) bloquerait le montage de Cosmos : il est garde de cote.
    /// Mais s'il contient une partition FAT32 nommee "FSOS", on la presente SEULE a Cosmos,
    /// sous la forme d'un petit disque virtuel :
    ///   - secteur 0           : un faux MBR qui annonce une seule partition FAT32 ;
    ///   - secteurs 1 a 2047   : du vide ;
    ///   - secteurs 2048 et +  : les vrais secteurs de la partition FSOS.
    /// Cosmos la monte alors comme n'importe quel lecteur : Explorateur, installation,
    /// sauvegarde de la session, configuration... tout fonctionne dessus, d'un demarrage a l'autre.
    ///
    /// Les autres partitions du disque (Windows, EFI, recuperation) restent invisibles :
    /// toute lecture ou ecriture en dehors de la partition FSOS est refusee.
    ///
    /// EXPERIMENTAL, DESACTIVE : le pilote de fichiers de Cosmos ecrivait des noms longs invalides
    /// sur le SSD du Dell. La session installee passe par SessionSsd (pilote de File System OS).
    /// Ne sert que si un fichier FSOS-MONTER.TXT est depose a la racine de la partition.
    /// </summary>
    public class VueFsos : BlockDevice
    {
        public const string FichierInterdit = "FSOS-PAS-MONTER.TXT";
        public const string FichierActiver = "FSOS-MONTER.TXT";
        private const ulong Decalage = 2048;

        public static readonly List<VueFsos> Vues = new List<VueFsos>();
        public static string Journal = "aucune partition FSOS sur les disques Windows";

        private readonly BlockDevice disque;
        private readonly ulong debut, taille;
        private readonly byte[] mbr = new byte[512];
        public readonly string Origine;

        public BlockDevice Disque => disque;
        public ulong Debut => debut;

        public override BlockDeviceType Type => BlockDeviceType.HardDrive;

        private VueFsos(BlockDevice d, ulong debutPartition, ulong secteurs, string origine)
        {
            disque = d;
            debut = debutPartition;
            taille = secteurs;
            Origine = origine;
            mBlockSize = 512;
            mBlockCount = Decalage + secteurs;

            // Faux MBR : une seule partition, active, type 0x0C (FAT32 adressage LBA)
            int e = 446;
            mbr[e] = 0x80;
            mbr[e + 1] = 0xFE; mbr[e + 2] = 0xFF; mbr[e + 3] = 0xFF;   // CHS "au-dela" : on lit en LBA
            mbr[e + 4] = 0x0C;
            mbr[e + 5] = 0xFE; mbr[e + 6] = 0xFF; mbr[e + 7] = 0xFF;
            Ecrire32(mbr, e + 8, (uint)Decalage);
            Ecrire32(mbr, e + 12, (uint)secteurs);
            Ecrire32(mbr, 440, 0x46534F53);                             // signature de disque "FSOS"
            mbr[510] = 0x55;
            mbr[511] = 0xAA;
        }

        private static void Ecrire32(byte[] b, int p, uint v)
        {
            b[p] = (byte)v; b[p + 1] = (byte)(v >> 8); b[p + 2] = (byte)(v >> 16); b[p + 3] = (byte)(v >> 24);
        }

        private void Verifier(ulong lba, ulong count)
        {
            if (lba + count > Decalage + taille)
                throw new Exception("acces hors de la partition FSOS refuse (secteur " + (lba + count) + ")");
        }

        public override void ReadBlock(ulong lba, ulong count, ref byte[] data)
        {
            Verifier(lba, count);
            if (lba >= Decalage)
            {
                disque.ReadBlock(debut + (lba - Decalage), count, ref data);
                return;
            }
            // Debut du disque virtuel : faux MBR puis vide, fabriques en memoire
            for (ulong i = 0; i < count; i++)
            {
                ulong s = lba + i;
                int pos = (int)(i * 512);
                if (s >= Decalage)
                {
                    // La demande deborde sur la partition : on lit le reste d'un coup
                    ulong reste = count - i;
                    byte[] tmp = new byte[reste * 512];
                    disque.ReadBlock(debut, reste, ref tmp);
                    Array.Copy(tmp, 0, data, pos, tmp.Length);
                    return;
                }
                if (s == 0) Array.Copy(mbr, 0, data, pos, 512);
                else for (int k = 0; k < 512; k++) data[pos + k] = 0;
            }
        }

        public override void WriteBlock(ulong lba, ulong count, ref byte[] data)
        {
            Verifier(lba, count);
            if (lba >= Decalage)
            {
                disque.WriteBlock(debut + (lba - Decalage), count, ref data);
                return;
            }
            // Le faux MBR et le vide ne sont jamais ecrits sur le vrai disque
            ulong saut = Decalage - lba;
            if (count <= saut) return;
            ulong reste = count - saut;
            byte[] tmp = new byte[reste * 512];
            Array.Copy(data, (int)(saut * 512), tmp, 0, tmp.Length);
            disque.WriteBlock(debut, reste, ref tmp);
        }

        /// <summary>
        /// A appeler pour un disque garde de cote, AVANT le montage de Cosmos : si une partition
        /// FAT32 nommee FSOS s'y trouve, elle est donnee a Cosmos comme un lecteur a part entiere.
        /// </summary>
        public static void Proposer(BlockDevice d, string nom)
        {
            try
            {
                var parts = Gpt.Lire(d, out bool gpt);
                for (int i = 0; i < parts.Count; i++)
                {
                    PartitionInfo p = parts[i];
                    if (p.Type.StartsWith("EFI")) continue;               // demarrage de Windows : jamais
                    var v = FatDirect.Monter(d, p.Debut, nom, out string erreur);
                    if (v == null) continue;
                    // 1.19 : le pilote de Cosmos ecrivait des noms longs invalides sur le SSD. La session
                    // installee passe maintenant par SessionSsd (pilote de File System OS) ; Cosmos ne
                    // monte la partition que si FSOS-MONTER.TXT le demande expressement.
                    if (!v.Existe(FichierActiver))
                    {
                        if (v.EstFsos) Journal = "partition FSOS trouvee sur " + nom + " (n." + p.Numero + ") : geree par le pilote de File System OS";
                        continue;
                    }
                    if (v.Existe(FichierInterdit))
                    {
                        Journal = "File System OS installe sur " + nom + " (n." + p.Numero + ") mais " + FichierInterdit + " : mode Live";
                        continue;
                    }
                    var vue = new VueFsos(d, p.Debut, p.Secteurs, nom + " partition " + p.Numero);
                    Vues.Add(vue);
                    if (!BlockDevice.Devices.Contains(vue)) BlockDevice.Devices.Add(vue);
                    Journal = "File System OS installe sur " + nom + " (partition " + p.Numero + ", " +
                              Fmt.Size(p.Octets) + ") : monte comme lecteur systeme";
                    return;
                }
            }
            catch (Exception e) { Journal = "partition FSOS de " + nom + " : erreur " + e.Message; }
        }

        /// <summary>Vrai si cette partition est deja montee par Cosmos (eviter d'ecrire des deux cotes).</summary>
        public static bool Montee(BlockDevice d, ulong debutPartition)
        {
            for (int i = 0; i < Vues.Count; i++)
                if (Vues[i].disque == d && Vues[i].debut == debutPartition) return true;
            return false;
        }
    }
}
