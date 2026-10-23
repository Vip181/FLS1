using System;
using System.Collections.Generic;
using System.Drawing;
using Cosmos.System;
using Cosmos.System.FileSystem.Listing;
using Cosmos.System.FileSystem.VFS;
using FileSystemOS.Fsl;

namespace FileSystemOS.Graphique
{
    /// <summary>
    /// BARRE DES TACHES CLASSIQUE et MENU DEMARRER A TROIS COLONNES (surfaces pleines, sans
    /// transparence a calculer : le plus leger possible pour Cosmos).
    ///
    /// Barre : le F (menu Demarrer), le lancement rapide, un onglet par fenetre, l'heure.
    /// Menu : a gauche le compte et les lieux, au milieu les categories, a droite les
    /// applications avec leur description ; en bas Verrouiller, Se deconnecter, Eteindre et la
    /// recherche (on tape : Entree lance le premier resultat, Echap efface puis ferme).
    /// La partie fixe du menu est calculee a l'ouverture (et quand la categorie ou la recherche
    /// change) ; le survol ne fait que recopier cette image et redessiner une ligne.
    /// </summary>
    public class Taskbar
    {
        public const int H = Desktop.TaskbarH;
        public const int MenuW = 600;

        // Couleurs (claires et pleines)
        private static readonly Color Fond = Color.FromArgb(237, 239, 243);
        private static readonly Color Bord = Color.FromArgb(204, 208, 216);
        private static readonly Color Texte = Color.FromArgb(30, 34, 44);
        private static readonly Color Discret = Color.FromArgb(100, 106, 120);
        private static readonly Color Survol = Color.FromArgb(220, 225, 235);
        private static readonly Color Onglet = Color.FromArgb(228, 231, 237);
        private static readonly Color Actif = Color.FromArgb(210, 218, 238);
        private static readonly Color Indigo = Color.FromArgb(58, 108, 226);
        private static readonly Color MenuFond = Color.FromArgb(250, 251, 253);
        private static readonly Color Colonne = Color.FromArgb(241, 243, 247);
        private static readonly Color Choisi = Color.FromArgb(224, 232, 250);
        private static readonly Color TuileSombre = Color.FromArgb(38, 42, 60);

        private const int Lieu = 1, Categorie = 2, App = 3, Bouton = 4, Recherche = 5;
        private const int GaucheW = 150, MilieuX = 150, MilieuW = 186, DroiteX = 336;

        /// <summary>Une entree : application, lieu, categorie, bouton ou champ de recherche.</summary>
        private class Appli
        {
            public string Nom = "", Cle = "", Chemin = "", Description = "";
            public HexBitmap Icone, Petite;      // 32 x 32 et 16 x 16
            public bool Claire;                  // icone blanche : posee sur une tuile sombre
            public Window Fenetre;
            public int Categories, Index, Genre;
            public bool Compacte;                // ligne courte (icone 16) dans la colonne de droite
            public int X, Y, W, H;
        }

        private static readonly string[] NomsCategories =
            { "Toutes les applis", "Accessoires", "Bureautique", "Graphisme", "Internet", "Jeux", "Programmation", "Systeme", "Mes programmes" };
        private const int Toutes = 0, MesProgrammes = 8;

        private readonly List<Appli> applis = new List<Appli>();
        private readonly List<Appli> programmes = new List<Appli>();
        private readonly List<Appli> categories = new List<Appli>();
        private readonly List<Appli> lieux = new List<Appli>();
        private readonly List<Appli> boutons = new List<Appli>();
        private readonly List<Appli> rapides = new List<Appli>();     // lancement rapide de la barre
        private readonly List<Appli> zonesMenu = new List<Appli>();
        private readonly List<Window> onglets = new List<Window>();
        private readonly List<int> ongletsX = new List<int>();
        private readonly HexBitmap iconeProgramme, petiteProgramme;

        public readonly PixelBuffer BarBuffer;
        public PixelBuffer MenuBuffer;
        private PixelBuffer fondMenu;
        public bool BarDirty = true;
        public bool MenuDirty = true;
        public bool MenuOpen;

        private int categorie;
        private string recherche = "";
        private int hauteurMenu = 520;
        private int survolMenu = -1, survolRapide = -1, survolOnglet = -1, ongletW = 180;
        private bool survolF;
        private int horlogeX;

