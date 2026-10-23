using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Cosmos.System.FileSystem.Listing;
using Cosmos.System.FileSystem.VFS;
using FileSystemOS.Processus;
using FileSystemOS.Systeme;
using FileSystemOS.Utils;

namespace FileSystemOS.Graphique
{
    /// <summary>Une icone posee sur le bureau.</summary>
    public class IconeBureau
    {
        public const int App = 0, Fichier = 1, Dossier = 2, Corbeille = 3;

        public string Cle = "";         // "app:console", "fichier:Notes.txt", "corbeille"
        public string Nom = "";         // texte sous l'icone
        public string Chemin = "";      // fichier ou dossier reel (pour Fichier / Dossier)
        public int Type;
        public int X, Y;
        public HexBitmap Image;
    }

    /// <summary>
    /// LE BUREAU : icones deplacables, menu du clic droit, corbeille.
    ///
    ///  - Raccourcis d'applications (Explorateur, Console, Calculatrice...) : clic droit ->
    ///    "Retirer l'icone" ; on peut les remettre par "Restaurer les icones d'applications".
    ///  - Fichiers et dossiers du dossier Bureau de l'utilisateur (\FSOS\Utilisateurs\nom\Bureau) :
    ///    clic droit -> "Supprimer" les envoie a la CORBEILLE (\...\Corbeille), d'ou on peut
    ///    les restaurer ou les supprimer definitivement.
    ///  - Clic droit sur le fond : nouveau fichier texte, nouveau dossier, corbeille, actualiser.
    ///  - Glisser une icone la deplace ; sa position est enregistree (bureau.cfg).
    ///  - Double-clic : ouvre l'application, le fichier (bon lecteur) ou le dossier.
    /// </summary>
    public static class Bureau
    {
        public const int Case = 96;                 // pas de la grille (12 caracteres par ligne)
        public const int TailleIcone = 32;          // icones hexa 16x16 agrandies x2

        public static readonly List<IconeBureau> Icones = new List<IconeBureau>();
        private static readonly List<string> masquees = new List<string>();
        private static IconeBureau selection;

        // ---------------- Glisser / double-clic ----------------
        private static IconeBureau tenue;
        private static int prisDx, prisDy, departX, departY;
        private static bool deplacee;
        private static IconeBureau dernierClic;
        private static uint dernierTemps;
        private static uint tours;

        // ---------------- Menu contextuel ----------------
        public static bool MenuOuvert;
        public static int MenuX, MenuY;
        private static readonly List<string> menuTextes = new List<string>();
        private static readonly List<int> menuActions = new List<int>();
        private static IconeBureau menuCible;
        private static int menuSurvol = -1;
        private const int MenuLigne = 24, MenuMarge = 6;

        private const int ActOuvrir = 1, ActSupprimer = 2, ActRetirer = 3, ActNouveauFichier = 4,
                          ActNouveauDossier = 5, ActCorbeille = 6, ActRestaurerApps = 7, ActActualiser = 8,
                          ActParametres = 9, ActViderCorbeille = 10, ActRaccourci = 11;

        public static string Message = "";

        // ================================================================
        //  Applications connues
        // ================================================================
        private static readonly string[] AppCles =
            { "explorateur", "console", "editeur", "calculatrice", "serpent", "navigateur", "parametres", "taches", "capture", "createur" };
        private static readonly string[] AppNoms =
            { "Explorateur", "Console", "Editeur", "Calculatrice", "Serpent", "Navigateur", "Parametres", "Taches", "Capture", "Createur" };

        private static HexBitmap IconeApp(string cle)
        {
            switch (cle)
            {
                case "explorateur": return Icone32(Icons.Folder);
                case "console": return Icone32(Icons.ConsoleIcon);
                case "parametres": return Icone32(Icons.SettingsIcon);
                case "editeur": return Icone32(Icons.EditorIcon);
                case "calculatrice": return Icone32(Icons.CalcIcon);
                case "serpent": return Icone32(Icons.GameIcon);
                case "navigateur": return Icone32(Icons.BrowserIcon);
                case "taches": return Icone32(Icons.TaskIcon);
                case "capture": return Icone32(Icons.CaptureIcon);
                case "createur": return Icone32(Icons.EditorIcon);
                default: return Icone32(Icons.FslApp);
            }
        }

