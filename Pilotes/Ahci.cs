using System;
using System.Collections.Generic;
using Cosmos.HAL;
using Cosmos.HAL.BlockDevice;

namespace FileSystemOS.Pilotes
{
    /// <summary>
    /// PILOTE AHCI (disques SATA) ecrit pour File System OS.
    ///
    /// Inspire du pilote AHCI 1.3.1 du nouveau Cosmos (Cosmos.Kernel.Drivers.Pci.Storage.Ahci),
    /// mais reecrit pour l'API classique de cette version de Cosmos (BlockDevice + PCIDevice).
    ///
    /// Principe, comme pour le NVMe : tout passe par des structures en memoire.
    ///  - une LISTE DE COMMANDES de 32 entetes par port ;
    ///  - une ZONE DE RECEPTION ou le disque depose ses reponses (FIS) ;
    ///  - une TABLE par commande : le FIS a envoyer + la liste des zones memoire (PRDT).
    /// On remplit une entree, on met son bit dans le registre CI, et on attend qu'il retombe.
    /// Pilote volontairement en attente active (aucune interruption), une page par transfert.
    ///
    /// Important : il ne fonctionne QUE si le BIOS est en mode AHCI. En mode "RAID On"
    /// (frequent chez Dell), le controleur se presente autrement et le disque reste invisible.
    /// </summary>
    public unsafe class Ahci : BlockDevice
    {
        // --- Registres generaux (ABAR) ---
        private const int CAP = 0x00, GHC = 0x04, IS = 0x08, PI = 0x0C, VS = 0x10;
        private const uint GhcAhciEnable = 1u << 31, GhcReset = 1u << 0;

        // --- Registres d'un port (base 0x100 + numero * 0x80) ---
        private const int PortBase = 0x100, PortTaille = 0x80;
        private const int PxCLB = 0x00, PxCLBU = 0x04, PxFB = 0x08, PxFBU = 0x0C;
        private const int PxIS = 0x10, PxIE = 0x14, PxCMD = 0x18, PxTFD = 0x20;
        private const int PxSIG = 0x24, PxSSTS = 0x28, PxSCTL = 0x2C, PxSERR = 0x30, PxSACT = 0x34, PxCI = 0x38;
        private const uint IsErreurFichierTache = 1u << 30;     // PxIS.TFES : le disque a refuse la commande

        private const uint CmdST = 1u << 0, CmdFRE = 1u << 4, CmdFR = 1u << 14, CmdCR = 1u << 15;
        private const uint TfdBSY = 1u << 7, TfdDRQ = 1u << 3, TfdERR = 1u << 0;
        private const uint SignatureSata = 0x00000101;

        private const int Emplacements = 32;        // entetes de commande par port
        private const int TailleEntete = 32, TailleTable = 256, TaillePage = 4096;

        public static readonly List<Ahci> Disques = new List<Ahci>();
        public static string Journal = "";

        /// <summary>Faut-il confier a Cosmos les disques SANS partition FAT ? (risque de blocage)</summary>
        public static bool MonterTout;

        /// <summary>Disques reconnus mais volontairement caches a Cosmos (Windows, GPT...).</summary>
        public static readonly List<Ahci> Caches = new List<Ahci>();

        private readonly PCIDevice pci;
        private readonly byte* abar;
        private readonly uint port;
        private readonly List<byte[]> memoire = new List<byte[]>();   // garde la memoire vivante

        private byte* liste;        // liste de commandes (1 Ko)
        private byte* reception;    // zone de reception des FIS (256 o)
        private byte* table;        // table de la commande 0
        private byte* tampon;       // page de donnees (4 Ko)

        private ulong capacite;
        private bool pret;
        public string Modele = "SATA";
        public string Serie = "", Micrologiciel = "";
        public bool Lba48;
        public uint Commandes, Erreurs, Reparations, Vidages;

        public override BlockDeviceType Type => BlockDeviceType.HardDrive;
        public bool Pret => pret;
        public long Octets => (long)capacite * 512;
        public uint Numero => port;

        private Ahci(PCIDevice d, byte* bar, uint numeroPort)
        {
            pci = d; abar = bar; port = numeroPort;
            mBlockSize = 512;
        }

        // ---- Acces aux registres ----
        private uint Lire(int r) => *(uint*)(abar + r);
        private void Ecrire(int r, uint v) { *(uint*)(abar + r) = v; }
        private int Reg(int r) => PortBase + (int)port * PortTaille + r;
        private uint LireP(int r) => Lire(Reg(r));
        private void EcrireP(int r, uint v) => Ecrire(Reg(r), v);