        public Taskbar()
        {
            BarBuffer = new PixelBuffer("barre des taches", Services.ScreenW, H);
            iconeProgramme = Bureau.Icone32(Icons.FslApp);
            petiteProgramme = new HexBitmap(Icons.FslApp, Icons.PaletteDe(Icons.FslApp));

            // Applications : nom, cle, icone, description, categories (bits)
            Ajouter("Fichiers", "fichiers", Icons.Folder, "Explorer les dossiers", 1 << 1);
            Ajouter("Editeur", "editeur", Icons.EditorIcon, "Ecrire et modifier du texte", 1 << 1 | 1 << 2);
            Ajouter("Createur", "createur", Icons.FslApp, "Un programme sans code", 1 << 6);
            Ajouter("Images", "images", Icons.ImageIcon, "Voir les images", 1 << 3);
            Ajouter("PDF", "pdf", Icons.PdfIcon, "Lire les documents PDF", 1 << 2);
            Ajouter("Guide FSL", "guide", Icons.PdfIcon, "Apprendre le langage FSL", 1 << 2 | 1 << 6);
            Ajouter("Navigateur", "navigateur", Icons.BrowserIcon, "Pages web simples", 1 << 4);
            Ajouter("Console", "console", Icons.ConsoleIcon, "Commandes du systeme", 1 << 6 | 1 << 7);
            Ajouter("Calculette", "calcul", Icons.CalcIcon, "Calculs rapides", 1 << 1);
            Ajouter("Serpent", "serpent", Icons.GameIcon, "Le jeu du serpent", 1 << 5);
            Ajouter("TextWar", "textwar", Icons.TextWarIcon, "Jeu en mode texte", 1 << 5);
            Ajouter("Reseau", "reseau", Icons.NetworkIcon, "Cartes et connexion", 1 << 4 | 1 << 7);
            Ajouter("Taches", "taches", Icons.TaskIcon, "Processus et threads", 1 << 7);
            Ajouter("Capture", "capture", Icons.CaptureIcon, "Photo ou video de l'ecran", 1 << 1 | 1 << 3);
            Ajouter("Comptes", "comptes", Icons.AccountIcon, "Utilisateurs", 1 << 7);
            Ajouter("Energie", "energie", Icons.BatteryIcon, "Batterie et alimentation", 1 << 7);
            Ajouter("Parametres", "parametres", Icons.SettingsIcon, "Ecran et fond d'ecran", 1 << 7);
            Ajouter("Installer", "installer", Icons.InstallerIcon, "Installer sur un disque", 1 << 7);
            Ajouter("A propos", "apropos", Icons.AboutIcon, "Version et auteur", 1 << 7);
            Ajouter("Sauver", "sauver", Icons.SessionIcon, "Enregistrer la session", 1 << 7);
            Ajouter("Sortie FSL", "sortie", Icons.OutputIcon, "Resultats des programmes", 1 << 6);

            string[][] icCat = { Icons.TaskIcon, Icons.CalcIcon, Icons.EditorIcon, Icons.ImageIcon, Icons.BrowserIcon,
                                 Icons.GameIcon, Icons.ConsoleIcon, Icons.SettingsIcon, Icons.FslApp };
            for (int i = 0; i < NomsCategories.Length; i++)
                categories.Add(new Appli { Nom = NomsCategories[i], Cle = "categorie", Index = i, Genre = Categorie,
                                           Petite = Petit(icCat[i]), Claire = Clair(icCat[i]) });

            AjouterLieu("Dossier FSOS", "", Icons.Folder);
            AjouterLieu("Documents", "\\Documents", Icons.TextFile);
            AjouterLieu("Images", "\\Images", Icons.ImageIcon);
            AjouterLieu("Applications", "\\Applications", Icons.FslApp);
            lieux.Add(new Appli { Nom = "Corbeille", Cle = "corbeille", Genre = Lieu, Petite = Petit(Icons.TrashIcon) });

            boutons.Add(new Appli { Nom = "Verrouiller", Cle = "verrouiller", Genre = Bouton, Petite = Petit(Icons.LockIcon) });
            boutons.Add(new Appli { Nom = "Se deconnecter", Cle = "deconnecter", Genre = Bouton, Petite = Petit(Icons.LogoutIcon) });
            boutons.Add(new Appli { Nom = "Eteindre", Cle = "alimentation", Genre = Bouton, Petite = Petit(Icons.PowerIcon) });

            string[] cles = { "fichiers", "navigateur", "editeur", "console", "createur" };
            for (int k = 0; k < cles.Length; k++)
                for (int i = 0; i < applis.Count; i++) if (applis[i].Cle == cles[k]) rapides.Add(applis[i]);

            RebuildMenu();
        }