        private static void Lancer(string cle)
        {
            var wm = Services.Windows;
            switch (cle)
            {
                case "explorateur": wm.Open(Services.ExplorerWin); break;
                case "console": wm.Open(Services.ConsoleWin); break;
                case "editeur": wm.Open(Services.EditorWin); break;
                case "calculatrice": wm.Open(Services.CalcWin); break;
                case "serpent": wm.Open(Services.SerpentWin); break;
                case "navigateur": wm.Open(Services.BrowserWin); break;
                case "parametres": wm.Open(Services.DisplayWin); break;
                case "taches": wm.Open(Services.TacheWin); break;
                case "capture": wm.Open(Services.CaptureWin); break;
                case "createur": wm.Open(Services.StudioWin); break;
            }
        }

        /// <summary>Icone de bureau : la version 32x32 dessinee si elle existe, sinon la 16x16 agrandie.</summary>
        public static HexBitmap Icone32(string[] petite)
        {
            string[] grande = Icons.Grand(petite);
            uint[] pal = Icons.PaletteDe(petite);
            return grande != null ? new HexBitmap(grande, pal) : Grossir(petite, pal);
        }

        /// <summary>Agrandit une icone 16x16 en 32x32 (chaque pixel devient un carre de 2x2).</summary>
        public static HexBitmap Grossir(string[] lignes, uint[] palette)
        {
            string[] r = new string[lignes.Length * 2];
            for (int y = 0; y < lignes.Length; y++)
            {
                char[] c = new char[lignes[y].Length * 2];
                for (int x = 0; x < lignes[y].Length; x++) { c[x * 2] = lignes[y][x]; c[x * 2 + 1] = lignes[y][x]; }
                string l = new string(c);
                r[y * 2] = l;
                r[y * 2 + 1] = l;
            }
            return new HexBitmap(r, palette);
        }

        // ================================================================
        //  Dossiers de l'utilisateur
        // ================================================================
        public static string DossierBureau()
        {
            string h = Comptes.Home();
            return h == null ? null : h + "\\Bureau";
        }

        public static string DossierCorbeille()
        {
            string h = Comptes.Home();
            return h == null ? null : h + "\\Corbeille";
        }

        private static string FichierConfig()
        {
            string h = Comptes.Home();
            return h == null ? null : h + "\\bureau.cfg";
        }

        private static void Creer(string d)
        {
            try { if (d != null) FileSystemOS.Utils.FsTools.MakeDir(d); } catch (Exception) { }
        }

        // ================================================================
        //  Chargement et enregistrement
        // ================================================================

        /// <summary>Construit le bureau de l'utilisateur connecte (appele a la connexion).</summary>
        public static void Charger()
        {
            string h = Comptes.Home();
            Creer(h);
            Creer(DossierBureau());
            Creer(DossierCorbeille());

            // Positions et icones retirees, lues dans bureau.cfg  (cle|x|y   ou   masque|cle)
            var positions = new List<string>();
            masquees.Clear();
            string cfg = FichierConfig();
            try
            {
                if (cfg != null && File.Exists(cfg))
                {
                    var l = TextCodec.ReadLines(cfg);
                    for (int i = 0; i < l.Count; i++)
                    {
                        if (l[i].StartsWith("masque|")) masquees.Add(l[i].Substring(7));
                        else if (l[i].Length > 0 && l[i][0] != '#') positions.Add(l[i]);
                    }
                }
            }
            catch (Exception) { }

            Icones.Clear();
            for (int i = 0; i < AppCles.Length; i++)
            {
                string cle = "app:" + AppCles[i];
                if (FileSystemOS.Utils.Listes.Contient(masquees, cle)) continue;
                var ic = new IconeBureau();
                ic.Cle = cle; ic.Nom = AppNoms[i]; ic.Type = IconeBureau.App;
                ic.Image = IconeApp(AppCles[i]);
                Icones.Add(ic);
            }

            // Fichiers et dossiers reels du dossier Bureau
            string bureau = DossierBureau();
            try
            {
                if (bureau != null && Directory.Exists(bureau))
                {
                    var l = VFSManager.GetDirectoryListing(bureau);
                    for (int i = 0; i < l.Count; i++)
                    {
                        var ic = new IconeBureau();
                        bool dossier = l[i].mEntryType == DirectoryEntryTypeEnum.Directory;
                        ic.Nom = l[i].mName;
                        ic.Chemin = bureau + "\\" + l[i].mName;
                        ic.Type = dossier ? IconeBureau.Dossier : IconeBureau.Fichier;
                        ic.Cle = (dossier ? "dossier:" : "fichier:") + l[i].mName;
                        ic.Image = dossier ? Icone32(Icons.Folder) : IconeFichier(l[i].mName);
                        Icones.Add(ic);
                    }
                }
            }
            catch (Exception) { }

            // La corbeille, toujours presente
            var corb = new IconeBureau();
            corb.Cle = "corbeille"; corb.Type = IconeBureau.Corbeille;
            corb.Nom = "Corbeille";
            corb.Image = Icone32(Icons.TrashIcon);
            Icones.Add(corb);

            // Positions : celles enregistrees, sinon la prochaine place libre de la grille
            for (int i = 0; i < Icones.Count; i++)
            {
                IconeBureau ic = Icones[i];
                bool place = false;
                for (int k = 0; k < positions.Count && !place; k++)
                {
                    string[] m = positions[k].Split('|');
                    if (m.Length == 3 && m[0] == ic.Cle)
                    {
                        ic.X = Fmt.ParseInt(m[1]); ic.Y = Fmt.ParseInt(m[2]);
                        place = true;
                    }
                }
                if (!place) PlaceLibre(ic);
                Borner(ic);
            }
            NombreCorbeille();
            Services.ScreenDirty = true;
        }

