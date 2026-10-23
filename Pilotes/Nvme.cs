using System;
using System.Collections.Generic;
using Cosmos.HAL;
using Cosmos.HAL.BlockDevice;

namespace FileSystemOS.Pilotes
{
    /// <summary>
    /// PILOTE NVMe (disques SSD de nouvelle generation, M.2 / PCIe) ecrit pour File System OS.
    ///
    /// NVMe fonctionne par FILES D'ATTENTE en memoire :
    ///  - on ecrit une commande de 64 octets dans la file d'envoi (SQ) ;
    ///  - on previent le disque en ecrivant dans une "sonnette" (doorbell) ;
    ///  - le disque repond dans la file de reponses (CQ), reconnue par son bit de phase.
    /// On cree une file d'administration (reglages) puis une file d'entrees/sorties (lecture/ecriture).
    ///
    /// Volontairement simple : une page de 4 Ko par commande, attente active (pas d'interruption).
    /// C'est plus lent qu'un vrai pilote, mais c'est sur et previsible.
    /// </summary>
    public unsafe class Nvme : BlockDevice
    {
        // Registres du controleur (BAR0)
        private const int CAP = 0x00, VS = 0x08, INTMS = 0x0C, INTMC = 0x10;
        private const int CC = 0x14, CSTS = 0x1C, AQA = 0x24, ASQ = 0x28, ACQ = 0x30;

        private const int QueueSize = 8;        // entrees par file (petit = peu de memoire)
        private const int PageSize = 4096;

        public static readonly List<Nvme> Disques = new List<Nvme>();
        public static readonly List<Nvme> Caches = new List<Nvme>();
        public static string Journal = "";
        public static bool MonterTout;

        private readonly PCIDevice pci;
        private readonly byte* regs;
        private readonly int doorbellStride;

        // Files : administration (0) et entrees/sorties (1)
        private byte* asq, acq, iosq, iocq, buffer;
        private readonly List<byte[]> memoire = new List<byte[]>();   // garde la memoire vivante
        private int asqTail, acqHead, iosqTail, iocqHead;
        private bool asqPhase = true, iocqPhase = true;

        private uint nsid = 1;
        private ulong capacite;                 // en blocs
        private uint tailleBloc = 512;
        private bool pret;
        public string Modele = "NVMe";

        public override BlockDeviceType Type => BlockDeviceType.HardDrive;
        public bool Pret => pret;
        public long Octets => (long)capacite * tailleBloc;

        private Nvme(PCIDevice d)
        {
            pci = d;
            regs = (byte*)(d.BaseAddressBar[0].BaseAddress & 0xFFFFFFF0);
            ulong cap = Read64(CAP);
            doorbellStride = 4 << (int)((cap >> 32) & 0xF);
        }

        // ---- Acces aux registres ----
        private uint Read32(int r) => *(uint*)(regs + r);
        private void Write32(int r, uint v) { *(uint*)(regs + r) = v; }
        private ulong Read64(int r) => *(ulong*)(regs + r);
        private void Write64(int r, ulong v) { *(ulong*)(regs + r) = v; }
        private void Sonnette(int queue, bool reponses, uint valeur) =>
            Write32(0x1000 + (2 * queue + (reponses ? 1 : 0)) * doorbellStride, valeur);

        /// <summary>Memoire alignee sur une page, gardee en vie (le ramasse-miettes n'y touche pas).</summary>
        private byte* Page(int taille)
        {
            byte[] bloc = new byte[taille + PageSize];
            memoire.Add(bloc);
            byte* p;
            fixed (byte* q = bloc) { p = q; }
            uint a = ((uint)p + PageSize - 1) & ~(uint)(PageSize - 1);
            byte* r = (byte*)a;
            for (int i = 0; i < taille; i++) r[i] = 0;
            return r;
        }