        private static HexBitmap Petit(string[] icone) => new HexBitmap(icone, Icons.PaletteDe(icone));
        private static bool Clair(string[] icone) => icone == Icons.ConsoleIcon || icone == Icons.SettingsIcon;

        private void Ajouter(string nom, string cle, string[] icone, string description, int cats)
        {
            applis.Add(new Appli { Nom = nom, Cle = cle, Description = description, Categories = cats | 1, Genre = App,
                                   Icone = Bureau.Icone32(icone), Petite = Petit(icone), Claire = Clair(icone) });
        }

        private void AjouterLieu(string nom, string sousDossier, string[] icone) =>
            lieux.Add(new Appli { Nom = nom, Cle = "lieu", Chemin = sousDossier, Genre = Lieu, Petite = Petit(icone) });

        // ---- Programmes du dossier Applications ----

        /// <summary>Relit les programmes (.fsl, .fsc) du dossier Applications ; renvoie leur nombre.</summary>
        public int RebuildMenu()
        {
            programmes.Clear();
            var noms = new List<string>();
            string root = InstallerWindow.FindRoot();
            if (root != null)
            {
                try
                {
                    var list = VFSManager.GetDirectoryListing(root + "\\Applications");
                    for (int i = 0; i < list.Count && programmes.Count < 200; i++)
                    {
                        if (list[i].mEntryType != DirectoryEntryTypeEnum.File) continue;
                        string n = list[i].mName, low = n.ToLower();
                        if (!low.EndsWith(".fsl") && !low.EndsWith(".fsc")) continue;
                        string nom = n.Substring(0, n.LastIndexOf('.'));
                        if (FileSystemOS.Utils.Listes.Contient(noms, nom)) continue;
                        noms.Add(nom);
                        var a = new Appli { Nom = nom, Cle = "programme", Chemin = list[i].mFullPath, Description = "Programme FSL",
                                            Categories = 1 | 1 << MesProgrammes, Genre = App, Icone = iconeProgramme, Petite = petiteProgramme };
                        // a sa place dans l'ordre alphabetique (insertion par decalage)
                        programmes.Add(a);
                        int k = programmes.Count - 1;
                        while (k > 0 && Avant(nom, programmes[k - 1].Nom)) { programmes[k] = programmes[k - 1]; k--; }
                        programmes[k] = a;
                    }
                }
                catch (Exception) { }
            }
            Mesurer();
            return programmes.Count;
        }

        private static bool Avant(string a, string b)
        {
            string x = a.ToLower(), y = b.ToLower();
            int n = x.Length < y.Length ? x.Length : y.Length;
            for (int i = 0; i < n; i++) if (x[i] != y[i]) return x[i] < y[i];
            return x.Length < y.Length;
        }

        // ---- Geometrie ----

        public int Y0 => Services.ScreenH - H;
        public int MenuH => hauteurMenu;
        public int MenuX => 0;
        public int MenuY => Y0 - hauteurMenu;

        private void Mesurer()
        {
            int dispo = Services.ScreenH - TopBar.H - H - 4;
            hauteurMenu = dispo < 520 ? (dispo < 360 ? 360 : dispo) : 520;
            if (MenuBuffer == null || MenuBuffer.H != hauteurMenu)
            {
                if (MenuBuffer != null) MenuBuffer.Release();
                if (fondMenu != null) fondMenu.Release();
                MenuBuffer = new PixelBuffer("menu", MenuW, hauteurMenu);
                fondMenu = new PixelBuffer("menu (fixe)", MenuW, hauteurMenu);
            }
            ConstruireMenu();
        }

        public void Invalidate() { BarDirty = true; }

        // ---- Fenetres ----

        private static Window FenetreDe(string cle)
        {
            switch (cle)
            {
                case "fichiers": return Services.ExplorerWin;
                case "editeur": return Services.EditorWin;
                case "createur": return Services.StudioWin;
                case "images": return Services.ImageWin;
                case "pdf": return Services.PdfWin;
                case "navigateur": return Services.BrowserWin;
                case "console": return Services.ConsoleWin;
                case "calcul": return Services.CalcWin;
                case "serpent": return Services.SerpentWin;
                case "textwar": return Services.TextWarWin;
                case "reseau": return Services.ReseauWin;
                case "taches": return Services.TacheWin;
                case "capture": return Services.CaptureWin;
                case "comptes": return Services.AccountsWin;
                case "energie": return Services.EnergieWin;
                case "parametres": return Services.DisplayWin;
                case "apropos": return Services.AboutWin;
                case "sortie": return Services.FslWin;
                case "alimentation": return Services.PowerWin;
                case "corbeille": return Services.CorbeilleWin;
            }
            return null;
        }

