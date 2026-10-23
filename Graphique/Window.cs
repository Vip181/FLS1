using System.Drawing;
using Cosmos.System;
using FileSystemOS.Processus;
using FileSystemOS.Utils;

namespace FileSystemOS.Graphique
{
    /// <summary>
    /// Fenetre de base. Elle possede :
    ///  - son propre buffer de pixels (Buffer) ou elle se dessine en coordonnees locales ;
    ///  - son propre processus et thread de rendu (Proc), crees a l'ouverture.
    /// Le compositeur recopie ensuite le buffer a l'ecran a la position (X, Y).
    /// </summary>
    public abstract class Window
    {
        public const int TitleH = 30;
        public const int BtnW = 40;          // largeur d'un bouton de barre de titre
        public const int BtnClose = 0, BtnMinimize = 1, BtnWait = 2;

        public int X, Y, W, H;
        public string Title;
        public bool Minimized;
        public bool Suspended;               // en attente (processus gele, voir WaitBuffer)
        public bool Dirty = true;            // le buffer doit etre redessine
        public bool Queued;                  // deja dans le buffer de cogestion
        public bool Fixed;                   // ne peut etre ni fermee, ni reduite, ni mise en attente (connexion)
        public readonly PixelBuffer Buffer;
        public readonly HexBitmap Icon;      // icone hexa (barre de titre, barre des taches...)
        public readonly string[] IconeSource; // ses lignes d'origine : le dock en fait une icone 32 x 32
        public Process Proc;

        private int hoverBtn = -1;

        protected Window(string title, int x, int y, int w, int h, string[] icon)
        {
            Title = title; X = x; Y = y; W = w; H = h;
            Buffer = new PixelBuffer("fenetre " + title, w, h);
            Icon = new HexBitmap(icon, Icons.PaletteDe(icon));
            IconeSource = icon;
        }

        /// <summary>Invisible a l'ecran : reduite ou en attente.</summary>
        public bool Hidden => Minimized || Suspended;

        public bool Contains(int mx, int my) => mx >= X && mx < X + W && my >= Y && my < Y + H;
        public bool HitTitle(int mx, int my) => Contains(mx, my) && my < Y + TitleH;

        /// <summary>0 = fermer, 1 = reduire, 2 = attente, -1 = aucun.</summary>
        public int TitleButtonAt(int mx, int my)
        {
            if (my < Y || my >= Y + TitleH || mx >= X + W) return -1;
            int k = (X + W - 1 - mx) / BtnW;
            return k <= BtnWait ? k : -1;
        }

        /// <summary>Survol des boutons de titre (appele par le gestionnaire de fenetres).</summary>
        public void UpdateTitleHover(int mx, int my)
        {
            int k = mx < 0 ? -1 : TitleButtonAt(mx, my);
            if (k != hoverBtn) { hoverBtn = k; Dirty = true; }
        }

        protected virtual Color ContentBg => Theme.WindowBg;

        /// <summary>Appele par le thread de la fenetre : dessine tout dans son buffer.</summary>
        public void Render(bool focused)
        {
            var b = Buffer;

            // Barre de titre plate et moderne
            b.FillRect(focused ? Theme.TitleActive : Theme.TitleInactive, 0, 0, W, TitleH);
            b.FillRect(focused ? Theme.TitleActiveLight : Theme.TitleInactiveLight, 0, 0, W, 1);
            Icon.Draw(b, 10, (TitleH - 16) / 2);

            string t = Title;
            int maxT = (W - 40 - 3 * BtnW - 10) / 8;
            if (t.Length > maxT) t = t.Substring(0, maxT);
            b.DrawText(t, Theme.TitleText, 34, (TitleH - 16) / 2);

            // Boutons : attente | reduire | fermer  (glyphes hexa)
            for (int k = 0; k <= BtnWait; k++)
            {
                int bx = W - (k + 1) * BtnW;
                if (k == hoverBtn)
                    b.FillRect(k == BtnClose ? Theme.Red : Theme.TitleBtnHover, bx, 0, BtnW, TitleH);
                HexBitmap g = k == BtnClose ? Glyphs.Close : (k == BtnMinimize ? Glyphs.Minimize : Glyphs.Wait);
                g.DrawTinted(b, bx + (BtnW - 10) / 2, (TitleH - 10) / 2, Theme.TitleText);
            }

            b.FillRect(ContentBg, 0, TitleH, W, H - TitleH);
            DrawContent(b, 0, TitleH, W, H - TitleH);

            b.DrawRect(Theme.Border, 0, 0, W, H);
        }

