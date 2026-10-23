using System;
using FileSystemOS.Processus;
using FileSystemOS.Systeme;
using FileSystemOS.Utils;

namespace FileSystemOS.Graphique
{
    /// <summary>
    /// ENREGISTREMENT DE L'ECRAN en video (GIF anime), et capture differee.
    ///
    /// Deux images par seconde, en demi-resolution. Chaque image est d'abord copiee (reduite
    /// et convertie en 256 couleurs) en une fois, puis compressee quelques lignes a la fois par
    /// le thread "video" : l'interface reste fluide pendant l'enregistrement.
    /// Le fichier va dans le dossier des captures : video-AAAAMMJJ-HHMMSS.gif.
    /// Limite : 24 Mo (environ plusieurs minutes d'utilisation normale), arret automatique.
    /// </summary>
    public static class Enregistreur
    {
        public static bool Actif;
        public static int Images;
        public static string Message = "";

        private const int Intervalle = 500;          // ms entre deux images
        private const int Delai = 50;                // duree d'une image a la lecture (centiemes)
        private const int LignesParPas = 12;
        private const int TailleMax = 24 * 1024 * 1024;

        private static int w, h;
        private static byte[] image;                 // image en cours, deja en 256 couleurs
        private static int ligne = -1;               // prochaine ligne a compresser (-1 : aucune)
        private static uint debut, derniere;
        private static Tampon gif;
        private static readonly LzwGif lzw = new LzwGif();

        // Capture differee (la fenetre Capture se cache le temps de la photo)
        private static uint capturePrevue;
        private static Window aMontrer;

        public static uint Secondes => Actif && Horloge.Active ? (Horloge.Millis - debut) / 1000 : 0;
        public static int Taille => gif != null ? gif.Count : 0;

        public static string Demarrer()
        {
            if (Actif) return "Enregistrement deja en cours";
            if (Services.Compositor == null) return "Erreur : ecran non pret";
            PixelBuffer s = Services.Compositor.Scene;
            w = s.W / 2; h = s.H / 2;
            image = new byte[w * h];
            gif = new Tampon(1024 * 1024);
            GifAnime.Entete(gif, w, h);
            Images = 0;
            ligne = -1;
            debut = Horloge.Millis;
            derniere = 0;
            Actif = true;
            Message = "Enregistrement en cours (" + w + " x " + h + ", 2 images par seconde)";
            return Message;
        }

        public static string Arreter()
        {
            if (!Actif) return "Aucun enregistrement en cours";
            // Termine l'image commencee, puis ferme le fichier
            if (ligne >= 0)
            {
                lzw.Ajouter(image, ligne * w, (h - ligne) * w);
                lzw.Terminer();
                GifAnime.FinImage(gif);
                Images++;
                ligne = -1;
            }
            Actif = false;
            GifAnime.Fin(gif);
            byte[] fichier = gif.VersTableau();
            gif = null;
            image = null;

            string d = Capture.Dossier();
            if (d == null) { Message = "Video NON enregistree : aucun disque accessible"; return Message; }
            try
            {
                string chemin = Downloads.Save(d, NomFichier(), fichier);
                Capture.Dernier = chemin;
                Message = "Video enregistree : " + chemin + "  (" + Images + " images, " + Fmt.Size(fichier.Length) + ")";
            }
            catch (Exception e) { Message = "Erreur : " + e.Message; }
            return Message;
        }

        private static string NomFichier()
        {
            try
            {
                return "video-20" + Fmt.Two(Cosmos.HAL.RTC.Year) + Fmt.Two(Cosmos.HAL.RTC.Month) +
                       Fmt.Two(Cosmos.HAL.RTC.DayOfTheMonth) + "-" + Fmt.Two(Cosmos.HAL.RTC.Hour) +
                       Fmt.Two(Cosmos.HAL.RTC.Minute) + Fmt.Two(Cosmos.HAL.RTC.Second) + ".gif";
            }
            catch (Exception) { return "video-" + Capture.Nombre + ".gif"; }
        }

        /// <summary>Photo de l'ecran dans 'ms' millisecondes, en cachant d'abord la fenetre 'fenetre'.</summary>
        public static void CaptureDifferee(Window fenetre, uint ms)
        {
            aMontrer = fenetre;
            if (fenetre != null) { fenetre.Minimized = true; Services.ScreenDirty = true; }
            capturePrevue = Horloge.Millis + ms;
            if (capturePrevue == 0) capturePrevue = 1;
            Message = "Capture dans " + (ms / 1000) + " s...";
        }

        /// <summary>Un petit morceau de travail : appele a chaque tour par le thread "video".</summary>
        public static void Pas()
        {
            if (capturePrevue != 0 && Horloge.Millis >= capturePrevue)
            {
                capturePrevue = 0;
                Message = Capture.Prendre(false);
                if (aMontrer != null) { aMontrer.Minimized = false; aMontrer.Dirty = true; aMontrer = null; }
                Services.ScreenDirty = true;
            }

            if (!Actif) return;
            if (ligne < 0)
            {
                if (Horloge.Millis - derniere < Intervalle) return;
                derniere = Horloge.Millis;
                Saisir();
                GifAnime.DebutImage(gif, w, h, Delai);
                lzw.Debuter(gif);
                ligne = 0;
                return;
            }

            int fin = ligne + LignesParPas;
            if (fin > h) fin = h;
            lzw.Ajouter(image, ligne * w, (fin - ligne) * w);
            ligne = fin;
            if (ligne < h) return;

            lzw.Terminer();
            GifAnime.FinImage(gif);
            Images++;
            ligne = -1;
            if (gif.Count > TailleMax) { Arreter(); Message = "Limite de 24 Mo atteinte. " + Message; }
        }

        /// <summary>Copie l'ecran actuel, reduit de moitie et en 256 couleurs.</summary>
        private static void Saisir()
        {
            PixelBuffer s = Services.Compositor.Scene;
            int sw = s.W;
            if (s.W / 2 != w || s.H / 2 != h) return;       // resolution changee : on garde l'image precedente
            for (int y = 0; y < h; y++)
            {
                int src = (y * 2) * sw, dst = y * w;
                for (int x = 0; x < w; x++) image[dst + x] = GifAnime.Indice(s.Data[src + x * 2]);
            }
        }
    }

    public class EnregistreurThread : KThread
    {
        public EnregistreurThread() : base("video") { }
        public override void Step() => Enregistreur.Pas();
    }
}