        private void Lancer(Appli a)
        {
            var wm = Services.Windows;
            switch (a.Cle)
            {
                case "installer": AssistantInstallation.OuvrirDepuisConsole(); return;
                case "sauver":
                    Services.ConsoleWin.Print("Session : " + FileSystemOS.Systeme.Live.Save());
                    wm.Open(Services.ConsoleWin);
                    return;
                case "verrouiller": FileSystemOS.Systeme.Session.Lock(); return;
                case "deconnecter": FileSystemOS.Systeme.Session.Deconnecter(); return;
                case "programme": Launcher.Run(a.Chemin, out string m); return;
                case "guide":
                    try
                    {
                        string c = FileSystemOS.Systeme.GuideFsl.Chemin();
                        if (!FileSystemOS.Utils.FsTools.Exists(c)) c = FileSystemOS.Systeme.GuideFsl.Creer();
                        Services.PdfWin.Load(c);
                    }
                    catch (Exception) { }
                    wm.Open(Services.PdfWin);
                    return;
                case "lieu":
                    {
                        string root = InstallerWindow.FindRoot();
                        if (root != null) Services.ExplorerWin.Navigate(root + a.Chemin, true);
                        wm.Open(Services.ExplorerWin);
                        return;
                    }
            }
            Window w = a.Fenetre ?? FenetreDe(a.Cle);
            if (w != null) wm.Open(w);
        }

        // ---- Barre : positions ----

        private const int FX = 4, RapideX = 50, RapidePas = 34;
        private int SeparateurX => RapideX + rapides.Count * RapidePas + 4;
        private int OngletsX => SeparateurX + 10;

        private bool SurF(int mx, int my) => my >= Y0 + 4 && my < Y0 + 36 && mx >= FX && mx < FX + 40;

        private int RapideA(int mx, int my)
        {
            if (my < Y0 + 4 || my >= Y0 + 36 || mx < RapideX) return -1;
            int i = (mx - RapideX) / RapidePas;
            return i < rapides.Count && (mx - RapideX) % RapidePas < 32 ? i : -1;
        }

        private int OngletA(int mx, int my)
        {
            if (my < Y0 + 4 || my >= Y0 + 36) return -1;
            for (int i = 0; i < ongletsX.Count; i++) if (mx >= ongletsX[i] && mx < ongletsX[i] + ongletW) return i;
            return -1;
        }

        private bool InMenu(int mx, int my) =>
            MenuOpen && mx >= MenuX && mx < MenuX + MenuW && my >= MenuY && my < MenuY + hauteurMenu;

        private int MenuA(int mx, int my)
        {
            if (!InMenu(mx, my)) return -1;
            int lx = mx - MenuX, ly = my - MenuY;
            for (int i = 0; i < zonesMenu.Count; i++)
            {
                Appli a = zonesMenu[i];
                if (lx >= a.X && lx < a.X + a.W && ly >= a.Y && ly < a.Y + a.H) return i;
            }
            return -1;
        }

        public bool IsOver(int mx, int my) => my >= Y0 || InMenu(mx, my);

        // ---- Souris ----

        public void UpdateHover(int mx, int my)
        {
            bool f = SurF(mx, my);
            int r = RapideA(mx, my), o = OngletA(mx, my);
            if (f != survolF || r != survolRapide || o != survolOnglet) { survolF = f; survolRapide = r; survolOnglet = o; BarDirty = true; }
            int m = MenuA(mx, my);
            if (m != survolMenu) { survolMenu = m; MenuDirty = true; }
        }

        public bool HandleClick(int mx, int my)
        {
            if (!Services.LoggedIn) return my >= Y0;
            if (MenuOpen)
            {
                if (InMenu(mx, my))
                {
                    int i = MenuA(mx, my);
                    if (i < 0) return true;
                    Appli a = zonesMenu[i];
                    if (a.Genre == Recherche) return true;
                    if (a.Genre == Categorie)
                    {
                        categorie = a.Index;
                        recherche = "";
                        Filtrer();
                        return true;
                    }
                    SetMenu(false);
                    Lancer(a);
                    return true;
                }
                SetMenu(false);
                if (SurF(mx, my) || my < Y0) return true;
            }

            if (my < Y0) return false;
            if (SurF(mx, my)) { SetMenu(true); return true; }
            int rp = RapideA(mx, my);
            if (rp >= 0) { Lancer(rapides[rp]); return true; }
            int o = OngletA(mx, my);
            if (o >= 0 && o < onglets.Count) Services.Windows.ToggleFromTaskbar(onglets[o]);
            return true;
        }

