using System.Drawing;
using FileSystemOS.Graphique;

namespace FileSystemOS.Demarrage
{
    /// <summary>
    /// Logo de File System OS : un V INVISIBLE.
    /// Seuls de petits points gris glissent le long de son trace ;
    /// c'est leur mouvement qui laisse deviner le V, sans jamais le dessiner.
    /// </summary>
    public static class VLogo
    {
        private const int DotsPerTrain = 9;
        private const int Spacing = 38;   // ecart entre deux points (sur 1000)
        private const int Speed = 9;      // avance par image (sur 1000)

        public static void Draw(PixelBuffer b, int cx, int cy, int size, uint frame, Color bg)
        {
            int half = size / 2;
            int ax = cx - half, ay = cy - half;   // branche gauche (haut)
            int px = cx, py = cy + half;          // pointe du V (bas)
            int ex = cx + half, ey = cy - half;   // branche droite (haut)

            b.FillRect(bg, ax - 8, ay - 8, size + 16, size + 16);

            int head = (int)((frame * Speed) % 1000);

            // Deux "trains" de points decales d'un demi-tour
            for (int train = 0; train < 2; train++)
            {
                for (int i = DotsPerTrain - 1; i >= 0; i--)          // la queue d'abord, la tete par-dessus
                {
                    int t = (head + train * 500 - i * Spacing + 2000) % 1000;
                    int x, y;
                    if (t < 500) { x = ax + (px - ax) * t / 500; y = ay + (py - ay) * t / 500; }
                    else { int u = t - 500; x = px + (ex - px) * u / 500; y = py + (ey - py) * u / 500; }

                    // Gris fondu : la tete est gris clair, la queue se fond dans le fond
                    int k = i * 255 / DotsPerTrain;
                    Color col = Color.FromArgb(
                        200 + (bg.R - 200) * k / 255,
                        202 + (bg.G - 202) * k / 255,
                        210 + (bg.B - 210) * k / 255);
                    int r = i == 0 ? 5 : (i < 4 ? 4 : 3);
                    b.FillCircle(col, x, y, r);
                }
            }
        }
    }
}
