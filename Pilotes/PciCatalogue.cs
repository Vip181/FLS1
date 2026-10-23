using System.Collections.Generic;
using Cosmos.HAL;
using FileSystemOS.Utils;

namespace FileSystemOS.Pilotes
{
    /// <summary>Un peripherique PCI identifie (nom, type, pilote).</summary>
    public class Peripherique
    {
        public ushort Vendor, Device, SubVendor, SubDevice;
        public byte Class, Sub, ProgIF;
        public uint Bus, Slot, Function;
        public string VendorName, Name, Kind, Driver;
        public bool Network, Wifi, Supported;

        public string Id => Fmt.Hex(Vendor, 4) + ":" + Fmt.Hex(Device, 4);
        public string Location => Bus + ":" + Slot + "." + Function;
    }

    /// <summary>
    /// Catalogue PCI : parcourt TOUS les peripheriques de l'ordinateur et les identifie
    /// (fabricant, modele, type). Les cartes reseau (Ethernet et Wi-Fi) sont reconnues
    /// par leurs identifiants, avec l'etat de leur pilote.
    /// </summary>
    public static class PciCatalogue
    {
        public static readonly List<Peripherique> Tous = new List<Peripherique>();

        public static List<Peripherique> Scan()
        {
            Tous.Clear();
            for (int i = 0; i < PCI.Devices.Count; i++)
            {
                PCIDevice d = PCI.Devices[i];
                var p = new Peripherique();
                p.Vendor = d.VendorID; p.Device = d.DeviceID;
                // Subsystem vendor/device = fabricant de la MACHINE (offsets PCI 0x2C / 0x2E).
                // Si ta version de Cosmos n'a pas ReadRegister16, remplace par :
                //   p.SubVendor = d.SubsystemVendorID; p.SubDevice = d.SubsystemID;
                try { p.SubVendor = d.ReadRegister16(0x2C); p.SubDevice = d.ReadRegister16(0x2E); }
                catch (System.Exception) { }
                p.Class = d.ClassCode; p.Sub = d.Subclass; p.ProgIF = d.ProgIF;
                p.Bus = d.bus; p.Slot = d.slot; p.Function = d.function;
                p.VendorName = VendorName(p.Vendor);
                p.Kind = ClassName(p.Class, p.Sub, p.ProgIF);

                string nic = NicName(p.Vendor, p.Device, out bool wifi);
                p.Wifi = wifi || (p.Class == 0x02 && p.Sub == 0x80) || p.Class == 0x0D;
                p.Network = p.Class == 0x02 || p.Class == 0x0D || nic != null;
                p.Name = nic ?? (p.VendorName + " " + p.Kind);
                if (p.Network)
                {
                    p.Kind = p.Wifi ? "Carte Wi-Fi" : "Carte Ethernet";
                    p.Driver = DriverFor(p, out bool ok);
                    p.Supported = ok;
                }
                else p.Driver = "";
                Tous.Add(p);
            }
            return Tous;
        }

        public static List<Peripherique> Reseau()
        {
            var r = new List<Peripherique>();
            for (int i = 0; i < Tous.Count; i++) if (Tous[i].Network) r.Add(Tous[i]);
            return r;
        }

        private static string DriverFor(Peripherique p, out bool ok)
        {
            ok = true;
            string d = PiloteReseau.Pour(p, out ok);
            return d;
        }

        public static string VendorName(ushort v)
        {
            switch (v)
            {
                case 0x8086: return "Intel";
                case 0x8087: return "Intel";
                case 0x10EC: return "Realtek";
                case 0x14E4: return "Broadcom";
                case 0x168C: return "Qualcomm Atheros";
                case 0x1969: return "Qualcomm Atheros";
                case 0x17CB: return "Qualcomm";
                case 0x1022: return "AMD";
                case 0x1002: return "AMD/ATI";
                case 0x10DE: return "NVIDIA";
                case 0x15AD: return "VMware";
                case 0x80EE: return "VirtualBox";
                case 0x1AF4: return "VirtIO (Red Hat)";
                case 0x1234: return "QEMU/Bochs";
                case 0x14C3: return "MediaTek";
                case 0x11AB: return "Marvell";
                case 0x1B4B: return "Marvell";
                case 0x10B7: return "3Com";
                case 0x1186: return "D-Link";
                case 0x1814: return "Ralink";
                case 0x1B21: return "ASMedia";
                case 0x106B: return "Apple";
                case 0x1106: return "VIA";
                case 0x1039: return "SiS";
                case 0x1217: return "O2 Micro";
                case 0x1180: return "Ricoh";
                case 0x144D: return "Samsung";
                case 0x1C5C: return "SK hynix";
                case 0x15B7: return "SanDisk/WD";
                case 0x1E0F: return "Kioxia";
                default: return "Fabricant " + Fmt.Hex(v, 4);
            }
        }