        private static void Patienter(int ms)
        {
            try { Global.PIT.Wait((uint)ms); }
            catch (Exception) { for (int i = 0; i < ms * 20000; i++) { } }
        }

        /// <summary>Memoire alignee et gardee en vie (le ramasse-miettes ne doit pas la reprendre).</summary>
        private byte* Aligne(int taille, int alignement)
        {
            byte[] bloc = new byte[taille + alignement];
            memoire.Add(bloc);
            byte* p;
            fixed (byte* q = bloc) { p = q; }
            uint a = ((uint)p + (uint)alignement - 1) & ~(uint)(alignement - 1);
            byte* r = (byte*)a;
            for (int i = 0; i < taille; i++) r[i] = 0;
            return r;
        }

        /// <summary>Cherche les controleurs AHCI (classe 01, sous-classe 06) et leurs disques.</summary>
        public static int Detecter()
        {
            int n = 0;
            Journal = "";
            for (int i = 0; i < PCI.Devices.Count; i++)
            {
                PCIDevice d = PCI.Devices[i];
                if (d.ClassCode != 0x01) continue;
                if (d.Subclass == 0x04) { Journal += "controleur en mode RAID : passez le BIOS en AHCI. "; continue; }
                if (d.Subclass != 0x06) continue;

                try
                {
                    d.EnableMemory(true);
                    d.EnableBusMaster(true);
                    byte* bar = (byte*)(d.BaseAddressBar[5].BaseAddress & 0xFFFFFFF0);
                    if ((uint)bar == 0) { Journal += "BAR5 absent. "; continue; }

                    uint ghc = *(uint*)(bar + GHC);
                    if ((ghc & GhcAhciEnable) == 0) *(uint*)(bar + GHC) = ghc | GhcAhciEnable;
                    uint ports = *(uint*)(bar + PI);

                    for (uint p = 0; p < 32; p++)
                    {
                        if ((ports & (1u << (int)p)) == 0) continue;
                        var disque = new Ahci(d, bar, p);
                        if (!disque.Demarrer()) continue;
                        Disques.Add(disque);

                        // On regarde la table de partitions AVANT de le confier a Cosmos :
                        // un disque Windows (GPT/NTFS) bloquerait le montage.
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
                            // Disque Windows : sa partition FSOS (si elle existe) devient un lecteur
                            VueFsos.Proposer(disque, "sata" + (Disques.Count - 1));
                        }

                        Journal += (Journal.Length > 0 ? " ; " : "") + "port " + p + " : " + disque.Modele +
                                   " " + Utils.Fmt.Size(disque.Octets) + " - " +
                                   FileSystemOS.Systeme.Partitions.Nom(type) +
                                   (donner ? "" : " : garde de cote (montage risque)");
                        n++;
                    }
                }
                catch (Exception e) { Journal += (Journal.Length > 0 ? " ; " : "") + "AHCI : " + e.Message; }
            }
            if (n == 0 && Journal.Length == 0) Journal = "aucun disque SATA";
            return n;
        }

        // ---- Moteur de commandes du port ----
        private bool ArreterMoteur()
        {
            EcrireP(PxCMD, LireP(PxCMD) & ~CmdST);
            EcrireP(PxCMD, LireP(PxCMD) & ~CmdFRE);
            for (int i = 0; i < 1000; i++)
            {
                uint c = LireP(PxCMD);
                if ((c & CmdCR) == 0 && (c & CmdFR) == 0) return true;
                Patienter(1);
            }
            return false;
        }

        private bool DemarrerMoteur()
        {
            for (int i = 0; i < 1000; i++)
            {
                if ((LireP(PxCMD) & CmdCR) == 0) break;
                Patienter(1);
            }
            EcrireP(PxCMD, LireP(PxCMD) | CmdFRE);
            EcrireP(PxCMD, LireP(PxCMD) | CmdST);
            return true;
        }

        /// <summary>
        /// COMRESET : reinitialise le lien SATA d'un port que le BIOS a laisse eteint
        /// (DET = 1 pendant au moins 1 ms, puis DET = 0, et on attend que le disque reponde).
        /// </summary>
        private void Reveiller()
        {
            uint sctl = LireP(PxSCTL);
            EcrireP(PxSCTL, (sctl & ~0x0Fu) | 1u);
            Patienter(2);
            EcrireP(PxSCTL, sctl & ~0x0Fu);
            for (int i = 0; i < 50; i++)
            {
                if ((LireP(PxSSTS) & 0x0F) == 3) break;
                Patienter(1);
            }
            EcrireP(PxSERR, 0xFFFFFFFF);
        }

