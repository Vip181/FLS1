using System;
using System.Drawing;
using Cosmos.System;
using FileSystemOS.Processus;

namespace FileSystemOS.Graphique
{
    /// <summary>
    /// JEU DU SERPENT : la demonstration du DOUBLE TAMPON.
    ///
    /// Le jeu ne dessine jamais directement dans la fenetre : il dessine dans l'image
    /// "arriere" du double tampon, puis bascule. Son processus et son thread sont dedies,
    /// et sa memoire est reservee en pages (rendues a la fermeture).
    /// </summary>
    public class SerpentWindow : Window
    {
        private const int Case = 16, Cols = 28, Lignes = 20, Bord = 10, Haut = 28;

        private readonly int[] x = new int[Cols * Lignes];
        private readonly int[] y = new int[Cols * Lignes];
        private int taille, dx = 1, dy, pommeX, pommeY, score, meilleur;
        private bool enCours, perdu;
        private uint graine = 12345, dernier, pas;
        private DoubleBuffer tampon;
        public Process JeuProc;

        // Taille calculee AVANT la classe de base, qui cree le buffer (avant : hauteur 0).
        private const int Largeur = Bord * 2 + Cols * Case;
        private const int Hauteur = TitleH + Haut + Bord * 2 + Lignes * Case;

        public SerpentWindow(int px, int py)
            : base("Serpent", px, py, Largeur, Hauteur, Icons.GameIcon) { }

        public override void OnOpen()
        {
            if (tampon == null)
            {
                // Processus dedie : le jeu a sa propre memoire et son propre fil d'execution
                JeuProc = Services.Scheduler.Spawn("jeu-serpent", false);
                tampon = new DoubleBuffer("serpent", Cols * Case, Lignes * Case, JeuProc);
                JeuProc.Start(new SerpentThread(this));
            }
            Nouvelle();
        }

        public void Nouvelle()
        {
            taille = 4;
            for (int i = 0; i < taille; i++) { x[i] = 8 - i; y[i] = 10; }
            dx = 1; dy = 0; score = 0; perdu = false; enCours = true;
            Pomme();
            Dessiner();
        }

        private uint Hasard()
        {
            graine = graine * 1103515245 + 12345;      // suite pseudo-aleatoire simple
            return (graine >> 16) & 0x7FFF;
        }

        private void Pomme()
        {
            for (int essai = 0; essai < 200; essai++)
            {
                int px = (int)(Hasard() % Cols), py = (int)(Hasard() % Lignes);
                bool libre = true;
                for (int i = 0; i < taille; i++) if (x[i] == px && y[i] == py) libre = false;
                if (libre) { pommeX = px; pommeY = py; return; }
            }
        }

        /// <summary>Un pas de jeu, appele par le thread dedie (5 fois par seconde).</summary>
        public void Avancer()
        {
            if (!enCours || Hidden) return;
            uint t;
            if (Horloge.Active && Horloge.Millis > 0) t = Horloge.Millis / 160;
            else t = ++pas / 12;                       // pas d'horloge : on compte les tours
            if (t == dernier) return;
            dernier = t;

            int nx = x[0] + dx, ny = y[0] + dy;
            if (nx < 0 || ny < 0 || nx >= Cols || ny >= Lignes) { Fin(); return; }
            for (int i = 0; i < taille; i++) if (x[i] == nx && y[i] == ny) { Fin(); return; }

            for (int i = taille; i > 0; i--) { x[i] = x[i - 1]; y[i] = y[i - 1]; }
            x[0] = nx; y[0] = ny;

            if (nx == pommeX && ny == pommeY)
            {
                if (taille < x.Length - 1) taille++;
                score += 10;
                Pomme();
            }
            Dessiner();
        }

        private void Fin()
        {
            enCours = false; perdu = true;
            if (score > meilleur) meilleur = score;
            Dessiner();
        }

        /// <summary>Tout le jeu est dessine dans l'image ARRIERE, puis on bascule.</summary>
        private void Dessiner()
        {
            PixelBuffer b = tampon.Dessin;
            b.Clear(Color.FromArgb(18, 20, 30));
            for (int i = 0; i <= Cols; i++) b.FillRect(Color.FromArgb(28, 31, 44), i * Case, 0, 1, Lignes * Case);
            for (int j = 0; j <= Lignes; j++) b.FillRect(Color.FromArgb(28, 31, 44), 0, j * Case, Cols * Case, 1);

            b.FillRoundRect(Theme.Red, pommeX * Case + 3, pommeY * Case + 3, Case - 6, Case - 6, 5);
            for (int i = taille - 1; i >= 0; i--)
            {
                Color c = i == 0 ? Theme.DotGreen : Color.FromArgb(60, 170 - i * 2 > 60 ? 170 - i * 2 : 60, 110);
                b.FillRoundRect(c, x[i] * Case + 2, y[i] * Case + 2, Case - 4, Case - 4, 4);
            }
            if (perdu)
            {
                b.FillRect(Color.FromArgb(10, 10, 16), 0, Lignes * Case / 2 - 26, Cols * Case, 52);
                string m = "Perdu !  Entree = rejouer";
                b.DrawText(m, Theme.TitleText, (Cols * Case - m.Length * 8) / 2, Lignes * Case / 2 - 8);
            }
            tampon.Basculer();      // l'image complete devient visible d'un coup
            Dirty = true;
        }

        protected override void DrawContent(PixelBuffer b, int cx, int cy, int cw, int ch)
        {
            b.FillRect(Theme.ToolbarBg, 0, cy, W, Haut);
            b.DrawText("Score " + score + "    Meilleur " + meilleur +
                       "    images " + (tampon == null ? 0 : tampon.Basculements), Theme.Text, 12, cy + 6);
            if (tampon != null) b.Blit(tampon.Affichee, Bord, cy + Haut);
            b.DrawRect(Theme.Separator, Bord - 1, cy + Haut - 1, Cols * Case + 2, Lignes * Case + 2);
        }

        public override void OnKey(KeyEvent k)
        {
            switch (k.Key)
            {
                case ConsoleKeyEx.LeftArrow: if (dx == 0) { dx = -1; dy = 0; } break;
                case ConsoleKeyEx.RightArrow: if (dx == 0) { dx = 1; dy = 0; } break;
                case ConsoleKeyEx.UpArrow: if (dy == 0) { dx = 0; dy = -1; } break;
                case ConsoleKeyEx.DownArrow: if (dy == 0) { dx = 0; dy = 1; } break;
                case ConsoleKeyEx.Enter: if (perdu) Nouvelle(); break;
                case ConsoleKeyEx.Spacebar: enCours = !enCours; break;
            }
        }
    }

    /// <summary>Thread dedie du jeu : il fait avancer le serpent, independamment de l'interface.</summary>
    public class SerpentThread : KThread
    {
        private readonly SerpentWindow jeu;
        public SerpentThread(SerpentWindow w) : base("jeu") { jeu = w; }
        public override void Step() => jeu.Avancer();
    }
}
