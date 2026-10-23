using System;
using FileSystemOS.Memoire;
using FileSystemOS.Processus;

namespace FileSystemOS.Graphique
{
    /// <summary>
    /// DOUBLE TAMPON pour les programmes gourmands (jeux, animations).
    ///
    /// Deux images : une AFFICHEE, une EN COURS DE DESSIN. Quand le dessin est fini, on
    /// ECHANGE les deux (Basculer) : le compositeur ne voit jamais une image a moitie dessinee,
    /// donc pas de scintillement ni de dechirure.
    ///
    /// La memoire est reservee en PAGES attribuees au processus du programme : elle revient
    /// automatiquement au systeme quand celui-ci se termine (voir Memoire/Pages.cs).
    /// </summary>
    public class DoubleBuffer
    {
        public readonly PixelBuffer Avant;    // celle qu'on affiche
        public readonly PixelBuffer Arriere;  // celle qu'on dessine
        private PixelBuffer affichee, dessin;

        public readonly int W, H;
        public readonly int PagesReservees;
        public uint Basculements;

        public DoubleBuffer(string nom, int w, int h, Process proc)
        {
            W = w; H = h;
            Avant = new PixelBuffer(nom + " (avant)", w, h);
            Arriere = new PixelBuffer(nom + " (arriere)", w, h);
            affichee = Avant;
            dessin = Arriere;

            // Memoire dediee : une page de 4 Ko pour 1024 pixels (4 octets chacun), x2 images
            int pages = (w * h * 4 * 2 + 4095) / 4096;
            var liste = Pages.Allouer(pages, nom + " (double tampon)", Attr.Donnees, proc, null);
            PagesReservees = liste == null ? 0 : pages;
        }

        /// <summary>L'image sur laquelle le programme dessine.</summary>
        public PixelBuffer Dessin => dessin;

        /// <summary>L'image prete a afficher.</summary>
        public PixelBuffer Affichee => affichee;

        /// <summary>Le dessin est fini : on echange les deux images.</summary>
        public void Basculer()
        {
            PixelBuffer t = affichee;
            affichee = dessin;
            dessin = t;
            Basculements++;
        }

        public void Liberer()
        {
            Avant.Release();
            Arriere.Release();
        }
    }
}
