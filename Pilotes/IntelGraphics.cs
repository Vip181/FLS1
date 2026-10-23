using System;
using System.Collections.Generic;
using Cosmos.HAL;
using FileSystemOS.Utils;

namespace FileSystemOS.Pilotes
{
    /// <summary>
    /// PILOTE D'AFFICHAGE INTEL (HD Graphics, generations 6 a 9).
    ///
    /// Ce qu'il fait reellement :
    ///  - lit l'etat des prises (SDEISR) : quelles sorties ont un ecran branche ;
    ///  - lit la configuration mise en place par le BIOS : quels "pipes" sont allumes,
    ///    a quelle taille, et quelle prise (DDI) chaque pipe alimente ;
    ///  - lit l'EDID de l'ecran branche par le bus I2C du chipset (GMBUS) : fabricant,
    ///    modele, taille, et resolution preferee.
    ///
    /// Ce qu'il ne fait PAS, et pourquoi : allumer une sortie eteinte. Il faudrait programmer
    /// les boucles a verrouillage de phase (PLL) qui fabriquent l'horloge pixel, les tables de
    /// temps d'affichage, l'encodeur TMDS du HDMI, le plan d'image et son adresse memoire, puis
    /// la sequence d'allumage dans le bon ordre - une erreur d'un bit eteint l'ecran du portable.
    /// Ce pilote ne fait donc que LIRE, sauf pour le GMBUS qui exige d'ecrire une adresse I2C.
    /// </summary>
    public static class IntelGraphics
    {
        // Registres (dans la fenetre memoire BAR0 de la carte)
        private const int SDEISR = 0xC4000;         // etat des prises (chipset)
        private const int GMBUS0 = 0x5100, GMBUS1 = 0x5104, GMBUS2 = 0x5108, GMBUS3 = 0x510C;
        private const int PIPEACONF = 0x70008, PIPEBCONF = 0x71008, PIPECCONF = 0x72008;
        private const int PIPEASRC = 0x6001C, PIPEBSRC = 0x6101C, PIPECSRC = 0x6201C;
        private const int DDI_BUF_CTL_A = 0x64000;  // puis +0x100 par prise

        private const uint PipeActif = 1u << 31;
        private const uint GmbusActif = 1u << 15, GmbusPret = 1u << 11, GmbusErreur = 1u << 10;

        public static string Carte = "inconnue";
        public static string Etat = "";
        public static readonly List<string> Ecrans = new List<string>();

        private static PCIDevice carte;
        private static unsafe byte* mmio;

        private static unsafe bool Ouvrir()
        {
            if (mmio != null) return true;
            for (int i = 0; i < PCI.Devices.Count; i++)
            {
                PCIDevice d = PCI.Devices[i];
                if (d.ClassCode != 0x03 || d.VendorID != 0x8086) continue;
                uint bar = d.BaseAddressBar[0].BaseAddress & 0xFFFFFFF0;
                if (bar == 0) continue;
                carte = d;
                mmio = (byte*)bar;
                Carte = "Intel " + Fmt.Hex(d.DeviceID, 4);
                return true;
            }
            Etat = "aucune carte graphique Intel";
            return false;
        }

        private static unsafe uint Lire(int r) => *(uint*)(mmio + r);
        private static unsafe void Ecrire(int r, uint v) { *(uint*)(mmio + r) = v; }

        private static void Attendre(int ms)
        {
            try { Global.PIT.Wait((uint)ms); }
            catch (Exception) { for (int i = 0; i < ms * 20000; i++) { } }
        }

        // ---------- EDID par le bus I2C du chipset (GMBUS) ----------

        /// <summary>
        /// Lit les 128 octets d'EDID d'un ecran. 'broche' est le numero de paire I2C du chipset :
        /// 1 = DAC/VGA, 2 = prise B, 3 = prise C, 4 = prise D.
        /// </summary>
        private static byte[] LireEdid(uint broche)
        {
            byte[] edid = new byte[128];
            Ecrire(GMBUS0, broche);                             // choix de la paire, vitesse 100 kHz
            Ecrire(GMBUS1, (1u << 31) | (1u << 30) | (128u << 16) | (0x50u << 1) | 1u);  // lire 128 o a l'adresse 0x50

            int lus = 0;
            for (int tour = 0; tour < 2000 && lus < 128; tour++)
            {
                uint etat = Lire(GMBUS2);
                if ((etat & GmbusErreur) != 0) { Ecrire(GMBUS1, 0); Ecrire(GMBUS0, 0); return null; }
                if ((etat & GmbusPret) == 0) { Attendre(1); continue; }

                uint mot = Lire(GMBUS3);
                for (int o = 0; o < 4 && lus < 128; o++) edid[lus++] = (byte)(mot >> (o * 8));
            }
            Ecrire(GMBUS1, 0);
            Ecrire(GMBUS0, 0);

            if (lus < 128) return null;
            if (edid[0] != 0x00 || edid[1] != 0xFF || edid[7] != 0x00) return null;   // signature EDID
            return edid;
        }

        /// <summary>Fabricant de l'ecran : trois lettres codees sur 5 bits chacune.</summary>
        private static string Fabricant(byte[] e)
        {
            int v = (e[8] << 8) | e[9];
            char a = (char)('A' + ((v >> 10) & 0x1F) - 1);
            char b = (char)('A' + ((v >> 5) & 0x1F) - 1);
            char c = (char)('A' + (v & 0x1F) - 1);
            return "" + a + b + c;
        }

