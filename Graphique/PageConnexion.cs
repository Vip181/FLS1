using System;
using System.Drawing;
using Cosmos.System;
using FileSystemOS.Demarrage;
using FileSystemOS.Processus;
using FileSystemOS.Systeme;
using FileSystemOS.Utils;

namespace FileSystemOS.Graphique
{
    /// <summary>
    /// PAGE DE CONNEXION plein ecran (inspiree d'une fenetre de connexion vitree) : le fond
    /// d'ecran, un panneau de verre au centre (identifiant, mot de passe, bouton rond en fleche),
    /// les comptes en petits avatars au-dessus, Eteindre et Redemarrer dans le panneau.
    ///
    /// Premier demarrage : creation du compte administrateur (nom, mot de passe, confirmation).
    /// Session verrouillee : seul le mot de passe est demande.
    /// Le fond et le verre sont calcules UNE fois a l'ouverture (pilote de transparence) ; le
    /// clignotement du curseur et la saisie ne redessinent que les champs.
    /// Tant qu'elle est ouverte, elle recoit TOUT le clavier et toute la souris.
    /// </summary>
    public static class PageConnexion
    {
        public static bool Active;
        private static PixelBuffer page, fondPage;
        private static int cleFond = -1;
        private static bool sale = true;

        private static readonly TextField nom = new TextField(0, 0, 300, false);
        private static readonly TextField mdp = new TextField(0, 0, 300, true);
        private static readonly TextField confirmation = new TextField(0, 0, 300, true);
        private static Button bouton, voir;
        private static int focus;              // 0 identifiant, 1 mot de passe, 2 confirmation
        private static bool creation;          // premier demarrage : creer l'administrateur
        private static bool verrouillage;
        private static string message = "", sousTitre = "", titre = "";
        private static bool erreur;
        private static uint dernierBattement, frame;
        private static bool caret = true, sourisAppuyee;
        private static int survolTuile = -1;
        private static bool survolFleche, survolEteindre, survolRedemarrer;

        // Panneau de verre
        private static int PanneauW => creation ? 480 : 500;
        private static int PanneauH => creation ? 290 : 210;
        private static int PanneauX => (Services.ScreenW - PanneauW) / 2;
        private static int PanneauY => (Services.ScreenH - PanneauH) / 2 + (creation || verrouillage ? 0 : 50);
        private static int FlecheX => PanneauX + PanneauW - 64;
        private static int FlecheY => PanneauY + 96;
        private static int BasY => PanneauY + PanneauH - 40;

        // Comptes, en avatars au-dessus du panneau
        private const int TuileW = 96, TuileH = 96, Ecart = 12, TuilesMax = 6;
        private static int TuilesY => PanneauY - TuileH - 16;

        // Couleurs des avatars (une par utilisateur, d'apres son nom)
        private static readonly Color[] Teintes =
        {
            Color.FromArgb(59, 130, 246), Color.FromArgb(16, 185, 129), Color.FromArgb(245, 158, 11),
            Color.FromArgb(236, 72, 153), Color.FromArgb(139, 92, 246), Color.FromArgb(20, 184, 166),
            Color.FromArgb(239, 68, 68), Color.FromArgb(100, 116, 139)
        };

        // ================================================================
        //  Ouverture / fermeture
        // ================================================================