        public void BasculerMenu() => SetMenu(!MenuOpen);

        private void SetMenu(bool open)
        {
            if (open)
            {
                recherche = "";
                categorie = Toutes;
                RebuildMenu();
                CuireMenu();
            }
            MenuOpen = open;
            survolMenu = -1;
            MenuDirty = true;
            BarDirty = true;
            Services.ScreenDirty = true;
        }

        // ---- Clavier : recherche ----

        public bool Touche(KeyEvent k)
        {
            if (!MenuOpen) return false;
            if (k.Key == ConsoleKeyEx.Escape)
            {
                if (recherche.Length > 0) { recherche = ""; Filtrer(); }
                else SetMenu(false);
                return true;
            }
            if (k.Key == ConsoleKeyEx.Backspace)
            {
                if (recherche.Length > 0) { recherche = recherche.Substring(0, recherche.Length - 1); Filtrer(); }
                return true;
            }
            if (k.Key == ConsoleKeyEx.Enter)
            {
                for (int i = 0; i < zonesMenu.Count; i++)
                    if (zonesMenu[i].Genre == App) { Appli a = zonesMenu[i]; SetMenu(false); Lancer(a); break; }
                return true;
            }
            char ch = k.KeyChar;
            if (ch >= ' ' && ch <= '~' && recherche.Length < 24) { recherche += ch; Filtrer(); }
            return true;
        }

        private void Filtrer()
        {
            ConstruireMenu();
            CuireMenu();
            survolMenu = -1;
            MenuDirty = true;
        }

        // ---- Disposition du menu ----

        private void ConstruireMenu()
        {
            zonesMenu.Clear();
            int y = 112;
            for (int i = 0; i < lieux.Count && y + 28 <= hauteurMenu - 52; i++, y += 28) Placer(lieux[i], 6, y, GaucheW - 12, 28);
            y = 12;
            for (int i = 0; i < categories.Count && y + 30 <= hauteurMenu - 52; i++, y += 30) Placer(categories[i], MilieuX + 6, y, MilieuW - 12, 30);

            // Colonne de droite : la categorie choisie, ou les resultats de la recherche
            var liste = new List<Appli>();
            string q = recherche.ToLower();
            for (int pass = 0; pass < 2; pass++)
            {
                List<Appli> src = pass == 0 ? applis : programmes;
                for (int i = 0; i < src.Count; i++)
                {
                    Appli a = src[i];
                    bool ok = recherche.Length > 0 ? a.Nom.ToLower().IndexOf(q) >= 0 || a.Description.ToLower().IndexOf(q) >= 0
                                                   : (a.Categories & (1 << categorie)) != 0;
                    if (ok) liste.Add(a);
                }
            }
            bool compacte = recherche.Length > 0 || categorie == Toutes || liste.Count > (hauteurMenu - 64) / 44;
            int bas = hauteurMenu - 52;
            if (compacte)
            {
                int lignes = (bas - 12) / 26, cw = (MenuW - DroiteX - 8) / 2;
                for (int i = 0; i < liste.Count && i < lignes * 2; i++)
                {
                    Appli a = liste[i];
                    a.Compacte = true;
                    Placer(a, DroiteX + 4 + (i / lignes) * cw, 12 + (i % lignes) * 26, cw - 4, 26);
                }
            }
            else
                for (int i = 0; i < liste.Count && 12 + (i + 1) * 44 <= bas; i++)
                {
                    liste[i].Compacte = false;
                    Placer(liste[i], DroiteX + 4, 12 + i * 44, MenuW - DroiteX - 12, 44);
                }

            for (int i = 0; i < boutons.Count; i++) Placer(boutons[i], 10 + i * 40, hauteurMenu - 42, 34, 34);
            zonesMenu.Add(new Appli { Nom = "Rechercher", Genre = Recherche, X = GaucheW + 8, Y = hauteurMenu - 42, W = MenuW - GaucheW - 20, H = 34 });
        }

