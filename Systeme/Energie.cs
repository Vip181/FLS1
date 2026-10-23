using System;
using System.Collections.Generic;
using FileSystemOS.Pilotes;
using FileSystemOS.Processus;
using FileSystemOS.Utils;

namespace FileSystemOS.Systeme
{
    /// <summary>
    /// ENERGIE : ce que l'OS peut reellement savoir de l'alimentation.
    ///
    /// Honnetement : le niveau de batterie se lit par l'ACPI, qui demande un interpreteur du
    /// langage AML (les tables de la carte mere sont de petits programmes a executer).
    /// Cosmos n'en a pas, donc le POURCENTAGE n'est pas accessible.
    /// Ce qui est accessible et affiche ici : la presence d'un controleur de batterie sur le bus,
    /// la duree de fonctionnement, et la consommation du systeme (temps processeur occupe).
    /// </summary>
    public static class Energie
    {
        public static bool Portable;
        public static string Controleur = "non detecte";

        public static void Detecter()
        {
            if (PciCatalogue.Tous.Count == 0) PciCatalogue.Scan();
            Portable = false;
            for (int i = 0; i < PciCatalogue.Tous.Count; i++)
            {
                Peripherique p = PciCatalogue.Tous[i];
                // Le controleur SMBus relie la batterie intelligente a la carte mere
                if (p.Class == 0x0C && p.Sub == 0x05) { Controleur = p.VendorName + " SMBus [" + p.Id + "]"; Portable = true; }
            }
            if (!Portable && Machine.Marque != "inconnue" && !Machine.Virtuel) Portable = true;
        }

        public static uint Secondes => Horloge.Millis / 1000;

        /// <summary>Part du temps passee a travailler (le reste, le systeme attend).</summary>
        public static int Charge()
        {
            var s = Services.Scheduler;
            if (s == null || Horloge.Millis == 0) return 0;
            uint pct = s.TempsOccupe * 100 / (Horloge.Millis > 0 ? Horloge.Millis : 1);
            return pct > 100 ? 100 : (int)pct;
        }

        public static List<string> Etat()
        {
            Detecter();
            var r = new List<string>();
            uint s = Secondes;
            r.Add("Allume depuis    : " + (s / 3600) + " h " + Fmt.Two((int)(s / 60 % 60)) + " min " + Fmt.Two((int)(s % 60)) + " s");
            r.Add("Charge systeme   : " + Charge() + " %   (" + Services.Fps + " images/s, " +
                  (Services.Scheduler == null ? 0 : Services.Scheduler.Commutations) + " commutations)");
            r.Add("Machine          : " + (Machine.Virtuel ? "virtuelle (pas de batterie)" :
                                           (Portable ? "portable " + Machine.Marque : "fixe")));
            r.Add("Controleur       : " + Controleur);
            r.Add("Niveau de batterie : non disponible");
            r.Add("  La charge se lit par l'ACPI, qui exige un interpreteur du langage AML.");
            r.Add("  Cosmos n'en fournit pas : aucun pilote ne peut donner le pourcentage.");
            r.Add("  Surveillez le voyant de charge de l'ordinateur.");
            return r;
        }
    }
}
