using System;
using Cosmos.System.Graphics;
using FileSystemOS.Pilotes;

namespace FileSystemOS.Systeme
{
    /// <summary>
    /// MODE TEXTE DE SECOURS : si l'ecran graphique ne s'ouvre pas (ecran noir sur un vrai PC),
    /// on reste en mode TEXTE du BIOS, qui marche toujours, et on affiche ce qui s'est passe.
    /// L'utilisateur peut alors essayer une autre resolution avec les touches 1 a 5.
    /// </summary>
    public static class EcranSecours
    {
        private static readonly int[] W = { 1024, 800, 640, 1280, 1366 };
        private static readonly int[] H = { 768, 600, 480, 720, 768 };

        public static string Raison = "";

        /// <summary>Renvoie un Canvas ouvert, ou null si l'utilisateur ne veut pas reessayer.</summary>
        public static Canvas Menu()
        {
            while (true)
            {
                Console.Clear();
                Console.WriteLine("=====================================================================");
                Console.WriteLine("  " + Services.OsName + " " + Services.Version + "  -  MODE TEXTE DE SECOURS");
                Console.WriteLine("  " + Services.Company + " - " + Services.Author);
                Console.WriteLine("=====================================================================");
                Console.WriteLine();
                Console.WriteLine("L'ecran graphique n'a pas pu s'ouvrir.");
                Console.WriteLine("Raison : " + (Raison.Length > 0 ? Raison : "inconnue"));
                Console.WriteLine();

                try
                {
                    Machine.Detect();
                    Console.WriteLine("Machine    : " + Machine.Marque + (Machine.Virtuel ? " (virtuelle)" : " (physique)"));
                    Console.WriteLine("Processeur : " + Machine.Cpu);
                    Console.WriteLine("Chipset    : " + Machine.Chipset);
                    Console.WriteLine("Graphique  : " + Machine.Carte);
                }
                catch (Exception e) { Console.WriteLine("Materiel : " + e.Message); }

                Console.WriteLine();
                Console.WriteLine("Essayer une resolution :");
                for (int i = 0; i < W.Length; i++)
                    Console.WriteLine("   " + (i + 1) + " = " + W[i] + "x" + H[i] + " en 32 bits");
                Console.WriteLine("   T = rester en mode texte (diagnostic)");
                Console.WriteLine("   R = redemarrer l'ordinateur");
                Console.WriteLine();
                Console.WriteLine("Si TOUTES les resolutions echouent : le chargeur de demarrage n'a pas");
                Console.WriteLine("prepare d'ecran graphique. Demarrez en mode BIOS / Legacy (CSM) et");
                Console.WriteLine("activez \"Enable Legacy Option ROMs\" dans le BIOS.");
                Console.Write("Votre choix : ");

                var key = Console.ReadKey(true);
                char c = key.KeyChar;
                if (c == 'r' || c == 'R') Redemarrage.Maintenant();
                if (c == 't' || c == 'T') return null;
                int n = c - '1';
                if (n < 0 || n >= W.Length) continue;

                Console.WriteLine();
                Console.WriteLine("Ouverture de " + W[n] + "x" + H[n] + "...");
                try
                {
                    Canvas cv = FullScreenCanvas.GetFullScreenCanvas(
                        new Mode(W[n], H[n], ColorDepth.ColorDepth32));
                    Services.ScreenW = W[n];
                    Services.ScreenH = H[n];
                    Materiel.Choix = "choisie en mode secours";
                    return cv;
                }
                catch (Exception e)
                {
                    Console.WriteLine("Echec : " + e.Message);
                    Console.WriteLine("Appuyez sur une touche...");
                    Console.ReadKey(true);
                }
            }
        }

        /// <summary>Affichage texte permanent quand aucun mode graphique n'est possible.</summary>
        public static void Diagnostic()
        {
            Console.Clear();
            Console.WriteLine(Services.OsName + " " + Services.Version + " - diagnostic (mode texte)");
            Console.WriteLine("---------------------------------------------------------------");
            try
            {
                var r = Machine.Report();
                for (int i = 0; i < r.Count; i++) Console.WriteLine(r[i]);
            }
            catch (Exception) { }
            try
            {
                var all = PciCatalogue.Scan();
                Console.WriteLine();
                Console.WriteLine(all.Count + " peripheriques PCI :");
                for (int i = 0; i < all.Count && i < 16; i++)
                    Console.WriteLine("  " + all[i].Location + " [" + all[i].Id + "] " + all[i].VendorName + " - " + all[i].Kind);
            }
            catch (Exception) { }
            Console.WriteLine();
            Console.WriteLine("Prenez cet ecran en photo. R = redemarrer.");
            while (true)
            {
                var k = Console.ReadKey(true);
                if (k.KeyChar == 'r' || k.KeyChar == 'R') Redemarrage.Maintenant();
            }
        }
    }
}