        /// <summary>Contenu en coordonnees LOCALES au buffer (cx, cy = coin du contenu).</summary>
        protected abstract void DrawContent(PixelBuffer b, int cx, int cy, int cw, int ch);

        /// <summary>Appele a chaque pas du thread (animations : curseur clignotant...).</summary>
        public virtual void Tick() { }

        /// <summary>Appele a chaque ouverture (ou reouverture) de la fenetre.</summary>
        public virtual void OnOpen() { }

        // Evenements en coordonnees ECRAN. mx = -1 si la souris n'est pas sur la fenetre.
        public virtual void OnMouseMove(int mx, int my) { }
        public virtual void OnMouseDown(int mx, int my) { }
        public virtual void OnMouseDrag(int mx, int my) { }
        public virtual void OnMouseUp(int mx, int my) { }
        public virtual void OnKey(KeyEvent key) { }
    }

    public class Button
    {
        public int RX, RY, W, H;
        public string Label;
        public Color Normal, HoverColor;
        public bool Hover, Pressed;
        public HexBitmap Icon;   // glyphe hexa optionnel (dessine en blanc)

        public Button(string label, int rx, int ry, int w, int h, Color normal, Color hover)
        {
            Label = label; RX = rx; RY = ry; W = w; H = h; Normal = normal; HoverColor = hover;
        }

        public Button(HexBitmap icon, string label, int rx, int ry, int w, int h, Color normal, Color hover)
            : this(label, rx, ry, w, h, normal, hover)
        {
            Icon = icon;
        }

        public bool Hit(int ox, int oy, int mx, int my) =>
            mx >= ox + RX && mx < ox + RX + W && my >= oy + RY && my < oy + RY + H;

        public void Draw(PixelBuffer b, int ox, int oy)
        {
            int x = ox + RX;
            int y = oy + RY + (Pressed ? 2 : 0);
            Color col = Pressed ? Shade(Normal, -40) : (Hover ? HoverColor : Normal);

            if (!Pressed) b.FillRect(Theme.ButtonShadow, x + 2, y + 3, W, H);
            b.FillRect(col, x, y, W, H);
            b.FillRect(Shade(col, 35), x, y, W, 2);
            if (Icon == null)
                b.DrawText(Label, Theme.TitleText, x + (W - Label.Length * 8) / 2, y + (H - 16) / 2);
            else if (Label.Length == 0)
                Icon.DrawTinted(b, x + (W - Icon.W) / 2, y + (H - Icon.H) / 2, Theme.TitleText);
            else
            {
                int cw = Icon.W + 6 + Label.Length * 8;
                int ix = x + (W - cw) / 2;
                Icon.DrawTinted(b, ix, y + (H - Icon.H) / 2, Theme.TitleText);
                b.DrawText(Label, Theme.TitleText, ix + Icon.W + 6, y + (H - 16) / 2);
            }
        }

        private static Color Shade(Color c, int d) =>
            Color.FromArgb(Clamp(c.R + d), Clamp(c.G + d), Clamp(c.B + d));

        private static int Clamp(int v) => v < 0 ? 0 : (v > 255 ? 255 : v);
    }

    /// <summary>
    /// Fond d'ecran : calcule une seule fois dans son propre buffer.
    /// Image .bmp choisie (interface.cfg : fond_ecran) ou, a defaut, degrade.
    /// </summary>
    public static class Desktop
    {
        public const int TaskbarH = 40;      // barre classique pleine largeur
        public static PixelBuffer Background;

        /// <summary>
        /// Bandes du fond deja floutees et teintees : le verre de la barre des taches (clair) et
        /// de la barre du haut (sombre). Calculees une seule fois, a chaque changement de fond.
        /// </summary>
        public static PixelBuffer VerreBas, VerreHaut;

