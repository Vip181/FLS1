using System;
using System.Collections.Generic;
using Cosmos.Core;
using Cosmos.Core.Memory;
using Cosmos.HAL;
using Cosmos.HAL.Network;

namespace FileSystemOS.Pilotes
{
    /// <summary>
    /// Pilote reseau Intel E1000 (PRO/1000) ecrit pour File System OS.
    /// Cartes : 82540EM (carte par defaut de VirtualBox « Intel PRO/1000 MT Desktop »),
    /// 82545EM (VMware), 82543GC, 82574L (QEMU « e1000e »).
    /// Il s'insere dans la pile reseau de Cosmos (DHCP, DNS, TCP fonctionnent dessus).
    /// Reception par interruption + interrogation de secours.
    /// </summary>
    public unsafe class E1000 : NetworkDevice
    {
        // Registres (MMIO, BAR0)
        private const int CTRL = 0x0000, STATUS = 0x0008, EERD = 0x0014, ICR = 0x00C0, IMS = 0x00D0, IMC = 0x00D8;
        private const int RCTL = 0x0100, TCTL = 0x0400, TIPG = 0x0410;
        private const int RDBAL = 0x2800, RDBAH = 0x2804, RDLEN = 0x2808, RDH = 0x2810, RDT = 0x2818;
        private const int TDBAL = 0x3800, TDBAH = 0x3804, TDLEN = 0x3808, TDH = 0x3810, TDT = 0x3818;
        private const int MTA = 0x5200, RAL = 0x5400, RAH = 0x5404;

        private const int RxCount = 32, TxCount = 16, BufSize = 2048;

        internal readonly PCIDevice pci;
        private readonly byte* mmio;
        private byte* rxDesc, txDesc;
        private byte*[] rxBuf, txBuf;
        private int rxCur, txCur;
        private readonly byte[] mac = new byte[6];
        private MACAddress macAddress;
        private bool ready;
        internal bool moderne;      // I217 / I218 / I219 : pas de remise a zero, pas d'EEPROM
        private readonly Queue<byte[]> received = new Queue<byte[]>();
        private readonly string name;

        public static readonly List<E1000> Cards = new List<E1000>();
        public uint Sent, Received;

        /// <summary>Cartes testees : pilote sur pour ces puces.</summary>
        public static bool IsSupported(ushort vendor, ushort device)
        {
            return vendor == 0x8086 && (device == 0x100E || device == 0x100F || device == 0x1004 ||
                                        device == 0x100C || device == 0x1015 || device == 0x10D3);
        }

        /// <summary>
        /// Puces Intel I217 / I218 / I219 des portables (Dell, HP, Lenovo...).
        /// Elles utilisent les MEMES registres que l'E1000 pour l'emission et la reception,
        /// mais leur PHY est pilote par le micrologiciel de la carte mere : on ne doit donc
        /// PAS faire de remise a zero, sous peine de bloquer la machine.
        /// Mode EXPERIMENTAL : desactive par defaut, a activer dans la fenetre Reseau.
        /// </summary>
        public static bool Experimental;

        public static bool IsExperimental(ushort vendor, ushort device)
        {
            if (vendor != 0x8086) return false;
            switch (device)
            {
                case 0x153A: case 0x153B:                                   // I217-LM / I217-V
                case 0x155A: case 0x1559: case 0x15A0: case 0x15A1:          // I218
                case 0x15A2: case 0x15A3:
                case 0x156F: case 0x1570:                                    // I219-LM / I219-V
                case 0x15B7: case 0x15B8: case 0x15BB: case 0x15BC:
                case 0x15BD: case 0x15BE: case 0x15D7: case 0x15D8:
                case 0x15D6: case 0x15E3: case 0x0D4C: case 0x0D4D:
                case 0x0D4E: case 0x0D4F: case 0x15FB: case 0x15FC:
                case 0x1502: case 0x1503:                                    // 82579
                    return true;
                default: return false;
            }
        }

        public static bool Gerable(ushort vendor, ushort device) =>
            IsSupported(vendor, device) || (Experimental && IsExperimental(vendor, device));

        /// <summary>Cherche les cartes compatibles et cree un pilote pour chacune.</summary>
        public static int Detect() => DetectOne(0, 0);