        /// <summary>Cherche les disques NVMe sur le bus PCI (classe 01, sous-classe 08).</summary>
        public static int Detecter()
        {
            int n = 0;
            Journal = "";
            for (int i = 0; i < PCI.Devices.Count; i++)
            {
                PCIDevice d = PCI.Devices[i];
                if (d.ClassCode != 0x01 || d.Subclass != 0x08) continue;
                try
                {
                    var disque = new Nvme(d);
                    if (disque.Demarrer())
                    {
                        Disques.Add(disque);
                        int type = FileSystemOS.Systeme.Partitions.Analyser(disque, out string detail);
                        bool donner = MonterTout || type == FileSystemOS.Systeme.Partitions.Fat ||
                                      type == FileSystemOS.Systeme.Partitions.Aucune;
                        if (donner)
                        {
                            if (!BlockDevice.Devices.Contains(disque)) BlockDevice.Devices.Add(disque);
                        }
                        else
                        {
                            Caches.Add(disque);
                            VueFsos.Proposer(disque, "nvme" + (Disques.Count - 1));
                        }
                        Journal += (Journal.Length > 0 ? " ; " : "") + disque.Modele + " " + Utils.Fmt.Size(disque.Octets) +
                                   " - " + FileSystemOS.Systeme.Partitions.Nom(type) +
                                   (donner ? "" : " : garde de cote (montage risque)");
                        n++;
                    }
                }
                catch (Exception e) { Journal += (Journal.Length > 0 ? " ; " : "") + "NVMe : " + e.Message; }
            }
            if (n == 0 && Journal.Length == 0) Journal = "aucun disque NVMe";
            return n;
        }

        private bool Attendre(bool pret_attendu)
        {
            for (int i = 0; i < 2000; i++)
            {
                bool rdy = (Read32(CSTS) & 1) != 0;
                if ((Read32(CSTS) & 2) != 0) throw new Exception("le controleur signale une panne");
                if (rdy == pret_attendu) return true;
                Global.PIT.Wait(1);
            }
            return false;
        }

        public bool Demarrer()
        {
            pci.EnableMemory(true);
            pci.EnableBusMaster(true);

            // 1. Arreter le controleur
            Write32(CC, Read32(CC) & ~1u);
            if (!Attendre(false)) throw new Exception("le controleur ne s'arrete pas");

            // 2. Files d'administration
            asq = Page(QueueSize * 64);
            acq = Page(QueueSize * 16);
            buffer = Page(PageSize);
            Write32(AQA, (uint)((QueueSize - 1) | ((QueueSize - 1) << 16)));
            Write64(ASQ, (uint)asq);
            Write64(ACQ, (uint)acq);

            // 3. Demarrer : pages de 4 Ko, commandes de 64 o, reponses de 16 o
            uint cc = (6u << 16) | (4u << 20) | (0u << 7) | (0u << 11) | 1u;
            Write32(CC, cc);
            if (!Attendre(true)) throw new Exception("le controleur ne demarre pas");

            // 4. Identifier le controleur (modele) puis l'espace de noms (capacite)
            Admin(0x06, 0, (uint)buffer, 1, 0);          // CNS 1 = controleur
            Modele = Texte(buffer + 24, 40);
            Admin(0x06, nsid, (uint)buffer, 0, 0);       // CNS 0 = espace de noms
            capacite = *(ulong*)buffer;                  // NSZE
            byte flbas = (byte)(buffer[26] & 0x0F);
            uint lbaf = *(uint*)(buffer + 128 + flbas * 4);
            int lbads = (int)((lbaf >> 16) & 0xFF);
            tailleBloc = lbads >= 9 && lbads <= 16 ? (uint)(1 << lbads) : 512;
            if (capacite == 0) throw new Exception("espace de noms vide");

            // 5. Files d'entrees/sorties (numero 1)
            iocq = Page(QueueSize * 16);
            iosq = Page(QueueSize * 64);
            Admin(0x05, 0, (uint)iocq, (uint)(QueueSize - 1) << 16 | 1u, 1u);          // creer CQ
            Admin(0x01, 0, (uint)iosq, (uint)(QueueSize - 1) << 16 | 1u, (1u << 16) | 1u); // creer SQ

            mBlockSize = tailleBloc;
            mBlockCount = capacite;
            pret = true;
            return true;
        }

