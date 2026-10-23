using System;
using System.Collections.Generic;
using FileSystemOS.Systeme;
using FileSystemOS.Utils;

namespace FileSystemOS.Graphique
{
    /// <summary>
    /// CAPTURES D'ECRAN (touche Impr. ecran).
    ///
    /// L'image affichee est deja en memoire dans le buffer "ecran" : il suffit de l'enregistrer
    /// au format BMP 24 bits, que l'OS sait relire dans l'application Images.
    /// Les fichiers vont dans \FSOS\Utilisateurs\<nom>\Captures, ou a defaut \FSOS\Captures.
    /// </summary>
    public static class Capture
    {
        public static string Dernier = "";
        public static uint Nombre;

        public static string Dossier()
        {
            string home = Comptes.Home();
            string root = InstallerWindow.FindRoot();
            string d = home != null ? home + "\\Captures" : (root != null ? root + "\\Captures" : null);
            if (d == null) return null;
            try
            {
                if (home != null) FileSystemOS.Utils.FsTools.MakeDir(home);
                FileSystemOS.Utils.FsTools.MakeDir(d);
            }
            catch (Exception) { return null; }
            return d;
        }

        private static string Nom()
        {
            string h;
            try
            {
                h = "20" + Fmt.Two(Cosmos.HAL.RTC.Year) + Fmt.Two(Cosmos.HAL.RTC.Month) + Fmt.Two(Cosmos.HAL.RTC.DayOfTheMonth) +
                    "-" + Fmt.Two(Cosmos.HAL.RTC.Hour) + Fmt.Two(Cosmos.HAL.RTC.Minute) + Fmt.Two(Cosmos.HAL.RTC.Second);
            }
            catch (Exception) { h = "capture" + Nombre; }
            return "ecran-" + h + ".bmp";
        }

        /// <summary>Transforme un buffer de pixels en fichier BMP 24 bits (lignes de bas en haut).</summary>
        public static byte[] VersBmp(PixelBuffer src)
        {
            int w = src.W, h = src.H;
            int ligne = (w * 3 + 3) & ~3;          // chaque ligne est alignee sur 4 octets
            int taille = 54 + ligne * h;
            byte[] b = new byte[taille];

            b[0] = (byte)'B'; b[1] = (byte)'M';
            Ecrire32(b, 2, taille);
            Ecrire32(b, 10, 54);                   // debut des pixels
            Ecrire32(b, 14, 40);                   // taille de l'en-tete
            Ecrire32(b, 18, w);
            Ecrire32(b, 22, h);
            b[26] = 1;                             // plans
            b[28] = 24;                            // bits par pixel
            Ecrire32(b, 34, ligne * h);
            Ecrire32(b, 38, 2835); Ecrire32(b, 42, 2835);   // 72 points par pouce

            for (int y = 0; y < h; y++)
            {
                int src0 = (h - 1 - y) * w;        // le BMP stocke la derniere ligne en premier
                int dst = 54 + y * ligne;
                for (int x = 0; x < w; x++)
                {
                    int p = src.Data[src0 + x];
                    b[dst + x * 3] = (byte)p;              // bleu
                    b[dst + x * 3 + 1] = (byte)(p >> 8);   // vert
                    b[dst + x * 3 + 2] = (byte)(p >> 16);  // rouge
                }
            }
            return b;
        }

        private static void Ecrire32(byte[] b, int p, int v)
        {
            b[p] = (byte)v; b[p + 1] = (byte)(v >> 8); b[p + 2] = (byte)(v >> 16); b[p + 3] = (byte)(v >> 24);
        }

        /// <summary>Capture l'ecran entier. Renvoie le message a afficher.</summary>
        public static string Prendre(bool ouvrir)
        {
            if (Services.Compositor == null) return "Erreur : ecran non pret";
            string d = Dossier();

            // Pas de disque (mode Live sur cle USB) : la capture est quand meme prise et
            // affichee dans l'application Images, mais elle ne peut pas etre enregistree.
            if (d == null)
            {
                try
                {
                    Services.ImageWin.Afficher(Services.Compositor.Scene.Copie("capture"),
                                               "capture (non enregistree)");
                    Services.Windows.Open(Services.ImageWin);
                    Nombre++;
                    return "Capture affichee mais NON enregistree : aucun disque accessible";
                }
                catch (Exception e) { return "Erreur : " + e.Message; }
            }
            try
            {
                byte[] bmp = VersBmp(Services.Compositor.Scene);
                string chemin = Downloads.Save(d, Nom(), bmp);
                Nombre++;
                Dernier = chemin;
                if (ouvrir)
                {
                    Services.ImageWin.Load(chemin);
                    Services.Windows.Open(Services.ImageWin);
                }
                return "Capture enregistree : " + chemin + "  (" + Fmt.Size(bmp.Length) + ")";
            }
            catch (Exception e) { return "Erreur : " + e.Message; }
        }

        public static List<string> Liste()
        {
            var r = new List<string>();
            string d = Dossier();
            if (d == null) { r.Add("aucun dossier de captures"); return r; }
            r.Add("Dossier : " + d);
            try
            {
                var l = Cosmos.System.FileSystem.VFS.VFSManager.GetDirectoryListing(d);
                for (int i = 0; i < l.Count; i++)
                    if (l[i].mName.ToLower().EndsWith(".bmp")) r.Add("  " + l[i].mName + "   " + Fmt.Size(l[i].mSize));
            }
            catch (Exception) { }
            if (r.Count == 1) r.Add("  (aucune capture)");
            return r;
        }
    }
}