        /// <summary>raison : "demarrage", "deconnexion" ou "verrouillage".</summary>
        public static void Ouvrir(string raison)
        {
            Comptes.Load();
            creation = Comptes.Liste.Count == 0;
            verrouillage = !creation && raison == "verrouillage" && Comptes.Actuel != null;
            mdp.Text = ""; confirmation.Text = "";
            mdp.Password = true;
            message = ""; erreur = false;
            survolTuile = -1;

            if (creation)
            {
                nom.Text = "";
                titre = "Connexion";
                sousTitre = "Premier demarrage : creez le compte administrateur";
                focus = 0;
            }
            else if (verrouillage)
            {
                nom.Text = Comptes.Actuel.Nom;          // session verrouillee : on garde le nom
                titre = "Session verrouillee";
                sousTitre = "Session de " + Comptes.Actuel.Nom + " : entrez votre mot de passe";
                focus = 1;
            }
            else
            {
                // Le dernier compte utilise est deja choisi : il reste le mot de passe
                Compte dernier = Comptes.Find(Config.Get("dernier_utilisateur", ""));
                if (dernier == null && Comptes.Liste.Count == 1) dernier = Comptes.Liste[0];
                nom.Text = dernier != null ? dernier.Nom : "";
                titre = raison == "deconnexion" ? "Au revoir" : "Bienvenue";
                sousTitre = "Veuillez entrer votre identifiant";
                focus = nom.Text.Length > 0 ? 1 : 0;
            }

            if (page == null || page.W != Services.ScreenW || page.H != Services.ScreenH)
                page = new PixelBuffer("page de connexion", Services.ScreenW, Services.ScreenH);
            Placer();
            Active = true;
            sale = true;
            Services.ScreenDirty = true;
        }

        public static void Fermer()
        {
            Active = false;
            mdp.Text = ""; confirmation.Text = "";
            Services.ScreenDirty = true;
        }

        private static void Placer()
        {
            int px = PanneauX, py = PanneauY;
            if (creation)
            {
                int x = px + 150;
                nom.RX = x; nom.RY = py + 92; nom.W = 300; nom.Max = 32;
                mdp.RX = x; mdp.RY = py + 132; mdp.W = 232; mdp.Max = 64;
                confirmation.RX = x; confirmation.RY = py + 172; confirmation.W = 300;
                voir = new Button("Voir", x + 236, py + 132, 64, 26, Theme.TitleInactive, Theme.TitleActive);
                bouton = new Button(Glyphs.Check, "Creer le compte", px + (PanneauW - 220) / 2, py + 214, 220, 34, Theme.Blue, Theme.BlueHover);
                return;
            }
            nom.RX = px + 72; nom.RY = py + 62; nom.W = 300; nom.Max = 32;
            mdp.RX = px + 72; mdp.RY = py + 104; mdp.W = 232; mdp.Max = 64;
            voir = new Button("Voir", px + 308, py + 104, 64, 26, Theme.TitleInactive, Theme.TitleActive);
            bouton = new Button(Glyphs.Check, "Se connecter", -1000, -1000, 10, 10, Theme.Blue, Theme.BlueHover);  // remplace par la fleche
        }

        // ================================================================
        //  Tuiles des utilisateurs
        // ================================================================

        private static int NbTuiles => Comptes.Liste.Count < TuilesMax ? Comptes.Liste.Count : TuilesMax;

        private static int TuileX(int i)
        {
            int n = NbTuiles;
            int largeur = n * TuileW + (n - 1) * Ecart;
            return (Services.ScreenW - largeur) / 2 + i * (TuileW + Ecart);
        }

        private static int TuileSous(int mx, int my)
        {
            if (creation || verrouillage) return -1;
            if (my < TuilesY || my >= TuilesY + TuileH) return -1;
            for (int i = 0; i < NbTuiles; i++)
            {
                int x = TuileX(i);
                if (mx >= x && mx < x + TuileW) return i;
            }
            return -1;
        }

        private static void Choisir(int i)
        {
            if (i < 0 || i >= NbTuiles) return;
            nom.Text = Comptes.Liste[i].Nom;
            mdp.Text = "";
            focus = 1;
            message = "";
            sale = true;
            Services.ScreenDirty = true;
        }

        private static Color Teinte(string n)
        {
            int s = 0;
            for (int i = 0; i < n.Length; i++) s += n[i] * (i + 1);
            if (s < 0) s = -s;
            return Teintes[s % Teintes.Length];
        }