        public static string ClassName(byte c, byte s, byte p)
        {
            switch (c)
            {
                case 0x00: return "peripherique";
                case 0x01:
                    if (s == 0x01) return "controleur IDE";
                    if (s == 0x06) return "disque SATA (pilote File System OS, AHCI)";
                    if (s == 0x08) return "disque NVMe (pilote File System OS)";
                    if (s == 0x00) return "controleur SCSI";
                    return "controleur de stockage";
                case 0x02: return s == 0x80 ? "controleur reseau (Wi-Fi probable)" : "controleur Ethernet";
                case 0x03: return "carte graphique";
                case 0x04: return s == 0x03 ? "audio HD" : "multimedia";
                case 0x05: return "memoire";
                case 0x06:
                    if (s == 0x00) return "pont hote";
                    if (s == 0x01) return "pont ISA";
                    if (s == 0x04) return "pont PCI";
                    return "pont";
                case 0x07: return "communication";
                case 0x08: return "peripherique systeme";
                case 0x09: return "entree";
                case 0x0C:
                    if (s == 0x03)
                    {
                        if (p == 0x00) return "USB 1 (UHCI)";
                        if (p == 0x10) return "USB 1 (OHCI)";
                        if (p == 0x20) return "USB 2 (EHCI)";
                        if (p == 0x30) return "USB 3 (xHCI)";
                        return "controleur USB";
                    }
                    if (s == 0x05) return "SMBus";
                    if (s == 0x00) return "FireWire";
                    return "bus serie";
                case 0x0D: return "controleur sans fil";
                case 0x11: return "traitement du signal";
                default: return "classe " + Fmt.Hex(c, 2);
            }
        }