        /// <summary>Demarre une puce precise (0,0 = toutes les puces compatibles).</summary>
        public static int DetectOne(ushort vendor, ushort device)
        {
            int n = 0;
            for (int i = 0; i < PCI.Devices.Count; i++)
            {
                PCIDevice d = PCI.Devices[i];
                if (!Gerable(d.VendorID, d.DeviceID)) continue;
                if (vendor != 0 && (d.VendorID != vendor || d.DeviceID != device)) continue;
                bool deja = false;
                for (int k = 0; k < Cards.Count; k++) if (Cards[k].pci == d) deja = true;
                if (deja) continue;
                try
                {
                    var card = new E1000(d, PciCatalogue.NicName(d.VendorID, d.DeviceID, out bool w) ?? "Intel E1000");
                    card.moderne = IsExperimental(d.VendorID, d.DeviceID);
                    if (card.Enable())
                    {
                        if (!NetworkDevice.Devices.Contains(card)) NetworkDevice.Devices.Add(card);
                        Cards.Add(card);
                        n++;
                    }
                }
                catch (Exception) { }
            }
            return n;
        }

        private E1000(PCIDevice device, string cardName) : base()
        {
            pci = device;
            name = cardName;
            uint bar = device.BaseAddressBar[0].BaseAddress & 0xFFFFFFF0;
            mmio = (byte*)bar;
        }

        // ---- Acces aux registres ----
        private uint Read(int reg) => *(uint*)(mmio + reg);
        private void Write(int reg, uint v) { *(uint*)(mmio + reg) = v; }

        /// <summary>Memoire alignee pour la carte (adresse physique = adresse, pas de pagination).</summary>
        // Les tampons de la carte restent references ici : le ramasse-miettes ne les touche pas
        private static readonly List<byte[]> keepAlive = new List<byte[]>();

        private static byte* AllocAligned(uint size, uint align)
        {
            byte[] block = new byte[size + align];
            keepAlive.Add(block);
            byte* p;
            fixed (byte* q = block) { p = q; }
            uint a = ((uint)p + align - 1) & ~(align - 1);
            byte* r = (byte*)a;
            for (uint i = 0; i < size; i++) r[i] = 0;
            return r;
        }

        private ushort EepromRead(byte addr)
        {
            Write(EERD, ((uint)addr << 8) | 1);
            for (int i = 0; i < 10000; i++)
            {
                uint v = Read(EERD);
                if ((v & (1 << 4)) != 0) return (ushort)(v >> 16);
            }
            return 0;
        }

        public override bool Enable()
        {
            if (ready) return true;
            pci.EnableMemory(true);
            pci.EnableBusMaster(true);

            // Remise a zero de la carte. INTERDIT sur I217/I218/I219 : leur PHY appartient au
            // micrologiciel de la carte mere, un reset bloque l'ordinateur.
            if (!moderne)
            {
                Write(CTRL, Read(CTRL) | (1u << 26));
                Cosmos.HAL.Global.PIT.Wait(10);
            }
            else Write(IMC, 0xFFFFFFFF);        // on coupe seulement les interruptions

            // Adresse MAC : registres RAL/RAH, sinon EEPROM
            uint ral = Read(RAL), rah = Read(RAH);
            if (ral != 0)
            {
                mac[0] = (byte)ral; mac[1] = (byte)(ral >> 8); mac[2] = (byte)(ral >> 16); mac[3] = (byte)(ral >> 24);
                mac[4] = (byte)rah; mac[5] = (byte)(rah >> 8);
            }
            else if (moderne)
            {
                throw new Exception("adresse MAC illisible (carte tenue par le micrologiciel)");
            }
            else
            {
                for (byte i = 0; i < 3; i++)
                {
                    ushort w = EepromRead(i);
                    mac[i * 2] = (byte)w; mac[i * 2 + 1] = (byte)(w >> 8);
                }
                Write(RAL, (uint)(mac[0] | (mac[1] << 8) | (mac[2] << 16) | (mac[3] << 24)));
                Write(RAH, (uint)(mac[4] | (mac[5] << 8)) | (1u << 31));
            }
            macAddress = new MACAddress(mac);

            // Lien actif (SLU) + vitesse automatique (ASDE). Sur I219, le lien est deja
            // etabli par le micrologiciel : on ne force rien.
            if (!moderne) Write(CTRL, (Read(CTRL) | (1u << 6) | (1u << 5)) & ~(1u << 3) & ~(1u << 31));
            for (int i = 0; i < 128; i++) Write(MTA + i * 4, 0);

            // ---- Reception : 32 descripteurs de 16 octets + 32 tampons de 2 Ko ----
            rxDesc = AllocAligned(RxCount * 16, 128);
            rxBuf = new byte*[RxCount];
            for (int i = 0; i < RxCount; i++)
            {
                rxBuf[i] = AllocAligned(BufSize, 16);
                *(ulong*)(rxDesc + i * 16) = (uint)rxBuf[i];
            }
            Write(RDBAL, (uint)rxDesc);
            Write(RDBAH, 0);
            Write(RDLEN, RxCount * 16);
            Write(RDH, 0);
            Write(RDT, RxCount - 1);
            rxCur = 0;
            // EN | SBP? non | BAM (diffusion) | tampons 2048 | SECRC (retire le CRC)
            Write(RCTL, (1u << 1) | (1u << 15) | (1u << 26));

            // ---- Emission : 16 descripteurs ----
            txDesc = AllocAligned(TxCount * 16, 128);
            txBuf = new byte*[TxCount];
            for (int i = 0; i < TxCount; i++)
            {
                txBuf[i] = AllocAligned(BufSize, 16);
                *(ulong*)(txDesc + i * 16) = (uint)txBuf[i];
                txDesc[i * 16 + 12] = 1;              // statut DD = libre
            }
            Write(TDBAL, (uint)txDesc);
            Write(TDBAH, 0);
            Write(TDLEN, TxCount * 16);
            Write(TDH, 0);
            Write(TDT, 0);
            txCur = 0;
            // EN | PSP | seuil de collision | distance de collision
            Write(TCTL, (1u << 1) | (1u << 3) | (0x0Fu << 4) | (0x40u << 12));
            Write(TIPG, 0x0060200A);

            // Interruptions : reception (RXT0, RXDMT0, RXO) + changement de lien
            Write(IMC, 0xFFFFFFFF);
            Read(ICR);
            try { INTs.SetIrqHandler(pci.InterruptLine, HandleIrq); } catch (Exception) { }
            Write(IMS, (1u << 7) | (1u << 6) | (1u << 4) | (1u << 2));

            ready = true;
            return true;
        }