        /// <summary>Apres une erreur : moteur arrete, erreurs effacees, moteur relance.</summary>
        private void Reparer()
        {
            Reparations++;
            ArreterMoteur();
            EcrireP(PxSERR, 0xFFFFFFFF);
            EcrireP(PxIS, 0xFFFFFFFF);
            if ((LireP(PxTFD) & (TfdBSY | TfdDRQ)) != 0) Reveiller();
            DemarrerMoteur();
        }

        public bool Demarrer()
        {
            // 0. Port eteint par le BIOS ? On tente de reveiller le lien.
            if ((LireP(PxSSTS) & 0x0F) != 3) Reveiller();

            // 1. Un disque SATA est-il present sur ce port ?
            uint ssts = LireP(PxSSTS);
            if ((ssts & 0x0F) != 3) return false;                 // pas de peripherique detecte
            if (((ssts >> 8) & 0x0F) != 1) return false;          // interface endormie
            if (LireP(PxSIG) != SignatureSata) return false;      // CD/DVD ou autre : non gere

            // 2. Moteur a l'arret, puis zones memoire du port
            if (!ArreterMoteur()) return false;
            liste = Aligne(Emplacements * TailleEntete, 1024);
            reception = Aligne(256, 256);
            table = Aligne(TailleTable, 128);
            tampon = Aligne(TaillePage, TaillePage);

            EcrireP(PxCLB, (uint)liste);
            EcrireP(PxCLBU, 0);
            EcrireP(PxFB, (uint)reception);
            EcrireP(PxFBU, 0);
            EcrireP(PxSERR, 0xFFFFFFFF);     // on efface les erreurs memorisees
            EcrireP(PxIS, 0xFFFFFFFF);
            EcrireP(PxIE, 0);                // aucune interruption : on interroge

            // L'entete 0 pointe sur notre unique table de commandes
            uint* e = (uint*)liste;
            e[2] = (uint)table;
            e[3] = 0;

            if (!DemarrerMoteur()) return false;

            // 3. IDENTIFY : modele et nombre de secteurs
            if (!Commande(0xEC, 0, 1, false)) return false;
            ushort* id = (ushort*)tampon;
            Modele = Texte(id, 27, 40);
            Serie = Texte(id, 10, 20);
            Micrologiciel = Texte(id, 23, 8);
            Lba48 = (id[83] & (1 << 10)) != 0;
            ulong secteurs48 = ((ulong)id[103] << 48) | ((ulong)id[102] << 32) | ((ulong)id[101] << 16) | id[100];
            uint secteurs28 = ((uint)id[61] << 16) | id[60];
            capacite = secteurs48 > 0 ? secteurs48 : secteurs28;
            if (capacite == 0) return false;

            mBlockCount = capacite;
            pret = true;
            return true;
        }

        private static string Texte(ushort* mots, int debut, int longueur)
        {
            char[] c = new char[longueur];
            int k = 0;
            for (int i = 0; i < longueur / 2; i++)
            {
                ushort m = mots[debut + i];
                char a = (char)(m >> 8), b = (char)(m & 0xFF);
                if (a >= 32 && a < 127) c[k++] = a;
                if (b >= 32 && b < 127) c[k++] = b;
            }
            string s = new string(c, 0, k).Trim();
            return s.Length == 0 ? "SATA" : s;
        }

        /// <summary>
        /// Prepare la table de commande puis attend la fin.
        /// commande : 0xEC identify, 0x25 lecture, 0x35 ecriture (toutes en 48 bits).
        /// </summary>
        private bool Commande(byte commande, ulong lba, int secteurs, bool ecriture)
        {
            string avant = FileSystemOS.Noyau.Watchdog.Etape;
            FileSystemOS.Noyau.Watchdog.Etape = FileSystemOS.Noyau.Watchdog.EtapeDisque;
            FileSystemOS.Noyau.Watchdog.DisqueCommande = commande;
            FileSystemOS.Noyau.Watchdog.DisqueSecteur = lba;
            // Volontairement SANS try/finally : un "return" a l'interieur d'un try/finally est une
            // construction risquee pour le compilateur de Cosmos, et ce code tourne a chaque commande.
            bool ok;
            try { ok = CommandeBrute(commande, lba, secteurs, ecriture); }
            catch (Exception) { ok = false; }
            FileSystemOS.Noyau.Watchdog.Etape = avant;
            return ok;
        }