        /// <summary>Nom du modele, inscrit dans un des quatre descripteurs.</summary>
        private static string Modele(byte[] e)
        {
            for (int d = 0; d < 4; d++)
            {
                int o = 54 + d * 18;
                if (e[o] != 0 || e[o + 1] != 0 || e[o + 3] != 0xFC) continue;
                char[] c = new char[13];
                int k = 0;
                for (int i = 0; i < 13; i++)
                {
                    byte b = e[o + 5 + i];
                    if (b == 0x0A) break;
                    if (b >= 32 && b < 127) c[k++] = (char)b;
                }
                return new string(c, 0, k).Trim();
            }
            return "";
        }

        /// <summary>Resolution preferee : premier descripteur de temps d'affichage.</summary>
        private static string Preferee(byte[] e)
        {
            int o = 54;
            int largeur = e[o + 2] | ((e[o + 4] & 0xF0) << 4);
            int hauteur = e[o + 5] | ((e[o + 7] & 0xF0) << 4);
            int pixel = (e[o] | (e[o + 1] << 8));               // en dizaines de kHz
            if (largeur < 320 || hauteur < 200) return "";
            int hz = 0;
            int total_h = largeur + (e[o + 3] | ((e[o + 4] & 0x0F) << 8));
            int total_v = hauteur + (e[o + 6] | ((e[o + 7] & 0x0F) << 8));
            if (total_h > 0 && total_v > 0) hz = pixel * 10000 / (total_h * total_v);
            return largeur + "x" + hauteur + (hz > 0 ? " a " + hz + " Hz" : "");
        }

        private static string Pouces(byte[] e)
        {
            int l = e[21], h = e[22];                            // en centimetres
            if (l == 0 || h == 0) return "";
            int carre = l * l + h * h;
            int d = 1;
            while ((d + 1) * (d + 1) <= carre) d++;      // racine carree entiere (sans flottant)
            return (d * 10 / 25) + " pouces";            // centimetres -> pouces
        }

        // ---------- Etat des pipes mis en place par le BIOS ----------

        private static string Pipe(string nom, int conf, int src)
        {
            uint c = Lire(conf);
            if ((c & PipeActif) == 0) return "pipe " + nom + " : eteint";
            uint s = Lire(src);
            int l = (int)((s >> 16) & 0xFFF) + 1, h = (int)(s & 0xFFF) + 1;
            return "pipe " + nom + " : allume, " + l + "x" + h;
        }

        /// <summary>Analyse complete. Les ecritures se limitent au bus I2C de lecture d'EDID.</summary>
        public static unsafe List<string> Analyser(bool lireEdid)
        {
            Ecrans.Clear();
            var r = new List<string>();
            if (!Ouvrir()) { r.Add(Etat); return r; }

            r.Add("Carte : " + Carte + "   registres a 0x" + Fmt.Hex((uint)mmio));

            // 1. Prises occupees
            uint sde = Lire(SDEISR);
            r.Add("Prises (SDEISR 0x" + Fmt.Hex(sde) + ") :");
            bool[] branche = new bool[5];
            branche[0] = (sde & (1u << 24)) != 0;   // A : ecran du portable
            branche[1] = (sde & (1u << 21)) != 0;   // B
            branche[2] = (sde & (1u << 22)) != 0;   // C
            branche[3] = (sde & (1u << 23)) != 0;   // D
            branche[4] = (sde & (1u << 25)) != 0;   // E
            string[] noms = { "A (ecran interne)", "B (HDMI/DP)", "C (HDMI/DP)", "D (HDMI/DP)", "E (VGA/station)" };
            for (int i = 0; i < 5; i++)
                r.Add("  " + (branche[i] ? "[branche] " : "[  vide  ] ") + noms[i]);

            // 2. Pipes allumes par le BIOS
            r.Add("Sorties allumees par le BIOS :");
            r.Add("  " + Pipe("A", PIPEACONF, PIPEASRC));
            r.Add("  " + Pipe("B", PIPEBCONF, PIPEBSRC));
            r.Add("  " + Pipe("C", PIPECCONF, PIPECSRC));
            for (int i = 0; i < 5; i++)
            {
                uint ddi = Lire(DDI_BUF_CTL_A + i * 0x100);
                if ((ddi & (1u << 31)) != 0) r.Add("  prise " + (char)('A' + i) + " : signal actif (ecran alimente)");
            }

            // 3. EDID des prises numeriques branchees
            if (lireEdid)
            {
                for (int i = 1; i <= 3; i++)
                {
                    if (!branche[i]) continue;
                    byte[] e = null;
                    try { e = LireEdid((uint)(i + 1)); } catch (Exception ex) { r.Add("  EDID prise " + (char)('A' + i) + " : " + ex.Message); }
                    if (e == null) { r.Add("  EDID prise " + (char)('A' + i) + " : illisible"); continue; }
                    string ligne = "Ecran sur la prise " + (char)('A' + i) + " : " + Fabricant(e) +
                                   " " + Modele(e) + "  " + Preferee(e) + "  " + Pouces(e);
                    r.Add(ligne);
                    Ecrans.Add(ligne);
                }
            }

            r.Add("Pour utiliser un ecran HDMI : branchez-le AVANT d'allumer le PC (le BIOS allume");
            r.Add("alors les deux sorties en mode clone). Allumer une sortie depuis l'OS demanderait");
            r.Add("de programmer les PLL, l'encodeur TMDS et le plan d'image : une erreur d'un bit");
            r.Add("eteint l'ecran du portable, et cela ne peut pas s'ecrire sans materiel de test.");
            return r;
        }
    }
}