        private static HexBitmap IconeFichier(string nom)
        {
            string n = nom.ToLower();
            if (n.EndsWith(".fsl") || n.EndsWith(".fsc") || n.EndsWith(".fsp")) return Icone32(Icons.FslApp);
            if (n.EndsWith(".pdf")) return Icone32(Icons.PdfIcon);
            if (n.EndsWith(".png") || n.EndsWith(".bmp") || n.EndsWith(".jpg") || n.EndsWith(".jpeg") || n.EndsWith(".gif"))
                return Icone32(Icons.ImageIcon);
            return Icone32(Icons.TextFile);
        }

        private static int HautUtile => TopBar.H + 12;
        private static int BasUtile => Services.ScreenH - Desktop.TaskbarH - Case;

        /// <summary>Premiere case libre de la grille, colonne par colonne.</summary>
        private static void PlaceLibre(IconeBureau ic)
        {
            int lignes = (BasUtile - HautUtile) / Case + 1;
            if (lignes < 1) lignes = 1;
            for (int n = 0; n < 400; n++)
            {
                int x = 12 + (n / lignes) * Case, y = HautUtile + (n % lignes) * Case;
                bool prise = false;
                for (int i = 0; i < Icones.Count; i++)
                {
                    IconeBureau o = Icones[i];
                    if (o == ic) continue;
                    if (o.X == x && o.Y == y && (o.X != 0 || o.Y != 0)) prise = true;
                }
                if (!prise) { ic.X = x; ic.Y = y; return; }
            }
            ic.X = 12; ic.Y = HautUtile;
        }

        private static void Borner(IconeBureau ic)
        {
            if (ic.X < 0) ic.X = 0;
            if (ic.Y < HautUtile - 8) ic.Y = HautUtile - 8;
            if (ic.X > Services.ScreenW - Case) ic.X = Services.ScreenW - Case;
            if (ic.Y > BasUtile) ic.Y = BasUtile;
        }

        public static void Enregistrer()
        {
            string cfg = FichierConfig();
            if (cfg == null) return;
            try
            {
                var l = new List<string>();
                l.Add("# Bureau de " + (Comptes.Actuel == null ? "?" : Comptes.Actuel.Nom) + " : cle|x|y, et icones retirees");
                for (int i = 0; i < Icones.Count; i++) l.Add(Icones[i].Cle + "|" + Icones[i].X + "|" + Icones[i].Y);
                for (int i = 0; i < masquees.Count; i++) l.Add("masque|" + masquees[i]);
                FsTools.WriteBytes(cfg, TextCodec.Encode(l));
                Live.SaveIfActive();
            }
            catch (Exception) { }
        }

        // ================================================================
        //  Dessin (appele par le compositeur, juste apres le fond d'ecran)
        // ================================================================
        private static int elementsCorbeille;