        /// <summary>Change a chaque nouveau fond (ou nouveau verre) : la barre refait alors son cache.</summary>
        public static int VersionFond;
        public static string WallpaperInfo = "degrade";

        public static void Build()
        {
            int h = Services.ScreenH;                // tout l'ecran : le dock flotte au-dessus du fond
            if (Background == null || Background.W != Services.ScreenW || Background.H != h)
            {
                if (Background != null) Background.Release();
                Background = new PixelBuffer("bureau", Services.ScreenW, h);
            }

            // Ce qui etait en preparation (vagues, image) est abandonne
            vagueRuban = -1;
            AbandonnerImage();

            string choix = Choix();
            int k = IndexFond(choix);
            if (k >= 0 && Fonds[k] != "vagues")
            {
                // Fond simple : un degrade, dessine instantanement (aucun risque au demarrage)
                Degrade(Background, k, 0, 0, Background.W, Background.H);
                WallpaperInfo = NomsFonds[k];
                PreparerVerre();
                return;
            }

            // Vagues ou image : le ciel tout de suite, le reste en arriere-plan (thread "fond")
            Degrade(Background, 0, 0, 0, Background.W, Background.H);
            PreparerVerre();
            if (k >= 0)
            {
                WallpaperInfo = "Vagues (en cours)";
                DemarrerVagues();
            }
            else
            {
                imageChemin = choix;
                WallpaperInfo = TextCodec.FileName(choix) + " (preparation)";
                etape = ELireCache;          // copie toute prete ? sinon, decodage
            }
        }

        private static void PreparerVerre()
        {
            int w = Services.ScreenW, hb = TaskbarH, ht = TopBar.H;
            if (VerreBas == null || VerreBas.W != w || VerreBas.H != hb)
            {
                if (VerreBas != null) VerreBas.Release();
                VerreBas = new PixelBuffer("verre du dock", w, hb);
            }
            if (VerreHaut == null || VerreHaut.W != w || VerreHaut.H != ht)
            {
                if (VerreHaut != null) VerreHaut.Release();
                VerreHaut = new PixelBuffer("verre du haut", w, ht);
            }
            // Au quart de la resolution : 16 fois moins de pixels (le flou complet coutait ~2 s)
            PixelBuffer.VerreRapide(Background, 0, Services.ScreenH - hb, VerreBas, 0, 0, w, hb, 3, Color.White, 150);
            PixelBuffer.VerreRapide(Background, 0, 0, VerreHaut, 0, 0, w, ht, 3, Color.FromArgb(18, 22, 36), 175);
            VersionFond++;
        }

        // ---- Fond "Vagues" : trois rubans bleus brillants sur un ciel clair (dessin original) ----

        // Quart d'onde du sinus : 1024 pas par tour, valeurs de 0 a 1024 (calcul en entiers)
        private static readonly int[] QuartSinus =
        {
            0, 6, 13, 19, 25, 31, 38, 44, 50, 57, 63, 69, 75, 82, 88, 94,
            100, 107, 113, 119, 125, 132, 138, 144, 150, 156, 163, 169, 175, 181, 187, 194,
            200, 206, 212, 218, 224, 230, 237, 243, 249, 255, 261, 267, 273, 279, 285, 291,
            297, 303, 309, 315, 321, 327, 333, 339, 345, 351, 357, 363, 369, 374, 380, 386,
            392, 398, 403, 409, 415, 421, 426, 432, 438, 443, 449, 455, 460, 466, 472, 477,
            483, 488, 494, 499, 505, 510, 516, 521, 526, 532, 537, 543, 548, 553, 558, 564,
            569, 574, 579, 584, 590, 595, 600, 605, 610, 615, 620, 625, 630, 635, 640, 645,
            650, 654, 659, 664, 669, 674, 678, 683, 688, 692, 697, 702, 706, 711, 715, 720,
            724, 729, 733, 737, 742, 746, 750, 755, 759, 763, 767, 771, 775, 779, 784, 788,
            792, 796, 799, 803, 807, 811, 815, 819, 822, 826, 830, 834, 837, 841, 844, 848,
            851, 855, 858, 862, 865, 868, 872, 875, 878, 882, 885, 888, 891, 894, 897, 900,
            903, 906, 909, 912, 915, 917, 920, 923, 926, 928, 931, 934, 936, 939, 941, 944,
            946, 948, 951, 953, 955, 958, 960, 962, 964, 966, 968, 970, 972, 974, 976, 978,
            980, 982, 983, 985, 987, 989, 990, 992, 993, 995, 996, 998, 999, 1000, 1002, 1003,
            1004, 1006, 1007, 1008, 1009, 1010, 1011, 1012, 1013, 1014, 1015, 1016, 1016, 1017, 1018, 1018,
            1019, 1020, 1020, 1021, 1021, 1022, 1022, 1022, 1023, 1023, 1023, 1024, 1024, 1024, 1024, 1024,
            1024
        };