        /// <summary>Avatar rond de l'utilisateur : couleur a lui, et son initiale en grand.</summary>
        private static void Avatar(PixelBuffer b, string n, int cx, int cy, int r)
        {
            b.FillRoundRect(Color.FromArgb(255, 255, 255), cx - r - 2, cy - r - 2, 2 * r + 4, 2 * r + 4, r + 2);
            b.FillRoundRect(Teinte(n), cx - r, cy - r, 2 * r, 2 * r, r);
            char c = n.Length > 0 ? n[0] : '?';
            if (c >= 'a' && c <= 'z') c = (char)(c - 32);
            if (c < ' ' || c > '~') c = '?';
            Grand(b, c.ToString(), cx, cy - 16, Color.White);
        }

        private static void DessinerTuiles(PixelBuffer b)
        {
            for (int i = 0; i < NbTuiles; i++)
            {
                Compte c = Comptes.Liste[i];
                int x = TuileX(i), y = TuilesY;
                bool choisi = c.Nom == nom.Text;
                if (choisi) b.FillRoundRectAlpha(Color.White, 90, x, y, TuileW, TuileH, 14);
                else if (i == survolTuile) b.FillRoundRectAlpha(Color.White, 45, x, y, TuileW, TuileH, 14);
                Avatar(b, c.Nom, x + TuileW / 2, y + 36, 28);
                string n = c.Nom.Length > 11 ? c.Nom.Substring(0, 10) + "." : c.Nom;
                Ombre(b, n, x + TuileW / 2 - n.Length * 4, y + 72, Color.White);
            }
        }

        /// <summary>Texte pose sur le fond d'ecran : sombre sur un fond clair, blanc sur un fond fonce.</summary>
        private static void Ombre(PixelBuffer b, string t, int x, int y, Color c)
        {
            if (FondClair(x + t.Length * 4, y + 8))
            {
                b.DrawText(t, Color.FromArgb(255, 255, 255), x + 1, y + 1);
                b.DrawText(t, Color.FromArgb(24, 30, 52), x, y);
            }
            else
            {
                b.DrawText(t, Color.FromArgb(10, 14, 30), x + 1, y + 1);
                b.DrawText(t, c, x, y);
            }
        }

        /// <summary>Le fond d'ecran est-il clair a cet endroit ? (un seul pixel lu)</summary>
        private static bool FondClair(int x, int y)
        {
            var f = fondPage ?? Desktop.Background;
            if (f == null || x < 0 || y < 0 || x >= f.W || y >= f.H) return false;
            int c = Desktop.Background != null && x < Desktop.Background.W && y < Desktop.Background.H
                    ? Desktop.Background.Data[y * Desktop.Background.W + x] : f.Data[y * f.W + x];
            return ((c >> 16) & 255) * 3 + ((c >> 8) & 255) * 6 + (c & 255) > 1500;
        }

        // ================================================================
        //  Dessin
        // ================================================================

        /// <summary>Appele par le compositeur : dessine la page par-dessus tout.</summary>
        public static void Dessiner(PixelBuffer scene)
        {
            if (!Active) return;

            // Curseur clignotant et logo anime : deux fois par seconde
            uint t = Horloge.Active ? Horloge.Millis : frame * 40;
            if (t - dernierBattement >= 500) { dernierBattement = t; caret = !caret; sale = true; }

            if (sale) { Redessiner(); sale = false; }
            scene.Blit(page, 0, 0);
        }

        /// <summary>Faut-il recomposer l'ecran ? (le compositeur le demande a chaque tour)</summary>
        public static void Tick()
        {
            if (!Active) return;
            frame++;
            uint t = Horloge.Active ? Horloge.Millis : frame * 40;
            if (t - dernierBattement >= 500) Services.ScreenDirty = true;
        }