        private static void NombreCorbeille()
        {
            elementsCorbeille = 0;
            try
            {
                string c = DossierCorbeille();
                if (c != null && Directory.Exists(c)) elementsCorbeille = VFSManager.GetDirectoryListing(c).Count;
            }
            catch (Exception) { }
        }

        public static void Dessiner(PixelBuffer b)
        {
            if (!Services.LoggedIn) return;
            for (int i = 0; i < Icones.Count; i++)
            {
                IconeBureau ic = Icones[i];
                if (ic == selection)
                    b.FillRoundRect(Theme.MenuHover, ic.X + 2, ic.Y - 2, Case - 4, Case - 6, 8);

                int ix = ic.X + (Case - TailleIcone) / 2, iy = ic.Y + 6;
                ic.Image.Draw(b, ix, iy);
                if (ic.Type == IconeBureau.Corbeille && elementsCorbeille > 0)
                {
                    // Pastille : nombre d'elements dans la corbeille
                    string n = elementsCorbeille > 99 ? "99" : elementsCorbeille.ToString();
                    b.FillRoundRect(Theme.Red, ix + 22, iy - 4, n.Length * 8 + 6, 16, 7);
                    b.DrawText(n, Theme.TitleText, ix + 25, iy - 4);
                }

                // Nom sur deux lignes au plus, coupe a un espace si possible, avec une ombre
                // pour rester lisible sur tout fond d'ecran
                string t1 = ic.Nom, t2 = "";
                int max = Case / 8;
                if (t1.Length > max)
                {
                    int coupe = t1.LastIndexOf(' ', max);
                    if (coupe <= 0) coupe = max;
                    t2 = t1.Substring(coupe).Trim();
                    t1 = t1.Substring(0, coupe).Trim();
                    if (t2.Length > max) t2 = t2.Substring(0, max - 1) + ".";
                }
                Texte(b, t1, ic.X, iy + TailleIcone + 4);
                if (t2.Length > 0) Texte(b, t2, ic.X, iy + TailleIcone + 20);
            }
        }

        private static void Texte(PixelBuffer b, string t, int x0, int y)
        {
            int x = x0 + (Case - t.Length * 8) / 2;
            // Couleur selon le fond sous le nom (un seul pixel lu) : texte sombre sur un fond clair,
            // texte clair sur un fond sombre. Rien de plus a calculer a chaque image.
            bool clair = false;
            var fond = Desktop.Background;
            if (fond != null)
            {
                int px = x0 + Case / 2, py = y + 8;
                if (px >= 0 && py >= 0 && px < fond.W && py < fond.H)
                {
                    int c = fond.Data[py * fond.W + px];
                    clair = ((c >> 16) & 255) * 3 + ((c >> 8) & 255) * 6 + (c & 255) > 1500;   // luminosite > ~150
                }
            }
            if (clair)
            {
                b.DrawText(t, Color.FromArgb(255, 255, 255), x + 1, y + 1);
                b.DrawText(t, Color.FromArgb(22, 28, 46), x, y);
            }
            else
            {
                b.DrawText(t, Color.FromArgb(10, 10, 16), x + 1, y + 1);
                b.DrawText(t, Color.FromArgb(240, 242, 250), x, y);
            }
        }

        public static void DessinerMenu(PixelBuffer b)
        {
            if (!MenuOuvert) return;
            int w = MenuLargeur, h = menuTextes.Count * MenuLigne + MenuMarge * 2;
            b.Darken(MenuX + 3, MenuY + 4, w, h);
            b.FillRoundRect(Theme.MenuBorder, MenuX - 1, MenuY - 1, w + 2, h + 2, 8);
            b.FillRoundRect(Theme.MenuBg, MenuX, MenuY, w, h, 8);
            for (int i = 0; i < menuTextes.Count; i++)
            {
                int y = MenuY + MenuMarge + i * MenuLigne;
                if (menuTextes[i].Length == 0)
                {
                    b.FillRect(Theme.TaskbarSep, MenuX + 10, y + MenuLigne / 2, w - 20, 1);
                    continue;
                }
                if (i == menuSurvol) b.FillRoundRect(Theme.MenuHover, MenuX + 4, y, w - 8, MenuLigne - 2, 6);
                Color c = menuActions[i] == ActSupprimer || menuActions[i] == ActRetirer || menuActions[i] == ActViderCorbeille
                          ? Color.FromArgb(255, 140, 140) : Theme.TaskbarText;
                b.DrawText(menuTextes[i], c, MenuX + 14, y + 3);
            }
        }