        private void Placer(Appli a, int x, int y, int w, int h)
        {
            a.X = x; a.Y = y; a.W = w; a.H = h;
            zonesMenu.Add(a);
        }

        // ---- Dessin du menu ----

        /// <summary>Partie fixe du menu : calculee a l'ouverture et a chaque changement de categorie ou de recherche.</summary>
        private void CuireMenu()
        {
            if (fondMenu == null) return;
            var b = fondMenu;
            b.FillRect(MenuFond, 0, 0, MenuW, hauteurMenu);
            b.FillRect(Colonne, 0, 0, GaucheW, hauteurMenu - 50);
            b.FillRect(Bord, GaucheW, 8, 1, hauteurMenu - 66);
            b.FillRect(Bord, DroiteX, 8, 1, hauteurMenu - 66);
            b.FillRect(Bord, 0, hauteurMenu - 50, MenuW, 1);
            // Contour du menu
            b.FillRect(Bord, 0, 0, MenuW, 1);
            b.FillRect(Bord, 0, 0, 1, hauteurMenu);
            b.FillRect(Bord, MenuW - 1, 0, 1, hauteurMenu);

            // Compte
            var compte = FileSystemOS.Systeme.Comptes.Actuel;
            string nom = compte != null ? compte.Nom : "Invite";
            Avatar(b, GaucheW / 2 - 24, 12, 48, nom);
            string n = nom.Length > 17 ? nom.Substring(0, 17) : nom;
            b.DrawText(n, Texte, (GaucheW - n.Length * 8) / 2, 66);
            string role = compte != null && compte.Admin ? "Administrateur" : "Utilisateur";
            b.DrawText(role, Discret, (GaucheW - role.Length * 8) / 2, 84);

            for (int i = 0; i < zonesMenu.Count; i++) DessinerZone(b, zonesMenu[i], false);

            // Champ de recherche (le texte tape est dessine a chaque fois)
            int ry = hauteurMenu - 42, rx = GaucheW + 8, rw = MenuW - GaucheW - 20;
            b.FillRoundRect(Bord, rx, ry, rw, 34, 8);
            b.FillRoundRect(Color.White, rx + 1, ry + 1, rw - 2, 32, 7);
            Loupe(b, rx + 12, ry + 11, Discret);
            if (recherche.Length > 0)
            {
                string t = "Resultats pour : " + recherche;
                b.DrawText(t.Length > 22 ? t.Substring(0, 22) : t, Indigo, MilieuX + 12, hauteurMenu - 74);
            }
        }

        private void DessinerZone(PixelBuffer b, Appli a, bool survol)
        {
            switch (a.Genre)
            {
                case Categorie:
                    {
                        bool choisi = recherche.Length == 0 && a.Index == categorie;
                        Color fond = choisi ? Choisi : (survol ? Survol : MenuFond);
                        b.FillRoundRect(fond, a.X, a.Y + 2, a.W, a.H - 4, 6);
                        if (choisi) b.FillRect(Indigo, a.X, a.Y + 6, 3, a.H - 12);
                        Poser16(b, a, a.X + 10, a.Y + 7);
                        b.DrawText(Couper(a.Nom, (a.W - 38) / 8), choisi ? Indigo : Texte, a.X + 32, a.Y + 7);
                        break;
                    }
                case Lieu:
                    b.FillRoundRect(survol ? Survol : Colonne, a.X, a.Y + 2, a.W, a.H - 4, 6);
                    Poser16(b, a, a.X + 8, a.Y + 6);
                    b.DrawText(Couper(a.Nom, (a.W - 34) / 8), Texte, a.X + 30, a.Y + 6);
                    break;
                case Bouton:
                    b.FillRoundRect(survol ? Survol : MenuFond, a.X, a.Y, a.W, a.H, 8);
                    Poser16(b, a, a.X + (a.W - 16) / 2, a.Y + (a.H - 16) / 2);
                    break;
                case App:
                    b.FillRoundRect(survol ? Survol : MenuFond, a.X, a.Y + 1, a.W, a.H - 2, 6);
                    if (a.Compacte)
                    {
                        Poser16(b, a, a.X + 6, a.Y + 5);
                        b.DrawText(Couper(a.Nom, (a.W - 32) / 8), Texte, a.X + 28, a.Y + 5);
                    }
                    else
                    {
                        if (a.Claire) b.FillRoundRect(TuileSombre, a.X + 3, a.Y + 3, 38, 38, 9);
                        a.Icone.Draw(b, a.X + 6, a.Y + 6);
                        b.DrawText(Couper(a.Nom, (a.W - 52) / 8), Texte, a.X + 48, a.Y + 6);
                        b.DrawText(Couper(a.Description, (a.W - 52) / 8), Discret, a.X + 48, a.Y + 24);
                    }
                    break;
            }
        }

