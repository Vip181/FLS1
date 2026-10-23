using System;
using Cosmos.HAL.BlockDevice;

namespace FileSystemOS.Systeme
{
    /// <summary>
    /// Disque en MEMOIRE vive (mode Live). Il se presente a Cosmos comme un vrai disque dur :
    /// table de partitions MBR + une partition FAT16 creee ici, octet par octet.
    /// Tout l'OS (explorateur, editeur, images...) l'utilise donc normalement.
    /// Stocke en morceaux de 1 Mo pour eviter un seul tableau geant.
    /// </summary>
    public class RamDisk : BlockDevice
    {
        private const int Chunk = 1024 * 1024;
        private const uint PartStart = 2048;
        private readonly byte[][] chunks;

        public RamDisk(uint sizeMb)
        {
            mBlockSize = 512;
            mBlockCount = sizeMb * 2048UL;
            chunks = new byte[sizeMb][];
            for (int i = 0; i < chunks.Length; i++) chunks[i] = new byte[Chunk];
        }

        public override BlockDeviceType Type => BlockDeviceType.HardDrive;

        public long Bytes => (long)mBlockCount * 512;

        public override void ReadBlock(ulong aBlockNo, ulong aBlockCount, ref byte[] aData)
        {
            ulong n = aBlockCount * 512;
            ulong start = aBlockNo * 512;
            for (ulong i = 0; i < n; i++)
            {
                ulong p = start + i;
                aData[i] = chunks[p >> 20][p & 0xFFFFF];
            }
        }

        public override void WriteBlock(ulong aBlockNo, ulong aBlockCount, ref byte[] aData)
        {
            ulong n = aBlockCount * 512;
            ulong start = aBlockNo * 512;
            for (ulong i = 0; i < n; i++)
            {
                ulong p = start + i;
                chunks[p >> 20][p & 0xFFFFF] = aData[i];
            }
        }

        // ---- Ecriture directe (formatage) ----
        private void B(ulong p, byte v) => chunks[p >> 20][p & 0xFFFFF] = v;
        private void U16(ulong p, uint v) { B(p, (byte)v); B(p + 1, (byte)(v >> 8)); }
        private void U32(ulong p, uint v) { U16(p, v & 0xFFFF); U16(p + 2, v >> 16); }
        private void Text(ulong p, string s) { for (int i = 0; i < s.Length; i++) B(p + (ulong)i, (byte)s[i]); }

        /// <summary>
        /// Cree le MBR et une partition FAT32 (format le mieux gere par le pilote de Cosmos),
        /// avec le marqueur LIVE.TAG ecrit par le pilote FAT32 de File System OS.
        /// Leve une exception si c'est impossible (disque trop petit...) : FAT16 en secours.
        /// </summary>
        public FileSystemOS.Pilotes.FatDirect FormatFat32()
        {
            uint n = (uint)mBlockCount - PartStart;
            ulong mbr = 446;
            B(mbr, 0x80);
            B(mbr + 4, 0x0C);                          // FAT32 (LBA)
            U32(mbr + 8, PartStart);
            U32(mbr + 12, n);
            B(510, 0x55); B(511, 0xAA);

            FileSystemOS.Systeme.Fat32.Formater(this, PartStart, n, "FSOS-LIVE");

            var v = FileSystemOS.Pilotes.FatDirect.Monter(this, PartStart, "memoire", out string erreur);
            if (v == null) throw new Exception("FAT32 du disque memoire illisible : " + erreur);
            v.Ecriture = true;
            byte[] tag = new byte[] { (byte)'F', (byte)'S', (byte)'O', (byte)'S' };
            v.Ecrire("LIVE.TAG", tag);
            return v;
        }

        /// <summary>Cree le MBR et une partition FAT16 (clusters de 2 Ko), avec le marqueur LIVE.TAG.</summary>
        public void FormatFat16()
        {
            uint total = (uint)mBlockCount;
            uint n = total - PartStart;                 // secteurs de la partition
            const uint rsvd = 4, rootEntries = 512, secPerClus = 4;
            uint rootSecs = rootEntries * 32 / 512;     // 32 secteurs

            // Taille d'une FAT (2 octets par cluster), calculee par approximations
            uint fat = 1;
            while (true)
            {
                uint clusters = (n - rsvd - 2 * fat - rootSecs) / secPerClus;
                uint need = ((clusters + 2) * 2 + 511) / 512;
                if (need <= fat) break;
                fat = need;
            }

            // --- MBR ---
            ulong mbr = 446;
            B(mbr, 0x80);
            B(mbr + 4, 0x0E);                          // FAT16 (LBA)
            U32(mbr + 8, PartStart);
            U32(mbr + 12, n);
            B(510, 0x55); B(511, 0xAA);

            // --- Secteur de demarrage de la partition (BPB) ---
            ulong bs = (ulong)PartStart * 512;
            B(bs, 0xEB); B(bs + 1, 0x3C); B(bs + 2, 0x90);
            Text(bs + 3, "FSOSRAM ");
            U16(bs + 11, 512);
            B(bs + 13, (byte)secPerClus);
            U16(bs + 14, rsvd);
            B(bs + 16, 2);
            U16(bs + 17, rootEntries);
            U16(bs + 19, 0);
            B(bs + 21, 0xF8);
            U16(bs + 22, fat);
            U16(bs + 24, 63);
            U16(bs + 26, 255);
            U32(bs + 28, PartStart);
            U32(bs + 32, n);
            B(bs + 36, 0x80);
            B(bs + 38, 0x29);
            U32(bs + 39, 0x46534F53);                  // "FSOS"
            Text(bs + 43, "FSOS-LIVE  ");
            Text(bs + 54, "FAT16   ");
            B(bs + 510, 0x55); B(bs + 511, 0xAA);

            // --- Les deux FAT : entrees 0 et 1 reservees ---
            for (uint f = 0; f < 2; f++)
            {
                ulong fp = bs + (rsvd + f * fat) * 512UL;
                B(fp, 0xF8); B(fp + 1, 0xFF); B(fp + 2, 0xFF); B(fp + 3, 0xFF);
            }

            // --- Repertoire racine : nom du volume + marqueur LIVE.TAG ---
            ulong root = bs + (rsvd + 2 * fat) * 512UL;
            Text(root, "FSOS-LIVE  ");
            B(root + 11, 0x08);
            Text(root + 32, "LIVE    TAG");
            B(root + 32 + 11, 0x20);
        }
    }
}
