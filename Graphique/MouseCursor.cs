namespace FileSystemOS.Graphique
{
    /// <summary>
    /// Curseur 12x19 ecrit en BINAIRE (1 bit = 1 pixel, bit de gauche = colonne 0).
    /// Deux masques : le contour (noir) et le remplissage (blanc).
    /// Build() les convertit en bitmap ARGB 32 bits.
    /// </summary>
    public static class MouseCursor
    {
        public const int W = 12;
        public const int H = 19;

        public const uint Black = 0xFF000000;
        public const uint White = 0xFFFFFFFF;

        public static readonly ushort[] Outline =
        {
            0b100000000000,
            0b110000000000,
            0b101000000000,
            0b100100000000,
            0b100010000000,
            0b100001000000,
            0b100000100000,
            0b100000010000,
            0b100000001000,
            0b100000000100,
            0b100000000010,
            0b100000011111,
            0b100010010000,
            0b100110010000,
            0b101001001000,
            0b110001001000,
            0b100000100100,
            0b000000100100,
            0b000000011000,
        };

        public static readonly ushort[] Fill =
        {
            0b000000000000,
            0b000000000000,
            0b010000000000,
            0b011000000000,
            0b011100000000,
            0b011110000000,
            0b011111000000,
            0b011111100000,
            0b011111110000,
            0b011111111000,
            0b011111111100,
            0b011111100000,
            0b011101100000,
            0b011001100000,
            0b010000110000,
            0b000000110000,
            0b000000011000,
            0b000000011000,
            0b000000000000,
        };

        /// <summary>Bitmap ARGB : 0 = transparent.</summary>
        public static uint[] Bitmap;

        public static void Build()
        {
            Bitmap = new uint[W * H];
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    int bit = 11 - x;
                    uint px = 0;
                    if (((Fill[y] >> bit) & 1) == 1) px = White;
                    if (((Outline[y] >> bit) & 1) == 1) px = Black;
                    Bitmap[y * W + x] = px;
                }
            }
        }

        public static void Draw(PixelBuffer screen, int mx, int my) =>
            screen.DrawImage(Bitmap, W, H, mx, my);
    }
}