        private static void Poser16(PixelBuffer b, Appli a, int x, int y)
        {
            if (a.Claire) b.FillRoundRect(TuileSombre, x - 2, y - 2, 20, 20, 5);
            a.Petite.Draw(b, x, y);
        }

        private static string Couper(string s, int n) => n <= 0 ? "" : (s.Length > n ? s.Substring(0, n) : s);

        public void RenderMenu()
        {
            var b = MenuBuffer;
            if (fondMenu != null && fondMenu.H == hauteurMenu) b.CopierZone(fondMenu, 0, 0, 0, 0, MenuW, hauteurMenu);
            else b.FillRect(MenuFond, 0, 0, MenuW, hauteurMenu);
            if (survolMenu >= 0 && survolMenu < zonesMenu.Count && zonesMenu[survolMenu].Genre != Recherche)
                DessinerZone(b, zonesMenu[survolMenu], true);
            int ry = hauteurMenu - 42, rx = GaucheW + 8;
            if (recherche.Length == 0) b.DrawText("Rechercher une application", Discret, rx + 32, ry + 9);
            else
            {
                b.DrawText(recherche, Texte, rx + 32, ry + 9);
                b.FillRect(Texte, rx + 32 + recherche.Length * 8 + 1, ry + 9, 2, 16);
            }
        }

        // ---- Dessin de la barre ----

        public void RenderBar()
        {
            var b = BarBuffer;
            b.FillRect(Fond, 0, 0, b.W, H);
            b.FillRect(Bord, 0, 0, b.W, 1);

            DessinerF(b, FX, 4, survolF || MenuOpen);
            for (int i = 0; i < rapides.Count; i++)
            {
                int x = RapideX + i * RapidePas;
                if (i == survolRapide) b.FillRoundRect(Survol, x, 4, 32, 32, 7);
                Poser16(b, rapides[i], x + 8, 12);
            }
            b.FillRect(Bord, SeparateurX, 8, 1, 24);

            // Heure et compte, a droite
            string heure = Services.ClockText.Length >= 5 ? Services.ClockText.Substring(0, 5) : Services.ClockText;
            string qui = FileSystemOS.Systeme.Comptes.Actuel != null && Services.LoggedIn ? FileSystemOS.Systeme.Comptes.Actuel.Nom : "";
            if (FileSystemOS.Systeme.Live.Active) qui = qui.Length > 0 ? "LIVE " + qui : "LIVE";
            if (qui.Length > 16) qui = qui.Substring(0, 16);
            horlogeX = b.W - heure.Length * 8 - 14;
            b.DrawText(heure, Texte, horlogeX, 12);
            int quiX = horlogeX - 16 - qui.Length * 8;
            if (qui.Length > 0) b.DrawText(qui, Discret, quiX, 12);

            // Un onglet par fenetre
            onglets.Clear();
            ongletsX.Clear();
            if (!Services.LoggedIn) return;
            var all = Services.Windows.All;
            int x0 = OngletsX, limite = (qui.Length > 0 ? quiX : horlogeX) - 12;
            int n = all.Count;
            if (n == 0) return;
            ongletW = (limite - x0) / n - 4;
            if (ongletW > 180) ongletW = 180;
            if (ongletW < 64) ongletW = 64;
            Window focused = Services.Windows.Focused;
            for (int i = 0; i < n; i++)
            {
                int x = x0 + i * (ongletW + 4);
                if (x + ongletW > limite) break;
                Window w = all[i];
                onglets.Add(w);
                ongletsX.Add(x);
                bool actif = w == focused && !w.Hidden;
                b.FillRoundRect(actif ? Actif : (i == survolOnglet ? Survol : Onglet), x, 4, ongletW, 32, 6);
                if (actif) b.FillRect(Indigo, x + 8, 33, ongletW - 16, 3);
                w.Icon.Draw(b, x + 8, 12);
                b.DrawText(Couper(w.Title, (ongletW - 38) / 8), w.Hidden ? Discret : Texte, x + 30, 12);
            }
        }

