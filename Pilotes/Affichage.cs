using System;
using System.Collections.Generic;
using Cosmos.HAL;
using FileSystemOS.Utils;

namespace FileSystemOS.Pilotes
{
    /// <summary>
    /// SORTIES VIDEO (ecran interne, HDMI, DisplayPort, VGA).
    ///
    /// Ce que File System OS peut faire :
    ///  - trouver la ou les cartes graphiques sur le bus PCI ;
    ///  - sur les puces Intel (gen 6 a 9 : HD Graphics des Core i de 2e a 9e generation),
    ///    LIRE le registre de detection de branchement du chipset (SDEISR) pour savoir
    ///    quelles prises sont occupees. C'est une lecture seule : rien n'est programme.
    ///
    /// Ce qui n'est pas possible : allumer un deuxieme bureau sur l'ecran HDMI.
    /// Cosmos dessine dans UN seul cadre memoire, celui que le chargeur de demarrage a prepare
    /// (VBE). Pour piloter une deuxieme sortie il faudrait un vrai pilote graphique : horloges,
    /// pipes, encodeurs, lecture de l'EDID de l'ecran... c'est le plus gros pilote d'un systeme.
    /// En revanche, le BIOS recopie souvent l'image sur la prise HDMI (mode "clone") : dans ce
    /// cas le second ecran affiche la meme chose, sans que l'OS ait rien a faire.
    /// </summary>
    public static class Affichage
    {
        private const int SDEISR = 0xC4000;        // registre d'etat du chipset (lecture seule)

        public class Sortie
        {
            public string Nom;
            public bool Branchee;
            public string Remarque;
        }

        public static readonly List<Sortie> Sorties = new List<Sortie>();
        public static string Carte = "inconnue";
        public static string Methode = "";

        private static unsafe uint LireRegistre(PCIDevice d, int offset)
        {
            uint bar = d.BaseAddressBar[0].BaseAddress & 0xFFFFFFF0;
            if (bar == 0) return 0;
            return *(uint*)((byte*)bar + offset);
        }

        /// <summary>Cherche les sorties video. Lecture seule : aucun risque pour l'ecran en cours.</summary>
        public static void Detecter()
        {
            Sorties.Clear();
            Methode = "";
            if (PciCatalogue.Tous.Count == 0) PciCatalogue.Scan();

            PCIDevice carte = null;
            for (int i = 0; i < PCI.Devices.Count; i++)
            {
                PCIDevice d = PCI.Devices[i];
                if (d.ClassCode != 0x03) continue;
                carte = d;
                Carte = PciCatalogue.VendorName(d.VendorID) + " " + Fmt.Hex(d.DeviceID, 4);
                break;
            }

            // L'ecran utilise actuellement : toujours la, c'est celui que le chargeur a allume
            var principal = new Sortie();
            principal.Nom = "Ecran principal (" + Services.ScreenW + "x" + Services.ScreenH + ")";
            principal.Branchee = true;
            principal.Remarque = "affiche le bureau";
            Sorties.Add(principal);

            if (carte == null) { Methode = "aucune carte graphique sur le bus PCI"; return; }
            if (carte.VendorID != 0x8086)
            {
                Methode = "detection des prises non geree pour cette carte (" + Carte + ")";
                return;
            }

            // Intel : le registre SDEISR indique les prises occupees (bits 21 a 25)
            try
            {
                uint etat = LireRegistre(carte, SDEISR);
                if (etat == 0 || etat == 0xFFFFFFFF)
                {
                    Methode = "registre de detection illisible sur cette puce";
                    return;
                }
                Methode = "registre Intel SDEISR = 0x" + Fmt.Hex(etat);
                Ajouter("Prise A (eDP / ecran du portable)", (etat & (1u << 24)) != 0);
                Ajouter("Prise B (HDMI ou DisplayPort)", (etat & (1u << 21)) != 0);
                Ajouter("Prise C (HDMI ou DisplayPort)", (etat & (1u << 22)) != 0);
                Ajouter("Prise D (HDMI ou DisplayPort)", (etat & (1u << 23)) != 0);
                Ajouter("Prise E (VGA / station d'accueil)", (etat & (1u << 25)) != 0);
            }
            catch (Exception e) { Methode = "lecture impossible : " + e.Message; }
        }

        private static void Ajouter(string nom, bool branchee)
        {
            var s = new Sortie();
            s.Nom = nom;
            s.Branchee = branchee;
            s.Remarque = branchee ? "ecran detecte (recopie par le BIOS si le mode clone est actif)"
                                  : "rien de branche";
            Sorties.Add(s);
        }

        public static int Branchees()
        {
            int n = 0;
            for (int i = 1; i < Sorties.Count; i++) if (Sorties[i].Branchee) n++;
            return n;
        }

        public static List<string> Etat()
        {
            // Analyse complete par le pilote Intel (prises, pipes, EDID)
            try
            {
                var detail = IntelGraphics.Analyser(true);
                if (detail.Count > 2) return detail;
            }
            catch (Exception) { }

            Detecter();
            var r = new List<string>();
            r.Add("Carte graphique : " + Carte + "   " + Methode);
            for (int i = 0; i < Sorties.Count; i++)
                r.Add("  " + (Sorties[i].Branchee ? "[branche] " : "[  vide  ] ") + Sorties[i].Nom +
                      " - " + Sorties[i].Remarque);
            r.Add("Un second bureau sur HDMI demanderait un vrai pilote graphique (pipes, encodeurs,");
            r.Add("EDID, horloges) : Cosmos ne dessine que dans le cadre memoire prepare au demarrage.");
            r.Add("Si l'ecran HDMI reste noir : activez le mode clone / miroir dans le BIOS, ou");
            r.Add("branchez-le AVANT d'allumer l'ordinateur (le BIOS choisit alors les deux sorties).");
            return r;
        }
    }
}