        private static int MenuLargeur
        {
            get
            {
                int m = 0;
                for (int i = 0; i < menuTextes.Count; i++) if (menuTextes[i].Length > m) m = menuTextes[i].Length;
                return m * 8 + 30;
            }
        }

        // ================================================================
        //  Souris (appele par le gestionnaire de fenetres, hors des fenetres)
        // ================================================================
        private static IconeBureau IconeA(int mx, int my)
        {
            for (int i = Icones.Count - 1; i >= 0; i--)
            {
                IconeBureau ic = Icones[i];
                if (mx >= ic.X && mx < ic.X + Case && my >= ic.Y - 2 && my < ic.Y + Case - 8) return ic;
            }
            return null;
        }

        private static uint Maintenant()
        {
            tours++;
            return Horloge.Active && Horloge.Millis > 0 ? Horloge.Millis : tours * 10;
        }

        public static void Appui(int mx, int my)
        {
            uint t = Maintenant();
            IconeBureau ic = IconeA(mx, my);
            selection = ic;
            Services.ScreenDirty = true;
            if (ic == null) { dernierClic = null; return; }

            // Double-clic : meme icone, moins d'une demi-seconde
            if (ic == dernierClic && t - dernierTemps < 500)
            {
                dernierClic = null;
                Ouvrir(ic);
                return;
            }
            dernierClic = ic;
            dernierTemps = t;

            tenue = ic;
            prisDx = mx - ic.X; prisDy = my - ic.Y;
            departX = ic.X; departY = ic.Y;
            deplacee = false;
        }

        public static void Glisser(int mx, int my)
        {
            if (tenue == null) return;
            int nx = mx - prisDx, ny = my - prisDy;
            if (!deplacee && Math.Abs(nx - departX) + Math.Abs(ny - departY) < 6) return;   // petit tremblement
            deplacee = true;
            tenue.X = nx; tenue.Y = ny;
            Borner(tenue);
            Services.ScreenDirty = true;
        }

        public static void Relache(int mx, int my)
        {
            if (tenue != null && deplacee)
            {
                // On aimante l'icone sur la grille, et on evite de la poser sur une autre
                tenue.X = 12 + (tenue.X - 12 + Case / 2) / Case * Case;
                tenue.Y = HautUtile + (tenue.Y - HautUtile + Case / 2) / Case * Case;
                Borner(tenue);
                for (int i = 0; i < Icones.Count; i++)
                {
                    IconeBureau o = Icones[i];
                    if (o != tenue && o.X == tenue.X && o.Y == tenue.Y) { PlaceLibre(tenue); break; }
                }
                Enregistrer();
                dernierClic = null;       // un glisser n'est pas la moitie d'un double-clic
                Services.ScreenDirty = true;
            }
            tenue = null;
        }

        // ---------------- Menu du clic droit ----------------

        private static void Item(string texte, int action) { menuTextes.Add(texte); menuActions.Add(action); }

        public static void OuvrirMenu(int mx, int my)
        {
            if (!Services.LoggedIn) return;
            menuTextes.Clear();
            menuActions.Clear();
            menuCible = IconeA(mx, my);
            selection = menuCible;

            if (menuCible == null)
            {
                Item("Nouveau fichier texte", ActNouveauFichier);
                Item("Nouveau dossier", ActNouveauDossier);
                Item("", 0);
                Item("Ouvrir la corbeille", ActCorbeille);
                Item("Restaurer les icones d'applications", ActRestaurerApps);
                Item("Actualiser", ActActualiser);
                Item("", 0);
                Item("Parametres", ActParametres);
            }
            else if (menuCible.Type == IconeBureau.App)
            {
                Item("Ouvrir", ActOuvrir);
                Item("", 0);
                Item("Retirer l'icone du bureau", ActRetirer);
            }
            else if (menuCible.Type == IconeBureau.Corbeille)
            {
                Item("Ouvrir la corbeille", ActCorbeille);
                Item("Vider la corbeille", ActViderCorbeille);
            }
            else
            {
                Item("Ouvrir", ActOuvrir);
                Item("", 0);
                Item("Mettre a la corbeille", ActSupprimer);
            }

            MenuX = mx; MenuY = my;
            int w = MenuLargeur, h = menuTextes.Count * MenuLigne + MenuMarge * 2;
            if (MenuX + w > Services.ScreenW - 4) MenuX = Services.ScreenW - 4 - w;
            if (MenuY + h > Services.ScreenH - Desktop.TaskbarH - 4) MenuY = Services.ScreenH - Desktop.TaskbarH - 4 - h;
            menuSurvol = -1;
            MenuOuvert = true;
            Services.ScreenDirty = true;
        }