        private static string Texte(byte* p, int n)
        {
            char[] c = new char[n];
            int k = 0;
            for (int i = 0; i < n; i++)
            {
                byte b = p[i];
                if (b >= 32 && b < 127) c[k++] = (char)b;
            }
            string s = new string(c, 0, k).Trim();
            return s.Length == 0 ? "NVMe" : s;
        }

        /// <summary>Envoie une commande d'administration et attend la reponse.</summary>
        private void Admin(byte opcode, uint ns, uint prp1, uint cdw10, uint cdw11)
        {
            byte* cmd = asq + asqTail * 64;
            for (int i = 0; i < 64; i++) cmd[i] = 0;
            cmd[0] = opcode;
            *(uint*)(cmd + 4) = ns;
            *(ulong*)(cmd + 24) = prp1;
            *(uint*)(cmd + 40) = cdw10;
            *(uint*)(cmd + 44) = cdw11;
            *(ushort*)(cmd + 2) = (ushort)asqTail;      // identifiant de commande

            asqTail = (asqTail + 1) % QueueSize;
            Sonnette(0, false, (uint)asqTail);
            Reponse(acq, ref acqHead, ref asqPhase, 0);
        }

        /// <summary>Attend une reponse dans une file et verifie le code d'erreur.</summary>
        private void Reponse(byte* cq, ref int head, ref bool phase, int queue)
        {
            for (int tour = 0; tour < 2000000; tour++)
            {
                byte* entree = cq + head * 16;
                ushort statut = *(ushort*)(entree + 14);
                bool p = (statut & 1) != 0;
                if (p == phase)
                {
                    int code = statut >> 1;
                    head = (head + 1) % QueueSize;
                    if (head == 0) phase = !phase;
                    Sonnette(queue, true, (uint)head);
                    if (code != 0) throw new Exception("erreur NVMe " + Utils.Fmt.Hex((uint)code, 4));
                    return;
                }
            }
            throw new Exception("le disque ne repond pas");
        }

        /// <summary>Lecture (opcode 2) ou ecriture (opcode 1) d'UNE page au maximum.</summary>
        private void Transfert(byte opcode, ulong lba, int blocs)
        {
            byte* cmd = iosq + iosqTail * 64;
            for (int i = 0; i < 64; i++) cmd[i] = 0;
            cmd[0] = opcode;
            *(uint*)(cmd + 4) = nsid;
            *(ulong*)(cmd + 24) = (uint)buffer;
            *(ulong*)(cmd + 40) = lba;
            *(ushort*)(cmd + 48) = (ushort)(blocs - 1);
            *(ushort*)(cmd + 2) = (ushort)iosqTail;

            iosqTail = (iosqTail + 1) % QueueSize;
            Sonnette(1, false, (uint)iosqTail);
            Reponse(iocq, ref iocqHead, ref iocqPhase, 1);
        }

        private int BlocsParPage => (int)(PageSize / tailleBloc);

        public override void ReadBlock(ulong lba, ulong count, ref byte[] data)
        {
            if (!pret) throw new Exception("disque NVMe non pret");
            int fait = 0;
            while (fait < (int)count)
            {
                int n = (int)count - fait;
                if (n > BlocsParPage) n = BlocsParPage;
                Transfert(0x02, lba + (ulong)fait, n);
                int octets = n * (int)tailleBloc;
                for (int i = 0; i < octets; i++) data[fait * tailleBloc + i] = buffer[i];
                fait += n;
            }
        }

        public override void WriteBlock(ulong lba, ulong count, ref byte[] data)
        {
            if (!pret) throw new Exception("disque NVMe non pret");
            int fait = 0;
            while (fait < (int)count)
            {
                int n = (int)count - fait;
                if (n > BlocsParPage) n = BlocsParPage;
                int octets = n * (int)tailleBloc;
                for (int i = 0; i < octets; i++) buffer[i] = data[fait * tailleBloc + i];
                Transfert(0x01, lba + (ulong)fait, n);
                fait += n;
            }
        }
    }
}
