using System;
using System.Collections.Generic;
using Cosmos.HAL;
using FileSystemOS.Utils;

namespace FileSystemOS.Pilotes
{
    /// <summary>
    /// RECONNAISSANCE DE L'ORDINATEUR.
    ///
    /// Un PC ne s'identifie pas par sa marque mais par ses PUCES. On lit donc :
    ///  - le processeur (nom complet, fabricant) ;
    ///  - le chipset de la carte mere (pont hote PCI 00:00.0) ;
    ///  - la carte graphique ;
    ///  - le "subsystem vendor" de chaque carte PCI : c'est le fabricant de la MACHINE
    ///    (Dell 1028, HP 103C, ASUS 1043, Lenovo 17AA...) inscrit dans chaque puce soudee.
    /// De ces elements on deduit la marque, la generation, et surtout le PROFIL MATERIEL
    /// qui dit quels pilotes peuvent etre demarres sans risque sur CETTE machine.
    /// </summary>
    public static class Machine
    {
        public static string Marque = "inconnue";
        public static string Cpu = "inconnu", CpuVendor = "inconnu";
        public static string Chipset = "inconnu", Carte = "inconnue";
        public static int Generation;                 // generation Intel Core (0 si inconnue)
        public static ushort OemId;                   // identifiant PCI du fabricant de la machine
        public static bool Virtuel;
        public static bool Analyse;

        /// <summary>Marque de la machine d'apres l'identifiant OEM inscrit dans les puces.</summary>
        public static string MarqueDe(ushort oem)
        {
            switch (oem)
            {
                case 0x1028: return "Dell";
                case 0x103C: return "HP";
                case 0x1043: return "ASUS";
                case 0x17AA: return "Lenovo";
                case 0x1025: return "Acer";
                case 0x1179: return "Toshiba";
                case 0x104D: return "Sony";
                case 0x1458: return "Gigabyte";
                case 0x1462: return "MSI";
                case 0x1849: return "ASRock";
                case 0x106B: return "Apple";
                case 0x144D: return "Samsung";
                case 0x1B0A: return "Packard Bell";
                case 0x1734: return "Fujitsu";
                case 0x10CF: return "Fujitsu";
                case 0x8086: return "Intel";
                case 0x80EE: return "VirtualBox";
                case 0x15AD: return "VMware";
                case 0x1AF4: case 0x1100: return "QEMU/KVM";
                default: return oem == 0 ? "inconnue" : "OEM " + Fmt.Hex(oem, 4);
            }
        }

        /// <summary>Generation Intel Core deduite du nom du processeur (ex : i5-6300U -> 6e).</summary>
        private static int GenerationIntel(string brand)
        {
            int i = brand.IndexOf("i3-");
            if (i < 0) i = brand.IndexOf("i5-");
            if (i < 0) i = brand.IndexOf("i7-");
            if (i < 0) i = brand.IndexOf("i9-");
            if (i < 0) return 0;
            int p = i + 3;
            string num = "";
            while (p < brand.Length && brand[p] >= '0' && brand[p] <= '9' && num.Length < 5) num += brand[p++];
            if (num.Length == 5) return Fmt.ParseInt(num.Substring(0, 2));     // 12700, 13600...
            if (num.Length == 4) return Fmt.ParseInt(num.Substring(0, 1));     // 6300U, 8250U...
            if (num.Length == 3) return 1;                                     // 1re generation
            return 0;
        }

        public static void Detect()
        {
            if (Analyse) return;
            Analyse = true;

            try { Cpu = Cosmos.Core.CPU.GetCPUBrandString().Trim(); } catch (Exception) { }
            try { CpuVendor = Cosmos.Core.CPU.GetCPUVendorName(); } catch (Exception) { }
            Generation = GenerationIntel(Cpu);
            try { Virtuel = Cosmos.System.VMTools.IsVirtualBox || Cosmos.System.VMTools.IsVMWare || Cosmos.System.VMTools.IsQEMU; }
            catch (Exception) { Virtuel = false; }

            if (PciCatalogue.Tous.Count == 0) PciCatalogue.Scan();

            // Marque : identifiant OEM le plus frequent parmi les cartes PCI
            var ids = new List<ushort>();
            var nb = new List<int>();
            for (int i = 0; i < PciCatalogue.Tous.Count; i++)
            {
                ushort sv = PciCatalogue.Tous[i].SubVendor;
                if (sv == 0 || sv == 0xFFFF) continue;
                int k = ids.IndexOf(sv);
                if (k < 0) { ids.Add(sv); nb.Add(1); } else nb[k]++;
            }
            int best = -1;
            for (int i = 0; i < ids.Count; i++) if (best < 0 || nb[i] > nb[best]) best = i;
            if (best >= 0) { OemId = ids[best]; Marque = MarqueDe(OemId); }
            if (Virtuel && Marque == "inconnue") Marque = "machine virtuelle";

            for (int i = 0; i < PciCatalogue.Tous.Count; i++)
            {
                Peripherique p = PciCatalogue.Tous[i];
                if (p.Class == 0x06 && p.Sub == 0x00) Chipset = p.VendorName + " " + Fmt.Hex(p.Device, 4);
                if (p.Class == 0x03 && Carte == "inconnue") Carte = p.VendorName + " " + Fmt.Hex(p.Device, 4);
            }
        }

        public static List<string> Report()
        {
            Detect();
            var r = new List<string>();
            r.Add("Machine    : " + Marque + (Virtuel ? " (virtuelle)" : " (physique)") +
                  (OemId != 0 ? "  OEM " + Fmt.Hex(OemId, 4) : ""));
            r.Add("Processeur : " + Cpu + " [" + CpuVendor + "]" + (Generation > 0 ? "  -  " + Generation + "e generation Core" : ""));
            r.Add("Chipset    : " + Chipset);
            r.Add("Graphique  : " + Carte);
            r.Add("Profil     : " + PiloteReseau.Profil());
            return r;
        }
    }
}