        private static int Sin(int a)
        {
            a &= 1023;
            if (a < 256) return QuartSinus[a];
            if (a < 512) return QuartSinus[512 - a];
            if (a < 768) return -QuartSinus[a - 512];
            return -QuartSinus[1024 - a];
        }

        // Par ruban : rouge, vert, bleu, centre, amplitude, frequence, phase, epaisseur, frequence 2, phase 2
        private static readonly int[] Rubans =
        {
            118, 160, 242, 300, 95, 1100, 0, 90, 1700, 200,
            58, 108, 226, 520, 120, 800, 300, 120, 1300, 600,
            28, 76, 206, 760, 90, 1250, 650, 150, 900, 100
        };

        // ================================================================
        //  Fonds d'ecran
        //  - fonds simples (degrades) : dessines instantanement, aucun risque ;
        //  - vagues : calculees en arriere-plan ;
        //  - images (JPEG, PNG, BMP, GIF) : preparees UNE fois en arriere-plan, puis gardees
        //    pretes dans \FSOS\Systeme\fond-ecran.cache : les demarrages suivants sont rapides.
        // ================================================================

        public static readonly string[] Fonds = { "ciel", "nuit", "aurore", "lagon", "foret", "sable", "graphite", "vagues" };
        public static readonly string[] NomsFonds = { "Ciel", "Nuit", "Aurore", "Lagon", "Foret", "Sable", "Graphite", "Vagues" };

        // Trois couleurs par fond (haut, milieu, bas)
        private static readonly int[] Couleurs =
        {
            225, 235, 252,  205, 222, 250,  178, 200, 243,      // ciel
            36, 42, 74,     24, 28, 52,     12, 14, 28,         // nuit
            255, 214, 226,  214, 190, 250,  150, 160, 240,      // aurore
            196, 244, 236,  120, 214, 222,  40, 140, 190,       // lagon
            222, 242, 214,  150, 200, 150,  58, 118, 88,        // foret
            250, 240, 222,  238, 214, 180,  214, 180, 140,      // sable
            96, 102, 116,   60, 64, 76,     30, 32, 40,         // graphite
            225, 235, 252,  205, 222, 250,  178, 200, 243       // vagues : sur le ciel
        };

        /// <summary>Le fond choisi : un nom de fond simple, "vagues" ou le chemin d'une image.</summary>
        public static string Choix()
        {
            string c = FileSystemOS.Systeme.Config.Get("fond_ecran", "").Trim();
            return c.Length == 0 ? "ciel" : c;
        }

        public static int IndexFond(string c)
        {
            string n = c.ToLower();
            for (int i = 0; i < Fonds.Length; i++) if (Fonds[i] == n) return i;
            return -1;
        }

        /// <summary>Degrade du fond k dans un rectangle (aussi pour les vignettes des Parametres).</summary>
        public static void Degrade(PixelBuffer b, int k, int x, int y, int w, int h)
        {
            int o = k * 9;
            for (int j = 0; j < h; j++)
            {
                int t = j * 1000 / (h < 1 ? 1 : h), a, f;
                if (t < 500) { a = o; f = t * 2; } else { a = o + 3; f = (t - 500) * 2; }
                int r = Couleurs[a] + (Couleurs[a + 3] - Couleurs[a]) * f / 1000;
                int g = Couleurs[a + 1] + (Couleurs[a + 4] - Couleurs[a + 1]) * f / 1000;
                int bl = Couleurs[a + 2] + (Couleurs[a + 5] - Couleurs[a + 2]) * f / 1000;
                b.FillRect(Color.FromArgb(r, g, bl), x, y + j, w, 1);
            }
        }