        /// <summary>Nom precis des cartes reseau courantes. wifi = vrai pour une carte sans fil.</summary>
        public static string NicName(ushort v, ushort d, out bool wifi)
        {
            wifi = false;
            if (v == 0x8086)
            {
                switch (d)
                {
                    case 0x100E: return "Intel PRO/1000 MT (82540EM)";
                    case 0x100F: return "Intel PRO/1000 MT (82545EM)";
                    case 0x1004: return "Intel PRO/1000 (82543GC)";
                    case 0x10D3: return "Intel 82574L Gigabit";
                    case 0x1533: return "Intel I210 Gigabit";
                    case 0x1539: return "Intel I211 Gigabit";
                    case 0x153A: return "Intel I217-LM";
                    case 0x153B: return "Intel I217-V";
                    case 0x15A0: case 0x15A1: case 0x15A2: case 0x15A3: return "Intel I218 Gigabit";
                    case 0x156F: return "Intel I219-LM";
                    case 0x1570: return "Intel I219-V";
                    case 0x15B7: case 0x15BB: case 0x15BD: case 0x15D7: case 0x15E3: case 0x0D4C: case 0x0D4E: case 0x15FB: return "Intel I219-LM";
                    case 0x15B8: case 0x15BC: case 0x15BE: case 0x15D8: case 0x15D6: case 0x0D4D: case 0x0D4F: case 0x15FC: return "Intel I219-V";
                    case 0x15F3: return "Intel I225-V 2.5G";
                    case 0x125C: return "Intel I226-V 2.5G";
                    case 0x1502: case 0x1503: return "Intel 82579 Gigabit";
                    case 0x10EA: case 0x10EB: return "Intel 82577 Gigabit";
                    case 0x10EF: case 0x10F0: return "Intel 82578 Gigabit";
                }
                wifi = true;
                switch (d)
                {
                    case 0x24F3: return "Intel Dual Band Wireless-AC 8260";
                    case 0x24FD: return "Intel Wireless-AC 8265";
                    case 0x095A: case 0x095B: return "Intel Wireless-AC 7265";
                    case 0x08B1: case 0x08B2: return "Intel Wireless-N/AC 7260";
                    case 0x3165: case 0x3166: return "Intel Wireless-AC 3165";
                    case 0x24FB: return "Intel Wireless-AC 3168";
                    case 0x2526: return "Intel Wireless-AC 9260";
                    case 0x9DF0: case 0xA370: case 0x30DC: case 0x31DC: return "Intel Wireless-AC 9560/9462";
                    case 0x2723: return "Intel Wi-Fi 6 AX200";
                    case 0x2725: return "Intel Wi-Fi 6E AX210";
                    case 0x51F0: case 0x7AF0: case 0x51F1: return "Intel Wi-Fi 6E AX211";
                    case 0x06F0: case 0x02F0: case 0xA0F0: case 0x4DF0: case 0x43F0: return "Intel Wi-Fi 6 AX201";
                    case 0x0082: case 0x0085: return "Intel Centrino Advanced-N 6205";
                    case 0x0088: case 0x0089: return "Intel Centrino Advanced-N 6235";
                    case 0x008A: case 0x008B: return "Intel Centrino Wireless-N 1030";
                    case 0x0887: case 0x0888: return "Intel Centrino Wireless-N 2230";
                    case 0x4232: case 0x4235: case 0x4236: case 0x4237: return "Intel WiFi Link 5100/5300";
                    case 0x272B: return "Intel Wi-Fi 7 BE200";
                }
                wifi = false;
                return null;
            }
            if (v == 0x10EC)
            {
                switch (d)
                {
                    case 0x8139: return "Realtek RTL8139";
                    case 0x8029: return "Realtek RTL8029 (NE2000)";
                    case 0x8168: return "Realtek RTL8111/8168 Gigabit";
                    case 0x8161: return "Realtek RTL8111 Gigabit";
                    case 0x8169: return "Realtek RTL8169 Gigabit";
                    case 0x8136: return "Realtek RTL8101/8102 Fast Ethernet";
                    case 0x8125: return "Realtek RTL8125 2.5G";
                }
                wifi = true;
                switch (d)
                {
                    case 0x8176: return "Realtek RTL8188CE Wi-Fi";
                    case 0x8179: return "Realtek RTL8188EE Wi-Fi";
                    case 0x818B: return "Realtek RTL8192EE Wi-Fi";
                    case 0xB822: return "Realtek RTL8822BE Wi-Fi";
                    case 0xC822: return "Realtek RTL8822CE Wi-Fi";
                    case 0xC821: return "Realtek RTL8821CE Wi-Fi";
                    case 0xB723: return "Realtek RTL8723BE Wi-Fi";
                    case 0xD723: return "Realtek RTL8723DE Wi-Fi";
                    case 0x8852: return "Realtek RTL8852AE Wi-Fi 6";
                    case 0xB852: return "Realtek RTL8852BE Wi-Fi 6";
                    case 0xC852: return "Realtek RTL8852CE Wi-Fi 6E";
                }
                wifi = false;
                return null;
            }
            if (v == 0x168C) { wifi = true; return "Qualcomm Atheros Wi-Fi (" + Fmt.Hex(d, 4) + ")"; }
            if (v == 0x17CB) { wifi = true; return "Qualcomm Wi-Fi (" + Fmt.Hex(d, 4) + ")"; }
            if (v == 0x14C3) { wifi = true; return "MediaTek Wi-Fi (" + Fmt.Hex(d, 4) + ")"; }
            if (v == 0x1814) { wifi = true; return "Ralink Wi-Fi (" + Fmt.Hex(d, 4) + ")"; }
            if (v == 0x14E4)
            {
                if ((d & 0xFF00) == 0x4300 || (d & 0xFF00) == 0x4400) { wifi = true; return "Broadcom Wi-Fi (BCM" + Fmt.Hex(d, 4) + ")"; }
                if ((d & 0xF000) == 0x1000) return "Broadcom NetXtreme Ethernet";
                return null;
            }
            if (v == 0x1969) return "Qualcomm Atheros / Killer Ethernet";
            if (v == 0x1022 && d == 0x2000) return "AMD PCnet-FAST III (79C973)";
            if (v == 0x1022 && d == 0x2001) return "AMD PCnet-Home";
            if (v == 0x1AF4 && (d == 0x1000 || d == 0x1041)) return "VirtIO Network";
            if (v == 0x15AD && d == 0x07B0) return "VMware VMXNET3";
            return null;
        }
    }
}