        /// <summary>
        /// Le bouton Demarrer : le F bleute, semi-transparent (degrade, reflet, liseré clair), melange
        /// a la barre par le pilote de transparence. Une tuile apparait au survol ou menu ouvert.
        /// </summary>
        private static void DessinerF(PixelBuffer b, int x, int y, bool allume)
        {
            if (allume) b.FillRoundRect(Survol, x, y, 40, 32, 8);
            int[] bx = { x + 14, x + 14, x + 14 }, by = { y + 5, y + 5, y + 14 }, bw = { 7, 17, 13 }, bh = { 22, 7, 6 };
            for (int k = 0; k < 3; k++) b.FillRoundRectAlpha(Color.White, 140, bx[k] - 1, by[k] - 1, bw[k] + 2, bh[k] + 2, 3);
            for (int k = 0; k < 3; k++)
            {
                for (int j = 0; j < bh[k]; j++)
                {
                    int t = (by[k] + j - (y + 5)) * 255 / 22;
                    Color c = Color.FromArgb(110 + (40 - 110) * t / 255, 165 + (92 - 165) * t / 255, 255 + (232 - 255) * t / 255);
                    int ins = j < 2 ? PixelBuffer.CornerInset(2, j) : (j >= bh[k] - 2 ? PixelBuffer.CornerInset(2, bh[k] - 1 - j) : 0);
                    b.FillRoundRectAlpha(c, 200, bx[k] + ins, by[k] + j, bw[k] - 2 * ins, 1, 0);
                }
                b.FillRoundRectAlpha(Color.White, 60, bx[k] + 1, by[k], bw[k] - 2, bh[k] / 2, 0);
            }
        }

        /// <summary>Nom du bouton survole de la barre (dessine par le compositeur).</summary>
        public void DessinerInfobulle(PixelBuffer s)
        {
            if (MenuOpen || !Services.LoggedIn) return;
            string t;
            int cx;
            if (survolF) { t = "Demarrer"; cx = FX + 20; }
            else if (survolRapide >= 0 && survolRapide < rapides.Count) { t = rapides[survolRapide].Nom; cx = RapideX + survolRapide * RapidePas + 16; }
            else if (survolOnglet >= 0 && survolOnglet < onglets.Count && onglets[survolOnglet].Title.Length > (ongletW - 38) / 8)
            { t = onglets[survolOnglet].Title; cx = ongletsX[survolOnglet] + ongletW / 2; }
            else return;
            if (t.Length > 40) t = t.Substring(0, 40);
            int w = t.Length * 8 + 16, x = cx - w / 2, y = Y0 - 28;
            if (x < 4) x = 4;
            if (x + w > s.W - 4) x = s.W - 4 - w;
            s.FillRoundRect(Color.FromArgb(32, 36, 52), x, y, w, 24, 8);
            s.DrawText(t, Color.White, x + 8, y + 4);
        }

        /// <summary>Avatar rond : un degrade indigo et l'initiale du compte.</summary>
        private static void Avatar(PixelBuffer b, int x, int y, int d, string nom)
        {
            for (int j = 0; j < d; j++)
            {
                int t = j * 255 / d;
                Color c = Color.FromArgb(124 + (70 - 124) * t / 255, 140 + (80 - 140) * t / 255, 255 + (220 - 255) * t / 255);
                int dy = 2 * j + 1 - d, xa = -1, xb = -1;
                for (int i = 0; i < d; i++)
                {
                    int dx = 2 * i + 1 - d;
                    if (dx * dx + dy * dy <= d * d) { if (xa < 0) xa = i; xb = i; }
                }
                if (xa >= 0) b.FillRect(c, x + xa, y + j, xb - xa + 1, 1);
            }
            string ini = nom.Length > 0 ? nom.Substring(0, 1).ToUpper() : "?";
            b.DrawText(ini, Color.White, x + (d - 8) / 2, y + (d - 16) / 2);
        }

        /// <summary>Petite loupe (12 x 12) : un anneau et un manche.</summary>
        private static void Loupe(PixelBuffer b, int x, int y, Color c)
        {
            for (int j = 0; j < 12; j++)
                for (int i = 0; i < 12; i++)
                {
                    int dx = 2 * i - 11, dy = 2 * j - 11, d2 = dx * dx + dy * dy;
                    if (d2 > 52 && d2 < 108) b.SetPixel(x + i, y + j, PixelBuffer.Argb(c));
                }
            b.FillRect(c, x + 10, y + 10, 4, 2);
        }
    }
}