        // ---- Image en fond d'ecran : preparee par etapes (thread "fond") ----

        private const int ERien = 0, ELireCache = 1, EDecoder = 2, ECouvrir = 3, EEcrireCache = 4;
        private static int etape;
        private static string imageChemin = "";
        private static bool annonce;
        private static JpegImage.Decodeur jpeg;
        private static PixelBuffer source;          // image decodee, avant sa mise a la taille de l'ecran
        private static int[] colonnes;              // colonne de l'image pour chaque colonne de l'ecran
        private static int srcY, srcH, ligneFond;   // zone de l'image gardee (hauteur), ligne en cours
        private static byte[] cache;                // contenu du fichier cache (lecture ou ecriture)
        private static int cacheDebut;

        /// <summary>Vrai tant qu'un fond (vagues ou image) se prepare.</summary>
        public static bool FondEnPreparation => vagueRuban >= 0 || etape != ERien;

        private static string CheminCache() =>
            TextCodec.PathJoin(TextCodec.PathJoin(TextCodec.PathJoin(FileSystemOS.Systeme.Live.Volume ?? "0:\\", "FSOS"), "Systeme"), "fond-ecran.cache");

        private static void AbandonnerImage()
        {
            etape = ERien;
            jpeg = null;
            if (source != null) { source.Release(); source = null; }
            cache = null;
            annonce = false;
        }

        private static void Annoncer(string m)
        {
            Raccourcis.Message = m;
            Raccourcis.MessageMillis = Horloge.Millis;
            Services.ScreenDirty = true;
        }

        /// <summary>Appele a chaque tour par le thread "fond" : vagues ou image, 15 ms au plus.</summary>
        public static void AvancerFond()
        {
            if (vagueRuban >= 0) { AvancerVagues(); return; }
            if (etape == ERien || Background == null) return;
            uint fin = Horloge.Millis + 15;
            try
            {
                if (etape == ELireCache) LireCache(fin);
                else if (etape == EDecoder) Decoder(fin);
                else if (etape == ECouvrir) Couvrir(fin);
                else if (etape == EEcrireCache) EcrireCache(fin);
            }
            catch (System.Exception e)
            {
                FileSystemOS.Noyau.Watchdog.Lent = false;
                string m = e.Message;
                AbandonnerImage();
                Degrade(Background, 0, 0, 0, Background.W, Background.H);
                WallpaperInfo = "Ciel (image illisible : " + m + ")";
                PreparerVerre();
                Rafraichir();
                Annoncer("Fond d'ecran : image illisible (" + m + ")");
                FileSystemOS.Noyau.Diagnostic.Report("fond", m);
            }
        }

        /// <summary>Copie toute prete : en-tete "FSFD", largeur, hauteur, chemin de l'image, puis les pixels.</summary>
        private static void LireCache(uint fin)
        {
            if (cache == null)
            {
                string c = CheminCache();
                if (!FsTools.Exists(c)) { etape = EDecoder; return; }
                FileSystemOS.Noyau.Watchdog.Lent = true;
                try { cache = FsTools.ReadBytes(c); }
                finally { FileSystemOS.Noyau.Watchdog.Lent = false; }
                if (!EnTeteValide()) { cache = null; etape = EDecoder; return; }
                ligneFond = 0;
                return;                                  // la recopie commence au tour suivant
            }
            int W = Background.W, H = Background.H;
            int[] d = Background.Data;
            while (ligneFond < H)
            {
                int o = cacheDebut + ligneFond * W * 4, k = ligneFond * W;
                for (int x = 0; x < W; x++, o += 4)
                    d[k + x] = unchecked((int)0xFF000000) | (cache[o + 2] << 16) | (cache[o + 1] << 8) | cache[o];
                ligneFond++;
                if ((ligneFond & 7) == 0 && Horloge.Millis >= fin) return;
            }
            cache = null;
            Termine();
        }