        /// <summary>Le fond d'ecran et le panneau de verre : calcules une fois (a l'ouverture ou si le fond change).</summary>
        private static void CuireFond()
        {
            if (fondPage == null || fondPage.W != page.W || fondPage.H != page.H)
            {
                if (fondPage != null) fondPage.Release();
                fondPage = new PixelBuffer("page de connexion (fixe)", page.W, page.H);
            }
            var b = fondPage;
            var fond = Desktop.Background;
            if (fond != null && fond.W == b.W && fond.H >= b.H) b.CopierZone(fond, 0, 0, 0, 0, b.W, b.H);
            else Desktop.Degrade(b, 0, 0, 0, b.W, b.H);

            int px = PanneauX, py = PanneauY, pw = PanneauW, ph = PanneauH;
            b.FillRoundRectAlpha(Color.Black, 45, px + 4, py + 8, pw, ph, 14);          // ombre
            b.FillRoundRectAlpha(Color.White, 150, px - 1, py - 1, pw + 2, ph + 2, 15); // liseré
            var verre = new PixelBuffer("verre de connexion", pw, ph);
            PixelBuffer.VerreRapide(b, px, py, verre, 0, 0, pw, ph, 4, Color.FromArgb(28, 66, 150), 165);
            b.CopierArrondi(verre, 0, 0, px, py, pw, ph, 14);
            verre.Release();
            b.FillRoundRectAlpha(Color.White, 34, px, py, pw, 40, 14);                  // bandeau du titre
            b.FillRoundRectAlpha(Color.White, 80, px + 14, py + 40, pw - 28, 1, 0);

            Color blanc = Color.White, doux = Color.FromArgb(200, 214, 240);
            b.DrawText(titre, blanc, px + 18, py + 13);
            string v = Services.OsName + " " + Services.Version;
            b.DrawText(v, doux, px + pw - 18 - v.Length * 8, py + 13);
            if (creation)
            {
                b.DrawText(sousTitre, doux, px + (pw - sousTitre.Length * 8) / 2, py + 56);
                b.DrawText("Utilisateur", blanc, px + 40, nom.RY + 5);
                b.DrawText("Mot de passe", blanc, px + 32, mdp.RY + 5);
                b.DrawText("Confirmer", blanc, px + 56, confirmation.RY + 5);
            }
            else
            {
                new HexBitmap(Icons.AccountIcon, Icons.PaletteDe(Icons.AccountIcon)).Draw(b, px + 40, nom.RY + 5);
                new HexBitmap(Icons.LockIcon, Icons.PaletteDe(Icons.LockIcon)).Draw(b, px + 40, mdp.RY + 5);
            }
            // Bas de l'ecran : identite de l'OS
            Ombre(b, Services.Company + "  -  " + Services.Author + "   (" + Services.OsName + " " + Services.Version + ")", 20, b.H - 30, doux);
        }

        private static void Redessiner()
        {
            int cle = Desktop.VersionFond * 4 + (creation ? 1 : 0) + (verrouillage ? 2 : 0);
            if (fondPage == null || cle != cleFond || fondPage.W != page.W || fondPage.H != page.H) { CuireFond(); cleFond = cle; }
            var b = page;
            b.CopierZone(fondPage, 0, 0, 0, 0, b.W, b.H);
            Color blanc = Color.White, doux = Color.FromArgb(200, 214, 240);
            int px = PanneauX, py = PanneauY, pw = PanneauW;

            if (!creation && !verrouillage) DessinerTuiles(b);
            if (verrouillage) Avatar(b, Comptes.Actuel.Nom, Services.ScreenW / 2, py - 56, 40);

            nom.Draw(b, focus == 0, caret && focus == 0);
            mdp.Draw(b, focus == 1, caret && focus == 1);
            voir.Label = mdp.Password ? "Voir" : "Cacher";     // afficher ou masquer le mot de passe
            voir.Draw(b, 0, 0);
            if (creation)
            {
                confirmation.Draw(b, focus == 2, caret && focus == 2);
                bouton.Draw(b, 0, 0);
            }
            else
            {
                // Bouton rond : une fleche
                int cx = FlecheX, cy = FlecheY;
                b.FillRoundRect(survolFleche ? Color.FromArgb(214, 228, 255) : Color.White, cx - 24, cy - 24, 48, 48, 24);
                Color bleu = Color.FromArgb(46, 98, 214);
                b.FillRect(bleu, cx - 11, cy - 2, 13, 5);
                for (int j = -8; j <= 8; j++) b.FillRect(bleu, cx + 2, cy + j, 9 - (j < 0 ? -j : j), 1);
                // Eteindre, Redemarrer
                Pastille(b, "Eteindre", px + 16, BasY, survolEteindre);
                Pastille(b, "Redemarrer", px + 116, BasY, survolRedemarrer);
            }

            if (message.Length > 0)
            {
                int max = (pw - 20) / 8;
                string m = message.Length > max ? message.Substring(0, max) : message;
                int my = creation ? bouton.RY + 44 : py + 146;
                b.DrawText(m, erreur ? Color.FromArgb(255, 170, 170) : blanc, px + (pw - m.Length * 8) / 2, my);
            }
            else if (!creation)
            {
                string aide = verrouillage ? "Entree : deverrouiller" : "Entree : se connecter";
                b.DrawText(aide, doux, px + pw - 18 - aide.Length * 8, BasY + 7);
            }

            // Heure, en bas a droite
            string heure = "";
            try { heure = Fmt.Two(Cosmos.HAL.RTC.Hour) + ":" + Fmt.Two(Cosmos.HAL.RTC.Minute); } catch (Exception) { }
            Grand(b, heure, b.W - 70, b.H - 50, FondClair(b.W - 70, b.H - 34) ? Color.FromArgb(24, 30, 52) : Color.White);
        }

