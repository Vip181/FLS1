using System;
using System.Collections.Generic;
using Cosmos.HAL;
using FileSystemOS.Systeme;

namespace FileSystemOS.Pilotes
{
    /// <summary>
    /// REGISTRE DES PILOTES RESEAU.
    ///
    /// A retenir : un pilote ne depend PAS de la marque de l'ordinateur (Dell, HP, ASUS...)
    /// mais de la PUCE reseau soudee dedans. Deux Dell peuvent avoir deux puces differentes,
    /// et un Dell et un HP peuvent avoir la meme. Ce registre associe donc chaque puce connue
    /// a son pilote, et dit si on peut la demarrer SANS RISQUE.
    ///
    /// Regle de securite : sur une vraie machine, on ne demarre QUE les puces testees.
    /// Une puce inconnue n'est jamais touchee (c'est ce qui faisait planter l'OS au demarrage).
    /// </summary>
    public static class PiloteReseau
    {
        public const int Aucun = 0, Cosmos = 1, Fsos = 2, Absent = 3, Essai = 4;

        public static string Mode = "auto";     // auto | tous | aucun  (reseau.cfg)
        public static string Journal = "";

        /// <summary>Pilote d'une puce : renvoie le texte affiche, ok = utilisable maintenant.</summary>
        public static string Pour(Peripherique p, out bool ok)
        {
            int kind = Type(p.Vendor, p.Device, out string nom);
            ok = kind == Cosmos || kind == Fsos;
            if (kind == Cosmos) return "pilote Cosmos (" + nom + ") - pret";
            if (kind == Fsos) return "pilote File System OS (" + nom + ") - pret";
            if (kind == Essai) return nom + " : cliquez sur \"Essayer le pilote I219\" (cable branche)";
            if (p.Wifi) return "Wi-Fi : aucun pilote possible (micrologiciel du fabricant + WPA2)";
            return nom + " : pas encore de pilote (branchez un cable sur une carte geree)";
        }

        /// <summary>Quel pilote gere cette puce ?</summary>
        public static int Type(ushort v, ushort d, out string nom)
        {
            nom = "inconnue";
            // --- Pilotes fournis par Cosmos ---
            if (v == 0x1022 && (d == 0x2000 || d == 0x2001)) { nom = "AMD PCnet"; return Cosmos; }
            if (v == 0x10EC && d == 0x8139) { nom = "Realtek RTL8139"; return Cosmos; }
            // --- Pilote ecrit pour File System OS ---
            if (E1000.IsSupported(v, d)) { nom = "Intel E1000"; return Fsos; }
            if (E1000.IsExperimental(v, d))
            {
                nom = "Intel I217/I218/I219 (experimental)";
                return E1000.Experimental ? Fsos : Essai;
            }

            // --- Puces reconnues mais sans pilote (on ne les touche surtout pas) ---
            if (v == 0x8086) { nom = "Intel e1000e / igc"; return Absent; }
            if (v == 0x10EC) { nom = "Realtek r8169"; return Absent; }
            if (v == 0x14E4) { nom = "Broadcom tg3"; return Absent; }
            if (v == 0x1969) { nom = "Qualcomm Atheros alx"; return Absent; }
            return Absent;
        }

        /// <summary>Profil de la machine : ce qui est demarrable ici.</summary>
        public static string Profil()
        {
            Machine.Detect();
            var cartes = PciCatalogue.Reseau();
            int pret = 0, sans = 0, wifi = 0, essai = 0;
            for (int i = 0; i < cartes.Count; i++)
            {
                if (cartes[i].Wifi) { wifi++; continue; }
                if (cartes[i].Supported) pret++;
                else if (Type(cartes[i].Vendor, cartes[i].Device, out string n2) == Essai) essai++;
                else sans++;
            }
            if (cartes.Count == 0) return "aucune carte reseau";
            return pret + " carte(s) pilotable(s), " + essai + " a essayer, " + sans + " sans pilote, " + wifi + " Wi-Fi" +
                   (Machine.Virtuel ? "  (machine virtuelle : demarrage automatique)"
                                    : "  (machine physique : demarrage des cartes pilotables uniquement)");
        }

        /// <summary>
        /// Demarre les pilotes possibles. Rien n'est tente sur une puce sans pilote :
        /// c'est ce qui garantit que l'OS ne plante plus au demarrage sur un vrai PC.
        /// </summary>
        public static int Demarrer()
        {
            Machine.Detect();
            Journal = "";
            if (Mode == "aucun") { Journal = "pilotes reseau desactives (reseau.cfg)"; return 0; }

            var cartes = PciCatalogue.Reseau();
            int lances = 0, ignores = 0;
            for (int i = 0; i < cartes.Count; i++)
            {
                Peripherique c = cartes[i];
                int kind = Type(c.Vendor, c.Device, out string nom);

                if (kind == Fsos)
                {
                    try { lances += E1000.DetectOne(c.Vendor, c.Device); }
                    catch (Exception e) { Journal += " [E1000 : " + e.Message + "]"; }
                }
                else if (kind == Cosmos) lances++;      // Cosmos les enregistre lui-meme
                else ignores++;
            }
            Journal = lances + " pilote(s) actif(s), " + ignores + " carte(s) laissee(s) de cote";
            return lances;
        }
    }
}