        private static bool EnTeteValide()
        {
            if (cache.Length < 14 || cache[0] != (byte)'F' || cache[1] != (byte)'S' || cache[2] != (byte)'F' || cache[3] != (byte)'D') return false;
            int w = cache[4] | cache[5] << 8 | cache[6] << 16 | cache[7] << 24;
            int h = cache[8] | cache[9] << 8 | cache[10] << 16 | cache[11] << 24;
            int n = cache[12] | cache[13] << 8;
            if (w != Background.W || h != Background.H || 14 + n > cache.Length) return false;
            string nom = "";
            for (int i = 0; i < n; i++) nom += (char)cache[14 + i];
            if (nom.ToLower() != imageChemin.ToLower()) return false;
            cacheDebut = 14 + n;
            return cache.Length >= cacheDebut + w * h * 4;
        }

        private static void Decoder(uint fin)
        {
            if (!annonce)
            {
                annonce = true;
                Annoncer("Preparation du fond d'ecran (une seule fois)...");
                return;                                  // le message s'affiche avant le travail
            }
            if (jpeg == null && source == null)
            {
                string n = imageChemin.ToLower();
                if (n.EndsWith(".jpg") || n.EndsWith(".jpeg"))
                {
                    FileSystemOS.Noyau.Watchdog.Lent = true;
                    try { jpeg = JpegImage.Commencer(FsTools.ReadBytes(imageChemin), "fond (image)"); }
                    finally { FileSystemOS.Noyau.Watchdog.Lent = false; }
                    return;
                }
                // PNG, GIF, BMP : decodes d'un bloc. Un grand PNG peut prendre plusieurs dizaines de
                // secondes sous Cosmos : le gardien est suspendu le temps de ce travail (une seule fois).
                bool actif = FileSystemOS.Noyau.Watchdog.Actif;
                FileSystemOS.Noyau.Watchdog.Lent = true;
                if (!n.EndsWith(".bmp")) FileSystemOS.Noyau.Watchdog.Actif = false;
                try { source = ImageLoader.Load(imageChemin); }
                finally { FileSystemOS.Noyau.Watchdog.Actif = actif; FileSystemOS.Noyau.Watchdog.Lent = false; }
                PreparerCouverture();
                return;
            }
            // JPEG : quelques blocs a la fois
            while (!jpeg.Avancer(4))
            {
                if (Horloge.Millis >= fin)
                {
                    if (Horloge.Millis - Raccourcis.MessageMillis > 1000) Annoncer("Preparation du fond d'ecran : " + jpeg.Progression + " %");
                    return;
                }
            }
            source = jpeg.Image;
            jpeg = null;
            PreparerCouverture();
        }

        /// <summary>Comme DrawCover : l'image remplit l'ecran sans deformation (les bords en trop sont coupes).</summary>
        private static void PreparerCouverture()
        {
            int W = Background.W, H = Background.H;
            int sw = source.W, sh = (int)((long)source.W * H / W);
            if (sh > source.H) { sh = source.H; sw = (int)((long)source.H * W / H); }
            int sx0 = (source.W - sw) / 2;
            srcY = (source.H - sh) / 2;
            srcH = sh;
            colonnes = new int[W];
            for (int x = 0; x < W; x++) colonnes[x] = sx0 + (int)((long)x * sw / W);
            ligneFond = 0;
            etape = ECouvrir;
        }

