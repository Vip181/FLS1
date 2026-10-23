using System;
using System.Collections.Generic;
using Cosmos.HAL.BlockDevice;
using FileSystemOS.Graphique;
using FileSystemOS.Processus;
using FileSystemOS.Utils;

namespace FileSystemOS.Systeme
{
    /// <summary>
    /// INSTALLATION COMPLETE depuis un disque dur : File System OS recopie, secteur par secteur,
    /// le disque sur lequel il a demarre (chargeur Limine + noyau + \FSOS) vers un autre disque.
    /// Le disque copie demarre ensuite tout seul : plus besoin d'ISO, de cle ni de Linux.
    /// Le disque source est reconnu a son volume FAT32 nomme "FSOS" (cree par creer-disque.sh).
    /// </summary>
    public static class DiskClone
    {
        public static BlockDevice Source;

        private static bool Usable(BlockDevice d)
        {
            if (d == null || d.BlockSize != 512 || d.BlockCount < 2048) return false;
            if (Live.Disk != null && d == (BlockDevice)Live.Disk) return false;
            try { return d.Type == BlockDeviceType.HardDrive; } catch (Exception) { return true; }
        }

        private static byte[] ReadSector(BlockDevice d, ulong lba)
        {
            byte[] b = new byte[512];
            d.ReadBlock(lba, 1, ref b);
            return b;
        }

        /// <summary>Le disque de demarrage File System OS (null si on a demarre sur l'ISO ou une cle).</summary>
        public static BlockDevice FindSource()
        {
            Source = null;
            for (int i = 0; i < BlockDevice.Devices.Count; i++)
            {
                BlockDevice d = BlockDevice.Devices[i];
                if (!Usable(d)) continue;
                try
                {
                    byte[] mbr = ReadSector(d, 0);
                    if (mbr[510] != 0x55 || mbr[511] != 0xAA || mbr[0] == 0) continue;     // pas de code de demarrage
                    uint lba = (uint)(mbr[454] | (mbr[455] << 8) | (mbr[456] << 16) | (mbr[457] << 24));
                    if (lba == 0 || lba >= d.BlockCount) continue;
                    byte[] bs = ReadSector(d, lba);
                    // Volume FAT32 "FSOS" : nom a l'octet 71, type "FAT32" a l'octet 82
                    if (bs[71] == 'F' && bs[72] == 'S' && bs[73] == 'O' && bs[74] == 'S' && bs[82] == 'F' && bs[85] == '3')
                    {
                        Source = d;
                        return d;
                    }
                }
                catch (Exception) { }
            }
            return null;
        }

        /// <summary>Disques qui peuvent recevoir la copie (assez grands, autres que la source).</summary>
        public static List<BlockDevice> Targets()
        {
            var r = new List<BlockDevice>();
            if (Source == null) return r;
            for (int i = 0; i < BlockDevice.Devices.Count; i++)
            {
                BlockDevice d = BlockDevice.Devices[i];
                if (d == Source || !Usable(d)) continue;
                if (d.BlockCount >= Source.BlockCount) r.Add(d);
            }
            return r;
        }

        public static int Number(BlockDevice d)
        {
            int n = 0;
            for (int i = 0; i < BlockDevice.Devices.Count; i++)
            {
                if (!Usable(BlockDevice.Devices[i])) continue;
                n++;
                if (BlockDevice.Devices[i] == d) return n;
            }
            return 0;
        }
    }

    /// <summary>Copie 128 secteurs (64 Ko) par tour : l'OS reste utilisable pendant la copie.</summary>
    public class CloneThread : KThread
    {
        private const ulong Chunk = 128;
        private readonly InstallerWindow win;
        private readonly BlockDevice src, dst;
        private readonly ulong total;
        private readonly byte[] buf = new byte[Chunk * 512];
        private ulong pos;
        private int lastTenth = -1;

        public CloneThread(InstallerWindow w, BlockDevice source, BlockDevice target) : base("copie")
        {
            win = w; src = source; dst = target;
            total = source.BlockCount;
        }

        public override void Step()
        {
            try
            {
                if (pos >= total)
                {
                    win.Log("Copie terminee : " + Fmt.Size((long)total * 512) + " recopies.");
                    win.Log("Le disque " + DiskClone.Number(dst) + " demarre maintenant tout seul.");
                    win.Log("Retirez l'ancien disque / la cle et redemarrez.");
                    win.Finished();
                    Owner.Kill();
                    return;
                }
                ulong n = total - pos < Chunk ? total - pos : Chunk;
                byte[] b = n == Chunk ? buf : new byte[n * 512];
                src.ReadBlock(pos, n, ref b);
                dst.WriteBlock(pos, n, ref b);
                pos += n;

                int tenth = (int)(pos * 10 / total);
                if (tenth != lastTenth) { lastTenth = tenth; win.Log("Copie : " + tenth * 10 + " %  (" + Fmt.Size((long)pos * 512) + ")"); }
            }
            catch (Exception e)
            {
                win.Log("Erreur : " + e.Message);
                win.Finished();
                Owner.Kill();
            }
        }
    }
}