        public static void FermerMenu()
        {
            if (!MenuOuvert) return;
            MenuOuvert = false;
            Services.ScreenDirty = true;
        }

        private static int LigneMenu(int mx, int my)
        {
            if (mx < MenuX || mx >= MenuX + MenuLargeur) return -1;
            int i = (my - MenuY - MenuMarge) / MenuLigne;
            if (my < MenuY + MenuMarge || i < 0 || i >= menuTextes.Count) return -1;
            return menuTextes[i].Length == 0 ? -1 : i;
        }

        public static void SurvolMenu(int mx, int my)
        {
            int s = LigneMenu(mx, my);
            if (s != menuSurvol) { menuSurvol = s; Services.ScreenDirty = true; }
        }

        /// <summary>Clic pendant que le menu est ouvert. Renvoie vrai si le clic etait DANS le menu.</summary>
        public static bool ClicMenu(int mx, int my)
        {
            bool dedans = mx >= MenuX && mx < MenuX + MenuLargeur && my >= MenuY &&
                          my < MenuY + menuTextes.Count * MenuLigne + MenuMarge * 2;
            int i = LigneMenu(mx, my);
            FermerMenu();
            if (i >= 0) Executer(menuActions[i]);
            return dedans;
        }

        // ================================================================
        //  Actions
        // ================================================================
        private static void Executer(int action)
        {
            try
            {
                switch (action)
                {
                    case ActOuvrir: if (menuCible != null) Ouvrir(menuCible); break;
                    case ActRetirer: Retirer(menuCible); break;
                    case ActSupprimer: MettreALaCorbeille(menuCible); break;
                    case ActNouveauFichier: NouveauFichier(); break;
                    case ActNouveauDossier: NouveauDossier(); break;
                    case ActCorbeille: Services.Windows.Open(Services.CorbeilleWin); break;
                    case ActViderCorbeille: Services.CorbeilleWin.Vider(); Services.Windows.Open(Services.CorbeilleWin); break;
                    case ActRestaurerApps: masquees.Clear(); Enregistrer(); Charger(); Message = "Icones d'applications restaurees"; break;
                    case ActActualiser: Charger(); break;
                    case ActParametres: Services.Windows.Open(Services.DisplayWin); break;
                }
            }
            catch (Exception e) { Message = "Erreur : " + e.Message; }
            if (Message.Length > 0) { try { Services.ConsoleWin.Print("Bureau : " + Message); } catch (Exception) { } Message = ""; }
        }

        public static void Ouvrir(IconeBureau ic)
        {
            switch (ic.Type)
            {
                case IconeBureau.App: Lancer(ic.Cle.Substring(4)); break;
                case IconeBureau.Corbeille: Services.Windows.Open(Services.CorbeilleWin); break;
                case IconeBureau.Dossier:
                    Services.ExplorerWin.Navigate(ic.Chemin, true);
                    Services.Windows.Open(Services.ExplorerWin);
                    break;
                default: OuvrirFichier(ic.Chemin); break;
            }
        }

        /// <summary>Ouvre un fichier avec le bon programme, comme l'explorateur.</summary>
        public static void OuvrirFichier(string chemin)
        {
            string n = chemin.ToLower();
            if (n.EndsWith(".fsc")) { FileSystemOS.Fsl.Launcher.Run(chemin, out string m); Message = m; }   // programme compile
            else if (n.EndsWith(".pdf")) { Services.PdfWin.Load(chemin); Services.Windows.Open(Services.PdfWin); }
            else if (n.EndsWith(".fsp")) { Services.StudioWin.Charger(chemin); Services.Windows.Open(Services.StudioWin); }
            else if (ImageLoader.IsImage(chemin)) { Services.ImageWin.Load(chemin); Services.Windows.Open(Services.ImageWin); }
            else { Services.EditorWin.Load(chemin); Services.Windows.Open(Services.EditorWin); }
        }