        private static void Couvrir(uint fin)
        {
            int W = Background.W, H = Background.H, sW = source.W;
            int[] d = Background.Data, s = source.Data, col = colonnes;
            while (ligneFond < H)
            {
                int sy = srcY + (int)((long)ligneFond * srcH / H);
                if (sy >= source.H) sy = source.H - 1;
                int ls = sy * sW, ld = ligneFond * W;
                for (int x = 0; x < W; x++) d[ld + x] = s[ls + col[x]] | unchecked((int)0xFF000000);
                ligneFond++;
                if ((ligneFond & 7) == 0 && Horloge.Millis >= fin) return;
            }
            source.Release();
            source = null;
            colonnes = null;
            // Copie toute prete pour les prochains demarrages
            int n = imageChemin.Length;
            cache = new byte[14 + n + W * H * 4];
            cache[0] = (byte)'F'; cache[1] = (byte)'S'; cache[2] = (byte)'F'; cache[3] = (byte)'D';
            cache[4] = (byte)W; cache[5] = (byte)(W >> 8); cache[6] = (byte)(W >> 16); cache[7] = (byte)(W >> 24);
            cache[8] = (byte)H; cache[9] = (byte)(H >> 8); cache[10] = (byte)(H >> 16); cache[11] = (byte)(H >> 24);
            cache[12] = (byte)n; cache[13] = (byte)(n >> 8);
            for (int i = 0; i < n; i++) cache[14 + i] = imageChemin[i] < 256 ? (byte)imageChemin[i] : (byte)'?';
            cacheDebut = 14 + n;
            ligneFond = 0;
            etape = EEcrireCache;
            Rafraichir();                                // l'image est deja visible
        }

        private static void EcrireCache(uint fin)
        {
            int W = Background.W, H = Background.H;
            int[] d = Background.Data;
            while (ligneFond < H)
            {
                int o = cacheDebut + ligneFond * W * 4, k = ligneFond * W;
                for (int x = 0; x < W; x++, o += 4)
                {
                    int c = d[k + x];
                    cache[o] = (byte)c; cache[o + 1] = (byte)(c >> 8); cache[o + 2] = (byte)(c >> 16); cache[o + 3] = 255;
                }
                ligneFond++;
                if ((ligneFond & 7) == 0 && Horloge.Millis >= fin) return;
            }
            FileSystemOS.Noyau.Watchdog.Lent = true;
            try { FsTools.WriteBytes(CheminCache(), cache); }
            catch (System.Exception) { }                 // sans copie, l'image sera simplement refaite
            finally { FileSystemOS.Noyau.Watchdog.Lent = false; }
            cache = null;
            Termine();
        }

        private static void Termine()
        {
            etape = ERien;
            WallpaperInfo = TextCodec.FileName(imageChemin);
            PreparerVerre();
            Rafraichir();
            if (annonce) Annoncer("Fond d'ecran pret");
            annonce = false;
        }

        private static void Rafraichir()
        {
            if (Services.Taskbar != null) Services.Taskbar.Invalidate();
            if (Services.TopBar != null) Services.TopBar.Dirty = true;
            Services.ScreenDirty = true;
        }

        // ---- Calcul des rubans en arriere-plan ----
        // Tables faites une fois : la lumiere selon la position dans le ruban (profil), et la couleur
        // selon la lumiere pour chaque ruban. Par pixel, il ne reste qu'une division et deux lectures.
        // Verifie en Python : image identique, pixel pour pixel, au calcul direct.

        private static int vagueRuban = -1, vagueColonne;     // -1 : rien a calculer
        private static int[] profil;
        private static int[][] couleursRuban;

        /// <summary>Vrai tant que les rubans du fond sont en cours de calcul.</summary>
        public static bool VaguesEnCours => vagueRuban >= 0;

        private static void DemarrerVagues()
        {
            if (profil == null)
            {
                profil = new int[1025];
                for (int s = 0; s <= 1024; s++)
                    profil[s] = s < 180 ? 250 + 300 * s / 180 : (s < 520 ? 550 - 550 * (s - 180) / 340 : -((s - 520) * 420 / 504));
                couleursRuban = new int[3][];
                for (int k = 0; k < 3; k++)
                {
                    int cr = Rubans[k * 10], cg = Rubans[k * 10 + 1], cb = Rubans[k * 10 + 2];
                    int[] t = new int[1301];
                    for (int i = 0; i < 1301; i++)
                    {
                        int v = i - 600, r, g, b;
                        if (v >= 0) { r = cr + (255 - cr) * v / 1000; g = cg + (255 - cg) * v / 1000; b = cb + (255 - cb) * v / 1000; }
                        else { int w = -v; r = cr - cr * w / 1000; g = cg - cg * w / 1000; b = cb - cb * w / 1000; }
                        t[i] = unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
                    }
                    couleursRuban[k] = t;
                }
            }
            vagueRuban = 0;
            vagueColonne = 0;
        }

