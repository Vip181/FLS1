using System;
using System.Drawing;
using Cosmos.System.Graphics;          // Canvas, Pen, Mode, ColorDepth, FullScreenCanvas
using Cosmos.System.Graphics.Fonts;    // PCScreenFont, Font

namespace FileSystemOS.Systeme
{
    /// <summary>
    /// AMORCE : la toute premiere chose que fait l'OS.
    ///
    /// Ouvre un ecran utilisable AVANT tout le reste (disques, memoire, pilotes), puis
    /// affiche chaque etape du demarrage. Ainsi, si l'OS se bloque, on voit exactement OU :
    /// plus jamais d'ecran noir muet.
    /// Si aucun mode graphique n'est possible, on ecrit en mode texte du BIOS.
    /// </summary>
    public static class Amorce
    {
        private static readonly int[] W = { 1024, 800, 640, 1280, 1366, 1280 };
        private static readonly int[] H = { 768, 600, 480, 720, 768, 1024 };

        public static bool Graphique;
        public static string Comment = "";
        private static int ligne;
        private static Pen blanc, gris, rouge, fond;

        /// <summary>Ouvre l'ecran le plus sur possible. Renvoie false si seul le texte marche.</summary>
        public static bool Ouvrir()
        {
            // 1. Le mode deja prepare par le chargeur de demarrage (GRUB / Limine)
            try
            {
                Canvas c = FullScreenCanvas.GetFullScreenCanvas();
                if (c != null)
                {
                    int w = 0, h = 0;
                    try { w = (int)c.Mode.Columns; h = (int)c.Mode.Rows; } catch (Exception) { }
                    if (w >= 640 && h >= 480 && w <= 4096 && h <= 2160)
                    {
                        Services.Canvas = c;
                        Services.ScreenW = w;
                        Services.ScreenH = h;
                        Comment = "mode du chargeur " + w + "x" + h;
                        Graphique = true;
                        Prepare();
                        return true;
                    }
                }
            }
            catch (Exception e) { Comment = e.Message; }

            // 2. Sinon on demande nous-memes un mode, du plus sur au moins sur
            for (int i = 0; i < W.Length; i++)
            {
                try
                {
                    Canvas c = FullScreenCanvas.GetFullScreenCanvas(new Mode(W[i], H[i], ColorDepth.ColorDepth32));
                    if (c == null) continue;
                    Services.Canvas = c;
                    Services.ScreenW = W[i];
                    Services.ScreenH = H[i];
                    Comment = "mode force " + W[i] + "x" + H[i];
                    Graphique = true;
                    Prepare();
                    return true;
                }
                catch (Exception e) { Comment = e.Message; }
            }

            // 3. Rien de graphique : mode texte du BIOS
            Graphique = false;
            try
            {
                Console.Clear();
                Console.WriteLine(Services.OsName + " " + Services.Version + " - mode texte");
                Console.WriteLine("Aucun mode graphique (" + Comment + ")");
                Console.WriteLine();
            }
            catch (Exception) { }
            return false;
        }

        private static void Prepare()
        {
            try { Services.Font = PCScreenFont.Default; } catch (Exception) { }
            blanc = new Pen(Color.FromArgb(220, 224, 236));
            gris = new Pen(Color.FromArgb(120, 126, 150));
            rouge = new Pen(Color.FromArgb(255, 110, 110));
            fond = new Pen(Color.FromArgb(14, 15, 22));
            try
            {
                Services.Canvas.DrawFilledRectangle(fond, 0, 0, Services.ScreenW, Services.ScreenH);
                Services.Canvas.Display();
            }
            catch (Exception) { }
            ligne = 0;
            Etape(Services.OsName + " " + Services.Version + "  -  " + Services.Company);
            Etape("Ecran : " + Comment);
        }

        /// <summary>Affiche une etape du demarrage (graphique ou texte).</summary>
        public static void Etape(string texte)
        {
            if (!Graphique)
            {
                try { Console.WriteLine(texte); } catch (Exception) { }
                return;
            }
            try
            {
                int y = 12 + ligne * 18;
                if (y > Services.ScreenH - 30) { ligne = 0; y = 12; Services.Canvas.DrawFilledRectangle(fond, 0, 0, Services.ScreenW, Services.ScreenH); }
                Services.Canvas.DrawString(texte, Services.Font, texte.StartsWith("ERREUR") ? rouge : (ligne == 0 ? blanc : gris), 12, y);
                Services.Canvas.Display();
                ligne++;
            }
            catch (Exception) { }
        }

        /// <summary>Execute une etape du demarrage en la nommant, et signale l'erreur s'il y en a une.</summary>
        public static bool Faire(string nom, Action action)
        {
            Etape("... " + nom);
            try { action(); }
            catch (Exception e)
            {
                Etape("ERREUR a l'etape '" + nom + "' : " + e.Message);
                return false;
            }
            return true;
        }
    }
}