        private bool CommandeBrute(byte commande, ulong lba, int secteurs, bool ecriture)
        {
            bool ok = Executer(commande, lba, secteurs, ecriture, secteurs > 0);
            Commandes++;
            if (!ok)
            {
                Erreurs++;
                Reparer();
                ok = Executer(commande, lba, secteurs, ecriture, secteurs > 0);   // une seconde chance
                if (!ok) Erreurs++;
            }
            return ok;
        }

        private bool Executer(byte commande, ulong lba, int secteurs, bool ecriture, bool avecDonnees)
        {
            // Le disque doit etre libre
            for (int i = 0; i < 100000; i++)
            {
                if ((LireP(PxTFD) & (TfdBSY | TfdDRQ)) == 0) break;
                if (i == 99999) return false;
            }
            EcrireP(PxIS, 0xFFFFFFFF);
            EcrireP(PxSERR, 0xFFFFFFFF);

            int octets = secteurs * 512;

            // --- Entete de commande 0 ---
            uint* e = (uint*)liste;
            uint flags = 5;                                  // longueur du FIS : 5 mots de 32 bits
            if (ecriture) flags |= 1u << 6;                  // bit W : ecriture vers le disque
            if (avecDonnees) flags |= 1u << 16;              // une seule entree PRDT
            e[0] = flags;
            e[1] = 0;                                        // compteur d'octets transferes

            // --- Table : le FIS a envoyer, puis la zone memoire a utiliser ---
            for (int i = 0; i < TailleTable; i++) table[i] = 0;
            byte* fis = table;
            fis[0] = 0x27;                                   // FIS "registre, hote vers peripherique"
            fis[1] = 0x80;                                   // c'est une commande
            fis[2] = commande;
            fis[4] = (byte)lba; fis[5] = (byte)(lba >> 8); fis[6] = (byte)(lba >> 16);
            fis[7] = 0x40;                                   // adressage LBA
            fis[8] = (byte)(lba >> 24); fis[9] = (byte)(lba >> 32); fis[10] = (byte)(lba >> 40);
            fis[12] = (byte)secteurs; fis[13] = (byte)(secteurs >> 8);

            if (avecDonnees)
            {
                uint* prdt = (uint*)(table + 128);           // PRDT juste apres le FIS + ACMD
                prdt[0] = (uint)tampon;
                prdt[1] = 0;
                prdt[2] = 0;
                prdt[3] = (uint)(octets - 1);                // nombre d'octets moins un
            }

            // --- Lancement : on met le bit 0 de CI, et on attend qu'il retombe ---
            EcrireP(PxCI, 1);
            for (int i = 0; i < 2000000; i++)
            {
                if ((LireP(PxCI) & 1) == 0) break;
                if ((LireP(PxIS) & IsErreurFichierTache) != 0) return false;   // erreur signalee par le disque
                if (i == 1999999) return false;
            }
            if ((LireP(PxTFD) & TfdERR) != 0) return false;
            return true;
        }

        /// <summary>FLUSH CACHE EXT : force le SSD a ecrire ce qu'il garde dans son cache.</summary>
        public bool Vider()
        {
            if (!pret) return false;
            Vidages++;
            return Commande(0xEA, 0, 0, false);
        }

        public string Fiche =>
            Modele + "  serie " + Serie + "  micrologiciel " + Micrologiciel + "  " +
            Utils.Fmt.Size(Octets) + "  LBA" + (Lba48 ? "48" : "28") + "  port " + port + "  " +
            Commandes + " commandes, " + Erreurs + " erreur(s), " + Reparations + " reparation(s)";

        private const int SecteursParPage = TaillePage / 512;

        public override void ReadBlock(ulong lba, ulong count, ref byte[] data)
        {
            if (!pret) throw new Exception("disque SATA non pret");
            int fait = 0;
            while (fait < (int)count)
            {
                int n = (int)count - fait;
                if (n > SecteursParPage) n = SecteursParPage;
                if (!Commande(0x25, lba + (ulong)fait, n, false)) throw new Exception("lecture SATA echouee");
                int octets = n * 512;
                for (int i = 0; i < octets; i++) data[fait * 512 + i] = tampon[i];
                fait += n;
            }
        }

        public override void WriteBlock(ulong lba, ulong count, ref byte[] data)
        {
            if (!pret) throw new Exception("disque SATA non pret");
            int fait = 0;
            while (fait < (int)count)
            {
                int n = (int)count - fait;
                if (n > SecteursParPage) n = SecteursParPage;
                int octets = n * 512;
                for (int i = 0; i < octets; i++) tampon[i] = data[fait * 512 + i];
                if (!Commande(0x35, lba + (ulong)fait, n, true)) throw new Exception("ecriture SATA echouee");
                fait += n;
            }
        }
    }
}