        /// <summary>
        /// Appele a chaque tour par le thread "fond" : calcule des colonnes de rubans pendant 15 ms
        /// au plus, puis rend la main. A la fin, le verre et l'ecran sont rafraichis.
        /// </summary>
        public static void AvancerVagues()
        {
            if (vagueRuban < 0 || Background == null) return;
            uint fin = Horloge.Millis + 15;
            int W = Background.W;
            try
            {
                while (vagueRuban < 3)
                {
                    Colonne(vagueRuban, vagueColonne);
                    vagueColonne++;
                    if (vagueColonne >= W) { vagueColonne = 0; vagueRuban++; }
                    if ((vagueColonne & 15) == 0 && Horloge.Millis >= fin) return;
                }
            }
            catch (System.Exception e)
            {
                // Une erreur arrete le calcul (le ciel reste) au lieu de se repeter a chaque tour
                vagueRuban = -1;
                FileSystemOS.Noyau.Diagnostic.Report("fond", e.Message);
                return;
            }
            vagueRuban = -1;
            WallpaperInfo = "Vagues";
            PreparerVerre();
            Rafraichir();
        }

        /// <summary>Une colonne d'un ruban (memes calculs qu'avant, au seizieme de pixel).</summary>
        private static void Colonne(int k, int x)
        {
            PixelBuffer bg = Background;
            int W = bg.W, H = bg.H, o = k * 10;
            int cen = Rubans[o + 3], amp = Rubans[o + 4], f1 = Rubans[o + 5], p1 = Rubans[o + 6];
            int ep = Rubans[o + 7], f2 = Rubans[o + 8], p2 = Rubans[o + 9];
            int a1 = x * f1 / W + p1, a2 = x * f2 / W + p2;
            int yc = H * cen * 16 / 1000 + H * amp * 16 / 1000 * Sin(a1) / 1024 + H * amp * 16 / 3000 * Sin(2 * a1 + 137) / 1024;
            int tt = H * ep * 16 / 1000 * (3072 + Sin(a2)) / 4096;          // demi-epaisseur, en seiziemes
            if (tt < 64) return;
            int lum = Sin(a1 + 256) * 60 / 1024;                              // reflet le long du ruban
            int y0 = (yc - tt) / 16, y1 = (yc + tt) / 16 + 1;
            if (y0 < 0) y0 = 0;
            if (y1 > H - 1) y1 = H - 1;
            int[] d = bg.Data, coul = couleursRuban[k], prof = profil;
            int deuxT = 2 * tt;
            for (int y = y0; y <= y1; y++)
            {
                int dd = y * 16 + 8 - yc;
                if (dd <= -tt || dd >= tt) continue;
                int v = prof[(dd + tt) * 1024 / deuxT] + lum;
                if (v > 700) v = 700;
                else if (v < -600) v = -600;
                int c = coul[v + 600];
                int idx = y * W + x;
                int bord = tt - (dd < 0 ? -dd : dd);
                if (bord < 24)                                                // bord adouci sur 1,5 pixel
                {
                    int a = bord * 255 / 24, od = d[idx], inv = 255 - a;
                    int r = (((c >> 16) & 255) * a + ((od >> 16) & 255) * inv) / 255;
                    int g = (((c >> 8) & 255) * a + ((od >> 8) & 255) * inv) / 255;
                    int b = ((c & 255) * a + (od & 255) * inv) / 255;
                    c = unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
                }
                d[idx] = c;
            }
        }

        /// <summary>Change le fond d'ecran (nom d'un fond simple, "vagues" ou chemin d'une image) et l'enregistre.</summary>
        public static string SetWallpaper(string path)
        {
            string r = FileSystemOS.Systeme.Config.SetAndSave("interface.cfg", "fond_ecran", path);
            Build();
            Services.ScreenDirty = true;
            return r;
        }
    }
}
