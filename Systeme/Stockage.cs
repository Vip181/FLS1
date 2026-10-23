using System;
using System.Collections.Generic;
using Cosmos.HAL.BlockDevice;
using FileSystemOS.Pilotes;

namespace FileSystemOS.Systeme
{
    /// <summary>
    /// Diagnostic du stockage : dit POURQUOI aucun disque n'est visible.
    /// Cosmos lit les disques en IDE/ATA et en AHCI. Les portables livres en mode
    /// "RAID On" (cas frequent chez Dell) cachent le disque : il faut passer le BIOS en AHCI.
    /// </summary>
    public static class Stockage
    {
        public static List<string> Rapport()
        {
            var r = new List<string>();
            int disques = 0;
            try
            {
                for (int i = 0; i < BlockDevice.Devices.Count; i++)
                {
                    BlockDevice d = BlockDevice.Devices[i];
                    bool live = Live.Disk != null && d == (BlockDevice)Live.Disk;
                    bool fsos = d is VueFsos;
                    if (!live) ++disques;
                    r.Add("  " + (live ? "disque memoire" : (fsos ? "partition FSOS du SSD" : "disque " + disques)) +
                          " : " + Utils.Fmt.Size((long)(d.BlockCount * d.BlockSize)));
                }
            }
            catch (Exception e) { r.Add("  erreur : " + e.Message); }
            if (disques == 0) r.Add("  aucun disque lisible");
            for (int i = 0; i < Ahci.Disques.Count; i++) r.Add("  sata" + i + " : " + Ahci.Disques[i].Fiche);
            for (int i = 0; i < Ahci.Caches.Count; i++)
                r.Add("  garde de cote : sata" + i + " " + Ahci.Caches[i].Modele + " " +
                      Utils.Fmt.Size(Ahci.Caches[i].Octets) + " (pas de partition FAT)");
            for (int i = 0; i < Nvme.Caches.Count; i++)
                r.Add("  garde de cote : nvme" + i + " " + Nvme.Caches[i].Modele + " " +
                      Utils.Fmt.Size(Nvme.Caches[i].Octets) + " (pas de partition FAT)");
            r.Add("  " + VueFsos.Journal);

            // Controleurs de stockage vus sur le bus PCI
            try
            {
                if (PciCatalogue.Tous.Count == 0) PciCatalogue.Scan();
                for (int i = 0; i < PciCatalogue.Tous.Count; i++)
                {
                    Peripherique p = PciCatalogue.Tous[i];
                    if (p.Class != 0x01) continue;
                    string mode = p.Sub == 0x01 ? "IDE (lisible)" :
                                  p.Sub == 0x06 ? "AHCI (pilote File System OS) : " + Ahci.Journal :
                                  p.Sub == 0x04 ? "RAID : passez le BIOS en AHCI !" :
                                  p.Sub == 0x08 ? "NVMe (pilote File System OS) : " + Nvme.Journal :
                                  "mode " + Utils.Fmt.Hex(p.Sub, 2);
                    r.Add("  controleur [" + p.Id + "] " + p.VendorName + " - " + mode);
                }
            }
            catch (Exception) { }
            return r;
        }

        /// <summary>Nature du disque (NVMe, SATA/IDE, memoire) d'apres sa taille.</summary>
        public static string Genre(long octets)
        {
            for (int i = 0; i < Nvme.Disques.Count; i++)
                if (Nvme.Disques[i].Octets == octets) return "NVMe";
            for (int i = 0; i < Ahci.Disques.Count; i++)
                if (Ahci.Disques[i].Octets == octets) return "SATA";
            if (Live.Disk != null && Live.Disk.Bytes == octets) return "memoire";
            return "SATA/IDE";
        }

        /// <summary>Controleurs USB vus (information : la lecture des cles USB n'est pas geree).</summary>
        public static List<string> Usb()
        {
            var r = new List<string>();
            try
            {
                if (PciCatalogue.Tous.Count == 0) PciCatalogue.Scan();
                for (int i = 0; i < PciCatalogue.Tous.Count; i++)
                {
                    Peripherique p = PciCatalogue.Tous[i];
                    if (p.Class == 0x0C && p.Sub == 0x03) r.Add("  [" + p.Id + "] " + p.VendorName + " - " + p.Kind);
                }
            }
            catch (Exception) { }
            if (r.Count == 0) r.Add("  aucun controleur USB vu");
            r.Add("  Lecture des cles USB : pas de pilote (xHCI + stockage de masse a ecrire).");
            r.Add("  Utilisez le disque NVMe/SATA interne, ou la cle virtuelle en machine virtuelle.");
            return r;
        }

        public static string Conseil()
        {
            try
            {
                if (PciCatalogue.Tous.Count == 0) PciCatalogue.Scan();
                for (int i = 0; i < PciCatalogue.Tous.Count; i++)
                {
                    Peripherique p = PciCatalogue.Tous[i];
                    if (p.Class != 0x01) continue;
                    if (p.Sub == 0x04) return "BIOS : SATA Operation = AHCI (RAID cache meme les disques NVMe)";
                    if (p.Sub == 0x06) return Ahci.Disques.Count > 0
                        ? "SSD vu par le pilote AHCI. Console : ssd partitions, ssd monter sata0 <n>, ssd installer"
                        : "Controleur AHCI present mais aucun disque branche dessus";
                    if (p.Sub == 0x08) return Nvme.Disques.Count > 0
                        ? "Disque NVMe pret : " + Nvme.Journal
                        : "Disque NVMe vu mais non demarre : " + Nvme.Journal;
                }
            }
            catch (Exception) { }
            return Machine.Virtuel ? "Ajoutez un disque IDE a la machine virtuelle."
                                   : "Aucun controleur de stockage visible : BIOS SATA Operation = AHCI.";
        }
    }
}