        private static void Pastille(PixelBuffer b, string t, int x, int y, bool survol)
        {
            int w = t.Length * 8 + 20;
            b.FillRoundRect(survol ? Color.FromArgb(214, 228, 255) : Color.FromArgb(232, 238, 250), x, y, w, 26, 13);
            b.DrawText(t, Color.FromArgb(30, 50, 100), x + 10, y + 5);
        }

        private static int LargeurPastille(string t) => t.Length * 8 + 20;

        private static void Centre(PixelBuffer b, string t, int cx, int y, Color c) =>
            b.DrawText(t, c, cx - t.Length * 4, y);

        /// <summary>Texte deux fois plus gros (chaque lettre dessinee en 2x2).</summary>
        private static void Grand(PixelBuffer b, string t, int cx, int y, Color c)
        {
            if (t.Length == 0) return;
            var tmp = new PixelBuffer("texte", t.Length * 8, 16);
            tmp.Clear(Color.FromArgb(0, 0, 0));
            tmp.DrawText(t, Color.White, 0, 0);
            int x0 = cx - t.Length * 8;
            int argb = PixelBuffer.Argb(c);
            for (int y2 = 0; y2 < 16; y2++)
                for (int x2 = 0; x2 < tmp.W; x2++)
                    if ((tmp.Data[y2 * tmp.W + x2] & 0x00FFFFFF) != 0)
                        for (int d = 0; d < 4; d++) b.SetPixel(x0 + x2 * 2 + (d & 1), y + y2 * 2 + (d >> 1), argb);
            tmp.Release();
        }

        // ================================================================
        //  Clavier et souris (la page recoit tout tant qu'elle est ouverte)
        // ================================================================
        public static void Touche(KeyEvent k)
        {
            if (k.Key == ConsoleKeyEx.Enter) { Valider(); return; }
            if (k.Key == ConsoleKeyEx.Tab)
            {
                focus = (focus + 1) % (creation ? 3 : 2);
                sale = true; Services.ScreenDirty = true;
                return;
            }
            // Fleches gauche / droite : compte precedent / suivant (quand on n'ecrit pas l'identifiant)
            if (!creation && !verrouillage && focus != 0 &&
                (k.Key == ConsoleKeyEx.LeftArrow || k.Key == ConsoleKeyEx.RightArrow))
            {
                int i = -1;
                for (int j = 0; j < NbTuiles; j++) if (Comptes.Liste[j].Nom == nom.Text) i = j;
                i = k.Key == ConsoleKeyEx.LeftArrow ? (i <= 0 ? NbTuiles - 1 : i - 1) : (i + 1) % NbTuiles;
                Choisir(i);
                return;
            }
            if (verrouillage && focus == 0) focus = 1;          // le nom reste celui de la session
            TextField f = focus == 0 ? nom : (focus == 1 ? mdp : confirmation);
            if (f.Key(k)) { message = ""; sale = true; Services.ScreenDirty = true; }
        }