        private static void Retirer(IconeBureau ic)
        {
            if (ic == null || ic.Type != IconeBureau.App) return;
            if (!FileSystemOS.Utils.Listes.Contient(masquees, ic.Cle)) masquees.Add(ic.Cle);
            Icones.Remove(ic);
            selection = null;
            Enregistrer();
            Message = "Icone retiree : " + ic.Nom + " (clic droit sur le bureau pour la restaurer)";
            Services.ScreenDirty = true;
        }

        /// <summary>Nom libre dans un dossier : "Nouveau fichier.txt", "Nouveau fichier (2).txt"...</summary>
        private static string NomLibre(string dossier, string baseNom, string ext)
        {
            for (int n = 1; n < 1000; n++)
            {
                string nom = baseNom + (n == 1 ? "" : " (" + n + ")") + ext;
                string p = dossier + "\\" + nom;
                if (!File.Exists(p) && !Directory.Exists(p)) return nom;
            }
            return baseNom + " " + Maintenant() + ext;
        }

        private static void NouveauFichier()
        {
            string d = DossierBureau();
            if (d == null) throw new Exception("aucun disque pour le bureau");
            Creer(d);
            string nom = NomLibre(d, "Nouveau fichier", ".txt");
            FsTools.WriteBytes(d + "\\" + nom, new byte[0]);
            Live.SaveIfActive();
            Charger();
            Services.EditorWin.Load(d + "\\" + nom);
            Services.Windows.Open(Services.EditorWin);
            Message = "Fichier cree sur le bureau : " + nom;
        }

        private static void NouveauDossier()
        {
            string d = DossierBureau();
            if (d == null) throw new Exception("aucun disque pour le bureau");
            Creer(d);
            string nom = NomLibre(d, "Nouveau dossier", "");
            FileSystemOS.Utils.FsTools.MakeDir(d + "\\" + nom);
            Live.SaveIfActive();
            Charger();
            Message = "Dossier cree sur le bureau : " + nom;
        }

        // ---------------- Corbeille ----------------

        /// <summary>
        /// Deplace un fichier (ou un dossier, recursivement) vers la corbeille.
        /// Copie puis suppression : le systeme de fichiers de Cosmos n'a pas de "deplacer" fiable.
        /// </summary>
        public static void MettreALaCorbeille(IconeBureau ic)
        {
            if (ic == null || (ic.Type != IconeBureau.Fichier && ic.Type != IconeBureau.Dossier)) return;
            string c = DossierCorbeille();
            Creer(c);
            string nom = NomLibre(c, SansExtension(ic.Nom), Extension(ic.Nom));
            Deplacer(ic.Chemin, c + "\\" + nom, ic.Type == IconeBureau.Dossier);
            Live.SaveIfActive();
            Charger();
            Message = "'" + ic.Nom + "' mis a la corbeille";
        }

        public static string SansExtension(string n) { int p = n.LastIndexOf('.'); return p <= 0 ? n : n.Substring(0, p); }
        public static string Extension(string n) { int p = n.LastIndexOf('.'); return p <= 0 ? "" : n.Substring(p); }

        /// <summary>Deplacement par copie + suppression (fichiers et dossiers).</summary>
        public static void Deplacer(string source, string dest, bool dossier)
        {
            if (!dossier)
            {
                byte[] data = File.ReadAllBytes(source);
                FsTools.WriteBytes(dest, data);
                FileSystemOS.Utils.FsTools.DeleteFile(source);
                return;
            }
            FileSystemOS.Utils.FsTools.MakeDir(dest);
            var l = VFSManager.GetDirectoryListing(source);
            for (int i = 0; i < l.Count; i++)
                Deplacer(source + "\\" + l[i].mName, dest + "\\" + l[i].mName,
                         l[i].mEntryType == DirectoryEntryTypeEnum.Directory);
            FsTools.DeleteTree(source);
        }

        public static void Rafraichir() { NombreCorbeille(); Services.ScreenDirty = true; }
    }
}