        private void HandleIrq(ref INTs.IRQContext ctx)
        {
            uint cause = Read(ICR);   // la lecture acquitte l'interruption
            if ((cause & ((1u << 7) | (1u << 6) | (1u << 4))) != 0) Poll();
        }

        /// <summary>Recupere les paquets recus (appelee par interruption et par le thread reseau).</summary>
        public void Poll()
        {
            if (!ready) return;
            while (true)
            {
                byte* d = rxDesc + rxCur * 16;
                if ((d[12] & 1) == 0) break;                    // DD : rien de plus
                int len = d[8] | (d[9] << 8);
                bool eop = (d[12] & 2) != 0;
                if (len > 0 && len <= BufSize && eop)
                {
                    byte[] pkt = new byte[len];
                    byte* src = rxBuf[rxCur];
                    for (int i = 0; i < len; i++) pkt[i] = src[i];
                    Received++;
                    if (DataReceived != null) DataReceived(pkt);
                    else received.Enqueue(pkt);
                }
                d[12] = 0;
                Write(RDT, (uint)rxCur);
                rxCur = (rxCur + 1) % RxCount;
            }
        }

        public bool LinkUp => ready && (Read(STATUS) & 2) != 0;

        // ---- Interface NetworkDevice de Cosmos ----
        public override CardType CardType => CardType.Ethernet;
        public override MACAddress MACAddress => macAddress;
        public override string Name => name;
        public override bool Ready => ready;

        public override bool QueueBytes(byte[] buffer, int offset, int length)
        {
            if (!ready || length <= 0 || length > BufSize) return false;
            byte* d = txDesc + txCur * 16;
            for (int wait = 0; (d[12] & 1) == 0 && wait < 100000; wait++) { }   // attend un descripteur libre
            byte* dst = txBuf[txCur];
            for (int i = 0; i < length; i++) dst[i] = buffer[offset + i];
            d[8] = (byte)length; d[9] = (byte)(length >> 8);
            d[10] = 0;                         // CSO
            d[11] = (1 << 0) | (1 << 1) | (1 << 3);   // EOP | IFCS | RS
            d[12] = 0;                         // statut
            txCur = (txCur + 1) % TxCount;
            Write(TDT, (uint)txCur);
            Sent++;
            return true;
        }

        public override bool ReceiveBytes(byte[] buffer, int offset, int max)
        {
            Poll();
            if (received.Count == 0) return false;
            byte[] p = received.Dequeue();
            int n = p.Length < max ? p.Length : max;
            for (int i = 0; i < n; i++) buffer[offset + i] = p[i];
            return true;
        }

        public override byte[] ReceivePacket()
        {
            Poll();
            return received.Count > 0 ? received.Dequeue() : null;
        }

        public override int BytesAvailable()
        {
            Poll();
            return received.Count > 0 ? received.Peek().Length : 0;
        }

        public override bool IsSendBufferFull() => ready && (txDesc[txCur * 16 + 12] & 1) == 0;
        public override bool IsReceiveBufferFull() => false;

        public string MacText()
        {
            string s = "";
            for (int i = 0; i < 6; i++) s += (i > 0 ? ":" : "") + Utils.Fmt.Hex(mac[i], 2);
            return s;
        }
    }
}