        public static void Souris(int mx, int my, bool appui)
        {
            bool h = creation && bouton.Hit(0, 0, mx, my), hv = voir.Hit(0, 0, mx, my);
            int dx = mx - FlecheX, dy = my - FlecheY;
            bool hf = !creation && dx * dx + dy * dy <= 26 * 26;
            bool he = !creation && my >= BasY && my < BasY + 26 && mx >= PanneauX + 16 && mx < PanneauX + 16 + LargeurPastille("Eteindre");
            bool hr = !creation && my >= BasY && my < BasY + 26 && mx >= PanneauX + 116 && mx < PanneauX + 116 + LargeurPastille("Redemarrer");
            int t = TuileSous(mx, my);
            if (h != bouton.Hover || hv != voir.Hover || t != survolTuile || hf != survolFleche || he != survolEteindre || hr != survolRedemarrer)
            {
                bouton.Hover = h; voir.Hover = hv; survolTuile = t;
                survolFleche = hf; survolEteindre = he; survolRedemarrer = hr;
                sale = true; Services.ScreenDirty = true;
            }

            if (appui && !sourisAppuyee)
            {
                if (t >= 0) Choisir(t);
                else if (!verrouillage && nom.Hit(mx, my)) focus = 0;
                else if (mdp.Hit(mx, my)) focus = 1;
                else if (creation && confirmation.Hit(mx, my)) focus = 2;
                else if (hv) mdp.Password = !mdp.Password;        // afficher / masquer le mot de passe
                else if (h || hf) Valider();
                else if (he) Alimentation.Arreter();
                else if (hr) Alimentation.Redemarrer();
                sale = true; Services.ScreenDirty = true;
            }
            sourisAppuyee = appui;
        }

        private static void Valider()
        {
            erreur = true;
            sale = true; Services.ScreenDirty = true;

            if (creation)
            {
                if (nom.Text.Length == 0) { message = "Ecrivez un nom d'utilisateur"; return; }
                if (mdp.Text != confirmation.Text) { message = "Les mots de passe ne correspondent pas"; return; }
                // Chaque etape s'affiche en bas de l'ecran : si tout se fige, on sait ou
                FileSystemOS.Noyau.Suivi.Commencer();
                FileSystemOS.Noyau.Suivi.Etape("creation du compte " + nom.Text);
                string err = Comptes.Create(nom.Text, mdp.Text, true);
                if (err != null) { message = err; FileSystemOS.Noyau.Suivi.Finir(); return; }
                FileSystemOS.Noyau.Suivi.Etape("journal : creation de l'administrateur");
                Journal.Ajouter("creation administrateur", nom.Text);
                FileSystemOS.Noyau.Suivi.Etape("enregistrement de la session");
                FileSystemOS.Systeme.Live.SaveIfActive();          // le compte est aussitot ecrit (SSD ou disque de sauvegarde)
                Compte nouveau = Comptes.Find(nom.Text);
                Fermer();
                Session.Start(nouveau);
                FileSystemOS.Noyau.Suivi.Finir();
                return;
            }

            if (nom.Text.Length == 0) { message = "Veuillez entrer votre identifiant"; focus = 0; return; }
            Compte c = Comptes.Check(nom.Text, mdp.Text);
            mdp.Text = "";
            if (c == null)
            {
                // Meme message dans les deux cas : on ne revele pas si le compte existe
                message = "Identifiant ou mot de passe incorrect";
                Journal.Ajouter("echec de connexion", nom.Text);
                focus = 1;
                return;
            }
            erreur = false;
            Fermer();
            FileSystemOS.Noyau.Suivi.Commencer();
            Session.Start(c);
            FileSystemOS.Noyau.Suivi.Finir();
        }
    }
}
