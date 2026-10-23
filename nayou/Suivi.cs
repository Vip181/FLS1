using System;
using System.Drawing;
using Cosmos.System.Graphics;   // Pen, Canvas
using FileSystemOS.Processus;

namespace FileSystemOS.Noyau
{
    /// <summary>
    /// SUIVI DES ETAPES, affiche TOUT DE SUITE en bas de l'ecran (sans attendre le compositeur).
    ///
    /// Sert aux operations longues ou delicates (creation du compte, ouverture de session,
    /// ecriture sur le SSD). Chaque etape est ecrite directement dans l'image envoyee a l'ecran :
    /// si le systeme se fige completement (meme la souris, sans bande rouge du chien de garde),
    /// la derniere ligne affichee dit exactement ou il s'est arrete. Elle est aussi recopiee
    /// dans Watchdog.Etape et dans le journal en memoire.
    /// </summary>
    public static class Suivi
    {
        public static bool Actif;
        public static string Derniere = "";
        private static int numero;

        public static void Commencer()
        {
            Actif = true;
            numero = 0;
        }

        public static void Finir()
        {
            Actif = false;
            Services.ScreenDirty = true;
        }

        public static void Etape(string texte)
        {
            Watchdog.Etape = texte;
            if (!Actif) return;
            numero++;
            Derniere = numero + ". " + texte;
            try { if (Services.Memory != null) Services.Memory.Write("Suivi : " + Derniere); } catch (Exception) { }
            try
            {
                var c = Services.Canvas;
                int y = Services.ScreenH - 26;
                c.DrawFilledRectangle(new Pen(Color.FromArgb(24, 26, 44)), 0, y, Services.ScreenW, 26);
                c.DrawString("En cours : " + Derniere, Services.Font, new Pen(Color.FromArgb(255, 210, 110)), 12, y + 5);
                c.Display();
            }
            catch (Exception) { }
        }
    }
}
