using System;
using System.Drawing;
using Cosmos.Core;
using Cosmos.System.Graphics;          // Canvas, Pen
using Cosmos.System.Graphics.Fonts;    // Font

namespace FileSystemOS.Systeme
{
    /// <summary>
    /// REDEMARRAGE ET ARRET FIABLES.
    ///
    /// Cosmos n'a qu'une methode (ACPI) et, sur beaucoup de vraies machines, elle ne fait rien :
    /// l'ecran reste bloque sur "Redemarrage en cours". On essaie donc plusieurs methodes, de la
    /// plus sure a la plus brutale :
    ///   1. le controleur clavier 8042 (port 0x64, commande 0xFE) : la methode des PC depuis 1984 ;
    ///   2. le registre de remise a zero PCI (port 0xCF9) : les chipsets Intel modernes ;
    ///   3. l'ACPI de Cosmos ;
    ///   4. en dernier recours, on repete les impulsions : certaines cartes meres ne
    ///      repondent qu'au bout de plusieurs essais.
    /// </summary>
    public static class Redemarrage
    {
        // Dans cette version de Cosmos, un port s'utilise comme une PROPRIETE :
        //   port.Byte = 0xFE;   ecrit un octet        byte v = port.Byte;   lit un octet
        //   port.Word = 0x2000; ecrit deux octets

        public static string Derniere = "";

        // Un objet IOPort par port materiel, cree une seule fois.
        private static readonly IOPort port64 = new IOPort(0x64);     // controleur clavier (commandes)
        private static readonly IOPort port60 = new IOPort(0x60);     // controleur clavier (donnees)
        private static readonly IOPort portCF9 = new IOPort(0xCF9);   // remise a zero des chipsets Intel
        private static readonly IOPort port604 = new IOPort(0x604);   // arret QEMU
        private static readonly IOPort portB004 = new IOPort(0xB004); // arret Bochs / QEMU ancien
        private static readonly IOPort port4004 = new IOPort(0x4004); // arret VirtualBox

        /// <summary>
        /// Petite attente qui ne depend de RIEN (ni horloge, ni interruptions) : pendant l'arret,
        /// le minuteur peut deja etre coupe et PIT.Wait() ne rendrait jamais la main.
        /// </summary>
        private static void Patienter(int ms)
        {
            for (int tour = 0; tour < ms; tour++)
                for (int i = 0; i < 30000; i++) { }
        }

        /// <summary>
        /// Redemarre la machine. Les methodes MATERIELLES passent en premier : elles agissent
        /// immediatement. L'ACPI de Cosmos est essayee en dernier car, sur beaucoup de machines,
        /// elle ne rend jamais la main (c'est elle qui figeait l'ecran "Redemarrage en cours").
        /// </summary>
        public static void Maintenant()
        {
            // 1. Controleur clavier 8042 : la methode des PC depuis 1984
            Derniere = "controleur clavier (8042)";
            Impulsion8042();
            Patienter(60);

            // 2. Registre de remise a zero des chipsets Intel
            Derniere = "registre PCI 0xCF9";
            try
            {
                portCF9.Byte = 0x02;      // prepare
                portCF9.Byte = 0x06;      // declenche (reset materiel)
                portCF9.Byte = 0x0E;      // variante avec coupure d'alimentation
            }
            catch (Exception) { }
            Patienter(60);

            // 3. Les deux en boucle : certaines cartes meres ne repondent qu'apres plusieurs essais
            Derniere = "impulsions repetees";
            for (int essai = 0; essai < 10; essai++)
            {
                Impulsion8042();
                try { portCF9.Byte = 0x06; } catch (Exception) { }
                Patienter(30);
            }

            // 4. ACPI, en dernier : si elle bloque, on a deja tout tente
            Derniere = "ACPI (peut ne pas rendre la main)";
            try { Cosmos.System.Power.Reboot(); }
            catch (Exception) { }
            Derniere = "aucune methode n'a fonctionne";
        }

        private static void Impulsion8042()
        {
            try
            {
                for (int i = 0; i < 200; i++)
                {
                    if ((port64.Byte & 0x02) == 0) break;      // file d'entree vide
                    if (port60.Byte == 0) { }                 // on vide le tampon
                }
                port64.Byte = 0xFE;                            // impulsion de remise a zero
            }
            catch (Exception) { }
        }

        public static void Eteindre()
        {
            // Methodes d'arret des machines virtuelles (QEMU, Bochs, VirtualBox) : immediates
            Derniere = "arret machine virtuelle";
            try
            {
                port604.Word = 0x2000;     // QEMU recent
                Patienter(50);
                portB004.Word = 0x2000;    // Bochs / QEMU ancien
                Patienter(50);
                port4004.Word = 0x3400;    // VirtualBox
            }
            catch (Exception) { }
            Patienter(100);

            Derniere = "ACPI (arret)";
            try { Cosmos.System.Power.Shutdown(); }
            catch (Exception) { }
            Derniere = "arret impossible : maintenez le bouton d'alimentation";
        }

        /// <summary>Message affiche quand la machine refuse de repartir toute seule.</summary>
        public static void Echec(Canvas c, Font f, bool redemarrage)
        {
            try
            {
                var fond = new Pen(Color.FromArgb(120, 20, 24));
                var texte = new Pen(Color.White);
                c.DrawFilledRectangle(fond, 0, Services.ScreenH / 2 - 40, Services.ScreenW, 80);
                c.DrawString((redemarrage ? "Le redemarrage automatique a echoue" : "L'arret automatique a echoue") +
                             "  (derniere methode : " + Derniere + ")", f, texte, 20, Services.ScreenH / 2 - 30);
                c.DrawString(redemarrage ? "Nouvel essai dans 2 s. Sinon : bouton d'alimentation de l'ordinateur."
                                         : "Maintenez le bouton d'alimentation quelques secondes.", f, texte, 20, Services.ScreenH / 2);
                c.Display();
            }
            catch (Exception) { }
        }
    }
}
