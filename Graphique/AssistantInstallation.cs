using System;
using System.Collections.Generic;
using System.Drawing;
using Cosmos.HAL.BlockDevice;
using Cosmos.System;
using FileSystemOS.Demarrage;
using FileSystemOS.Pilotes;
using FileSystemOS.Systeme;
using FileSystemOS.Utils;

namespace FileSystemOS.Graphique
{
    /// <summary>
    /// ASSISTANT D'INSTALLATION plein ecran, affiche AVANT la page de connexion.
    ///
    ///   Accueil      : installer sur le SSD, ou essayer en mode Live
    ///   1. Disque    : choisir le SSD (SATA ou NVMe)
    ///   2. Partition : choisir ou installer ; seules les partitions FAT32 sont proposees,
    ///                  celles de Windows (NTFS, EFI) sont affichees mais protegees
    ///   3. Confirmer : resume de ce qui va se passer
    ///   Fin          : redemarrer (File System OS demarre alors sur la partition choisie)
    ///                  ou continuer en mode Live
    ///
    /// Il ne s'ouvre que si un SSD est detecte et que File System OS n'y est pas encore installe.
    /// Sans SSD, le demarrage passe directement en mode Live.
    /// Tout passe par le pilote FAT32 de File System OS (FatDirect) : rien n'est formate, on
    /// ajoute seulement un dossier FSOS sur la partition choisie.
    /// Souris : clic sur les lignes et les boutons. Clavier : chiffres 1 a 9 pour une ligne,
    /// Entree pour le bouton principal, Echap pour revenir.
    /// </summary>
    public static class AssistantInstallation
    {
        public static bool Active;

        private class Choix
        {
            public string Titre = "", Detail = "";
            public bool Possible, Conseille;
            public BlockDevice Disque;
            public string NomDisque = "", Modele = "";
            public long Octets;
            public PartitionInfo Partition;
            public EspaceLibre Espace;           // espace non alloue : la partition FSOS y sera creee
        }

        private const int Accueil = 0, Disques = 1, Partitions = 2, Confirmation = 3, EnCours = 4, Fin = 5;
        private const int CarteW = 720, CarteH = 500, ListeY = 112, LigneH = 46, MaxLignes = 6;

        private static int etape;
        private static PixelBuffer page;
        private static bool sale = true, sourisAppuyee, reussi;
        private static int survol = -1, attente;
        private static string message = "";
        private static readonly List<Choix> liste = new List<Choix>();
        private static readonly List<string> rapport = new List<string>();
        private static Choix disque, partition;
        private static Button principal, secondaire;

        private static int CarteX => Math.Max(0, (Services.ScreenW - CarteW) / 2);
        private static int CarteY => Math.Max(0, (Services.ScreenH - CarteH) / 2);

        // ================================================================
        //  Ouverture
        // ================================================================

        /// <summary>Faut-il proposer l'installation ? Non si deja installe, ou si aucun SSD.</summary>
        public static bool DoitSAfficher()
        {
            if (SessionSsd.Pret) return false;                  // session du SSD deja chargee
            if (SessionSsd.Volume != null) return false;        // installe sur le SSD (meme si non chargee)
            if (VueFsos.Vues.Count > 0) return false;           // installe sur le SSD : deja monte
            // Un environnement \FSOS sur un vrai disque (pas le disque memoire) : deja installe
            string racine = InstallerWindow.FindRoot();
            if (racine != null && (Live.Volume == null || !racine.StartsWith(Live.Volume))) return false;
            return Ahci.Disques.Count + Nvme.Disques.Count > 0; // aucun SSD : mode Live directement
        }

        private static bool auDemarrage;

        /// <summary>Au demarrage, avant la page de connexion.</summary>
        public static void Ouvrir()
        {
            auDemarrage = true;
            OuvrirPage();
        }

        /// <summary>Depuis la Console (commande "installation") : se ferme sans deconnecter.</summary>
        public static void OuvrirDepuisConsole()
        {
            auDemarrage = false;
            OuvrirPage();
        }

        private static void OuvrirPage()
        {
            if (page == null || page.W != Services.ScreenW || page.H != Services.ScreenH)
                page = new PixelBuffer("assistant d'installation", Services.ScreenW, Services.ScreenH);
            Active = true;
            Aller(Accueil);
        }

        private static void ModeLive()
        {
            Active = false;
            Services.ScreenDirty = true;
            if (auDemarrage && !Session.ConnexionAuto()) Session.ShowLogin();
        }

        private static string Sortie => auDemarrage ? "Mode Live" : "Fermer";

        // ================================================================
        //  Etapes
        // ================================================================

        private static void Aller(int e)
        {
            etape = e;
            survol = -1;
            message = "";
            liste.Clear();
            principal = null;
            secondaire = null;

            int y = CarteY + CarteH - 58;
            int xg = CarteX + 30, xd = CarteX + CarteW - 30 - 240;
            Color gris = Theme.TitleInactive, grisClair = Theme.TitleActive;

            switch (e)
            {
                case Accueil:
                    principal = new Button(Glyphs.Check, "Installer sur le SSD", xd, y, 240, 38, Theme.Blue, Theme.BlueHover);
                    secondaire = new Button(auDemarrage ? "Essayer en mode Live" : "Fermer", xg, y, 220, 38, gris, grisClair);
                    break;
                case Disques:
                    ListerDisques();
                    principal = new Button(Sortie, xd + 100, y, 140, 38, gris, grisClair);
                    secondaire = new Button(Glyphs.Back, "Retour", xg, y, 140, 38, gris, grisClair);
                    break;
                case Partitions:
                    ListerPartitions();
                    principal = new Button(Sortie, xd + 100, y, 140, 38, gris, grisClair);
                    secondaire = new Button(Glyphs.Back, "Retour", xg, y, 140, 38, gris, grisClair);
                    break;
                case Confirmation:
                    principal = new Button(Glyphs.Check, "Installer", xd, y, 240, 38, Theme.Green, Theme.GreenHover);
                    secondaire = new Button(Glyphs.Back, "Retour", xg, y, 140, 38, gris, grisClair);
                    break;
                case EnCours:
                    attente = 3;                                // laisser l'ecran s'afficher avant de copier
                    break;
                case Fin:
                    if (reussi)
                    {
                        principal = new Button(Glyphs.Check, "Ouvrir la session", xd, y, 240, 38, Theme.Blue, Theme.BlueHover);
                        secondaire = new Button("Redemarrer", xg, y, 220, 38, gris, grisClair);
                    }
                    else
                    {
                        principal = new Button("Autre partition", xd, y, 240, 38, Theme.Blue, Theme.BlueHover);
                        secondaire = new Button(Sortie, xg, y, 140, 38, gris, grisClair);
                    }
                    break;
            }
            sale = true;
            Services.ScreenDirty = true;
        }

        private static void Principal()
        {
            switch (etape)
            {
                case Accueil: Aller(Disques); break;
                case Disques:
                case Partitions: ModeLive(); break;
                case Confirmation: Aller(EnCours); break;
                case Fin:
                    if (reussi)
                    {
                        // La session vient d'etre COPIEE de la memoire vers le SSD : les deux sont
                        // identiques, on l'adopte sans tout recopier. Chaque etape est suivie a l'ecran.
                        FileSystemOS.Noyau.Suivi.Commencer();
                        FileSystemOS.Noyau.Suivi.Etape("session : rattachement a " + partition.NomDisque +
                                                       " partition " + partition.Partition.Numero);
                        SessionSsd.Adopter(partition.Disque, partition.Partition.Debut, partition.NomDisque);
                        FileSystemOS.Noyau.Suivi.Etape("reglages : lecture");
                        Config.LoadAll();
                        FileSystemOS.Noyau.Suivi.Etape("connexion");
                        ModeLive();
                        FileSystemOS.Noyau.Suivi.Finir();
                    }
                    else Aller(Partitions);
                    break;
            }
        }

        private static void Secondaire()
        {
            switch (etape)
            {
                case Accueil: ModeLive(); break;
                case Disques: Aller(Accueil); break;
                case Partitions: Aller(Disques); break;
                case Confirmation: Aller(Partitions); break;
                case Fin:
                    if (reussi)
                    {
                        if (partition.Disque is Ahci a) a.Vider();     // vider le cache du SSD d'abord
                        Redemarrage.Maintenant();
                    }
                    else ModeLive();
                    break;
            }
        }

        private static void ClicLigne(int i)
        {
            if (i < 0 || i >= liste.Count) return;
            Choix c = liste[i];
            if (!c.Possible) { message = c.Detail; sale = true; Services.ScreenDirty = true; return; }
            if (etape == Disques) { disque = c; Aller(Partitions); }
            else if (etape == Partitions) { partition = c; Aller(Confirmation); }
        }

        // ================================================================
        //  Disques et partitions
        // ================================================================

        private static void ListerDisques()
        {
            for (int i = 0; i < Ahci.Disques.Count; i++)
                AjouterDisque(Ahci.Disques[i], "sata" + i, Ahci.Disques[i].Modele, Ahci.Disques[i].Octets);
            for (int i = 0; i < Nvme.Disques.Count; i++)
                AjouterDisque(Nvme.Disques[i], "nvme" + i, Nvme.Disques[i].Modele, Nvme.Disques[i].Octets);
        }

        private static void AjouterDisque(BlockDevice d, string nom, string modele, long octets)
        {
            var c = new Choix { Disque = d, NomDisque = nom, Modele = modele.Trim(), Octets = octets, Possible = true };
            int n = 0, utilisables = 0;
            try
            {
                var parts = Gpt.Lire(d, out bool gpt);
                n = parts.Count;
                for (int i = 0; i < parts.Count; i++)
                    if (!parts[i].Type.StartsWith("EFI") && FatDirect.Monter(d, parts[i].Debut, nom, out string e) != null)
                        utilisables++;
            }
            catch (Exception) { }
            c.Titre = c.Modele + "  (" + Fmt.Size(octets) + ")";
            c.Detail = nom + " : " + n + " partition(s), dont " + utilisables + " en FAT32 utilisable(s)";
            c.Conseille = utilisables > 0;
            liste.Add(c);
        }

        private static void ListerPartitions()
        {
            List<PartitionInfo> parts;
            try { parts = Gpt.Lire(disque.Disque, out bool gpt); }
            catch (Exception e) { message = "Lecture du disque impossible : " + e.Message; return; }

            bool une = false;
            for (int i = 0; i < parts.Count; i++)
            {
                PartitionInfo p = parts[i];
                var c = new Choix { Disque = disque.Disque, NomDisque = disque.NomDisque, Modele = disque.Modele, Partition = p };
                string taille = Fmt.Size(p.Octets);
                FatDirect v = null;
                try { v = FatDirect.Monter(disque.Disque, p.Debut, disque.NomDisque, out string e); } catch (Exception) { }

                if (p.Type.StartsWith("EFI"))
                {
                    c.Titre = "Demarrage de Windows (EFI)   " + taille + "   (partition " + p.Numero + ")";
                    c.Detail = "Protegee : Windows en a besoin pour demarrer.";
                }
                else if (v == null)
                {
                    string type = p.Type == "Donnees Windows" ? "Windows (NTFS)" : p.Type;
                    c.Titre = type + "   " + taille + "   (partition " + p.Numero + ")";
                    c.Detail = "Protegee : ce n'est pas du FAT32 (Windows ou autre systeme).";
                }
                else
                {
                    bool installe = Existe(v, "FSOS");
                    string nom = v.Etiquette.Length > 0 ? v.Etiquette : "sans nom";
                    c.Possible = true;
                    c.Conseille = v.EstFsos || installe;
                    c.Titre = "\"" + nom + "\"   FAT32   " + taille + "   (partition " + p.Numero + ")";
                    c.Detail = installe ? "File System OS y est deja : reinstaller (vos fichiers restent)."
                             : (v.EstFsos ? "Conseillee : partition preparee pour File System OS."
                                          : "FAT32 : un dossier FSOS y sera ajoute, rien n'est efface.");
                    une = true;
                }
                liste.Add(c);
            }
            // Espace non alloue : File System OS peut y creer lui-meme sa partition (en tete de liste)
            EspaceLibre esp = Partitionneur.Chercher(disque.Disque);
            if (esp != null)
            {
                var c = new Choix
                {
                    Disque = disque.Disque, NomDisque = disque.NomDisque, Modele = disque.Modele,
                    Espace = esp, Possible = true, Conseille = !une
                };
                c.Titre = "Espace libre   " + Fmt.Size(esp.Octets) + "   (non utilise)";
                c.Detail = "Creer ici une partition FSOS. Les autres partitions ne sont pas touchees.";
                liste.Insert(0, c);
                une = true;
            }

            if (parts.Count == 0 && esp == null) message = "Aucune partition ni espace libre sur ce disque.";
            else if (!une) message = "Pas de FAT32 ni d'espace libre : reduisez C: dans Windows (Gestion des disques).";
        }

        private static bool Existe(FatDirect v, string chemin)
        {
            try { return v.Existe(chemin); }
            catch (Exception) { return false; }
        }

        // ================================================================
        //  Installation
        // ================================================================

        private static void Installer()
        {
            rapport.Clear();
            reussi = false;

            // Espace libre choisi : File System OS cree d'abord sa partition (et la formate)
            if (partition.Espace != null)
            {
                try
                {
                    FileSystemOS.Noyau.Watchdog.Lent = true;
                    partition.Partition = Partitionneur.Creer(partition.Disque, partition.Espace, out string cr);
                    FileSystemOS.Noyau.Watchdog.Lent = false;
                    rapport.Add(cr);
                    partition.Espace = null;
                }
                catch (Exception ex)
                {
                    FileSystemOS.Noyau.Watchdog.Lent = false;
                    rapport.Add("Partition NON creee : " + ex.Message);
                    rapport.Add("Rien d'autre n'a ete modifie.");
                    Aller(Fin);
                    return;
                }
            }

            FatDirect v = null;
            try
            {
                v = FatDirect.Monter(partition.Disque, partition.Partition.Debut, partition.NomDisque, out string e);
                if (v == null) rapport.Add("Erreur : " + e);
            }
            catch (Exception ex) { rapport.Add("Erreur : " + ex.Message); }

            if (v != null)
            {
                v.Ecriture = true;                              // choix explicite dans l'assistant
                try
                {
                    Ssd.InstallerDans(v, rapport);
                    reussi = Existe(v, "FSOS");
                    if (reussi)
                        Journal.Ajouter("installation par l'assistant",
                                        partition.NomDisque + " partition " + partition.Partition.Numero);
                }
                catch (Exception ex) { rapport.Add("Erreur : " + ex.Message); }
            }
            Aller(Fin);
        }

        // ================================================================
        //  Dessin
        // ================================================================

        public static void Dessiner(PixelBuffer scene)
        {
            if (!Active) return;
            if (sale) { Redessiner(); sale = false; }
            scene.Blit(page, 0, 0);
        }

        public static void Tick()
        {
            if (!Active || etape != EnCours) return;
            if (--attente <= 0) Installer();
            else Services.ScreenDirty = true;
        }

        private static void Redessiner()
        {
            var b = page;
            Color haut = Color.FromArgb(18, 20, 36), bas = Color.FromArgb(30, 52, 80);
            for (int y = 0; y < b.H; y += 4)
            {
                int k = y * 255 / b.H;
                b.FillRect(Color.FromArgb(haut.R + (bas.R - haut.R) * k / 255, haut.G + (bas.G - haut.G) * k / 255,
                                          haut.B + (bas.B - haut.B) * k / 255), 0, y, b.W, 4);
            }

            int cx = CarteX, cy = CarteY;
            Color clair = Color.FromArgb(230, 232, 245), discret = Color.FromArgb(150, 154, 180);
            Color vert = Color.FromArgb(90, 200, 130), rouge = Color.FromArgb(240, 110, 110);

            b.FillRoundRect(Color.FromArgb(70, 76, 120), cx - 1, cy - 1, CarteW + 2, CarteH + 2, 14);
            b.FillRoundRect(BootScreen.Bg, cx, cy, CarteW, CarteH, 14);
            Grand(b, "Installation de File System OS", cx + CarteW / 2, cy + 24, clair);

            string sousTitre = etape == Accueil ? "Bienvenue"
                             : etape == Disques ? "Etape 1 sur 3 : choisissez le disque"
                             : etape == Partitions ? "Etape 2 sur 3 : choisissez ou installer (" + disque.NomDisque + ")"
                             : etape == Confirmation ? "Etape 3 sur 3 : verifiez avant d'installer"
                             : etape == EnCours ? "Installation en cours..." : (reussi ? "Installation terminee" : "L'installation a echoue");
            Centre(b, sousTitre, cx + CarteW / 2, cy + 70, discret);

            int tx = cx + 40, ty = cy + ListeY;
            switch (etape)
            {
                case Accueil:
                    string resume = "";
                    for (int i = 0; i < Ahci.Disques.Count; i++)
                        resume += (resume.Length > 0 ? ", " : "") + Ahci.Disques[i].Modele.Trim() + " (" + Fmt.Size(Ahci.Disques[i].Octets) + ")";
                    for (int i = 0; i < Nvme.Disques.Count; i++)
                        resume += (resume.Length > 0 ? ", " : "") + Nvme.Disques[i].Modele.Trim() + " (" + Fmt.Size(Nvme.Disques[i].Octets) + ")";
                    b.DrawText("Disque detecte : " + resume, vert, tx, ty);
                    b.DrawText("INSTALLER : File System OS s'installe sur une partition FAT32 du SSD.", clair, tx, ty + 40);
                    b.DrawText("Vos reglages, comptes, documents et programmes sont conserves", discret, tx, ty + 60);
                    b.DrawText("d'un demarrage a l'autre. Windows n'est jamais modifie.", discret, tx, ty + 78);
                    b.DrawText("MODE LIVE : File System OS fonctionne en memoire, sans rien installer.", clair, tx, ty + 118);
                    b.DrawText("Tout disparait a l'arret, sauf si vous sauvegardez la session.", discret, tx, ty + 138);
                    break;

                case Disques:
                case Partitions:
                    for (int i = 0; i < liste.Count && i < MaxLignes; i++) DessinerLigne(b, i, clair, discret, vert);
                    break;

                case Confirmation:
                    b.DrawText("Disque     : " + partition.NomDisque + "  " + partition.Modele, clair, tx, ty);
                    if (partition.Espace != null)
                    {
                        b.DrawText("Nouvelle partition : FSOS, FAT32, " + Fmt.Size(partition.Espace.Octets) +
                                   ", dans l'espace libre", clair, tx, ty + 22);
                        b.DrawText("La table des partitions recoit une nouvelle case (verifiee avant", discret, tx, ty + 66);
                        b.DrawText("et apres l'ecriture). Aucune partition n'est deplacee, reduite", discret, tx, ty + 84);
                        b.DrawText("ni effacee. Puis le systeme y est copie.", discret, tx, ty + 102);
                        b.DrawText("Windows et ses fichiers ne sont jamais touches.", vert, tx, ty + 120);
                        b.DrawText("Au prochain demarrage, File System OS utilisera cette partition.", clair, tx, ty + 160);
                        break;
                    }
                    b.DrawText("Partition  : " + partition.Titre, clair, tx, ty + 22);
                    b.DrawText("Un dossier FSOS va etre cree sur cette partition, avec le systeme,", discret, tx, ty + 66);
                    b.DrawText("les reglages, les comptes et les programmes d'exemple.", discret, tx, ty + 84);
                    b.DrawText("Les fichiers deja presents ne sont pas effaces.", discret, tx, ty + 102);
                    b.DrawText("Les autres partitions (Windows) ne sont jamais touchees.", vert, tx, ty + 120);
                    b.DrawText("Au prochain demarrage, File System OS utilisera cette partition.", clair, tx, ty + 160);
                    break;

                case EnCours:
                    b.DrawText(partition.Partition == null ? "Creation de la partition FSOS sur " + partition.NomDisque + "..."
                               : "Copie des fichiers sur " + partition.NomDisque + ", partition " + partition.Partition.Numero + "...", clair, tx, ty);
                    b.DrawText("N'eteignez pas l'ordinateur.", discret, tx, ty + 24);
                    break;

                case Fin:
                    if (reussi)
                    {
                        b.DrawText("File System OS est installe sur " + partition.NomDisque + ", partition " + partition.Partition.Numero + ".", vert, tx, ty);
                        b.DrawText("\"Ouvrir la session\" : vos fichiers sont charges depuis le SSD,", clair, tx, ty + 22);
                        b.DrawText("et enregistres dessus a chaque modification.", clair, tx, ty + 40);
                    }
                    else b.DrawText("Rien n'a ete installe. Detail :", rouge, tx, ty);
                    int debut = Math.Max(0, rapport.Count - 8);
                    for (int i = debut; i < rapport.Count; i++)
                    {
                        string l = rapport[i];
                        if (l.Length > 80) l = l.Substring(0, 80);
                        b.DrawText(l, discret, tx, ty + 60 + (i - debut) * 18);
                    }
                    break;
            }

            if (message.Length > 0)
            {
                string m = message.Length > 84 ? message.Substring(0, 84) : message;
                Centre(b, m, cx + CarteW / 2, cy + CarteH - 92, Color.FromArgb(255, 200, 90));
            }
            if (principal != null) principal.Draw(b, 0, 0);
            if (secondaire != null) secondaire.Draw(b, 0, 0);

            b.DrawText(Services.Company + "  -  " + Services.Author, discret, 20, b.H - 30);
            b.DrawText("Souris, ou : chiffres pour choisir, Entree pour valider, Echap pour revenir", discret, 20, b.H - 50);
        }

        private static void DessinerLigne(PixelBuffer b, int i, Color clair, Color discret, Color vert)
        {
            Choix c = liste[i];
            int x = CarteX + 30, y = CarteY + ListeY + i * LigneH, w = CarteW - 60, h = LigneH - 6;
            Color fond = !c.Possible ? Color.FromArgb(28, 30, 42)
                       : (i == survol ? Theme.TitleActive : Color.FromArgb(44, 48, 72));
            if (c.Conseille && c.Possible) b.FillRoundRect(vert, x - 2, y - 2, w + 4, h + 4, 10);
            b.FillRoundRect(fond, x, y, w, h, 9);
            Color texte = c.Possible ? clair : Color.FromArgb(110, 114, 136);
            b.DrawText((i + 1) + "", c.Possible ? vert : texte, x + 12, y + 12);
            b.DrawText(c.Titre, texte, x + 40, y + 4);
            b.DrawText(c.Detail, c.Possible ? discret : Color.FromArgb(96, 100, 120), x + 40, y + 21);
        }

        private static void Centre(PixelBuffer b, string t, int cx, int y, Color c) =>
            b.DrawText(t, c, cx - t.Length * 4, y);

        /// <summary>Texte deux fois plus gros (chaque lettre dessinee en 2x2).</summary>
        private static void Grand(PixelBuffer b, string t, int cx, int y, Color c)
        {
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
        //  Souris et clavier (l'assistant recoit tout tant qu'il est ouvert)
        // ================================================================

        private static int LigneSous(int mx, int my)
        {
            if (etape != Disques && etape != Partitions) return -1;
            int x = CarteX + 30, w = CarteW - 60;
            if (mx < x || mx >= x + w) return -1;
            int i = (my - (CarteY + ListeY)) / LigneH;
            if (my < CarteY + ListeY || i >= liste.Count || i >= MaxLignes) return -1;
            if ((my - (CarteY + ListeY)) % LigneH >= LigneH - 6) return -1;
            return i;
        }

        public static void Souris(int mx, int my, bool appui)
        {
            bool hp = principal != null && principal.Hit(0, 0, mx, my);
            bool hs = secondaire != null && secondaire.Hit(0, 0, mx, my);
            int s = LigneSous(mx, my);
            bool change = s != survol;
            if (principal != null && principal.Hover != hp) { principal.Hover = hp; change = true; }
            if (secondaire != null && secondaire.Hover != hs) { secondaire.Hover = hs; change = true; }
            if (change) { survol = s; sale = true; Services.ScreenDirty = true; }

            if (appui && !sourisAppuyee)
            {
                if (hp) Principal();
                else if (hs) Secondaire();
                else if (s >= 0) ClicLigne(s);
            }
            sourisAppuyee = appui;
        }

        public static void Touche(KeyEvent k)
        {
            if (etape == EnCours) return;
            if (k.Key == ConsoleKeyEx.Enter) { if (principal != null) Principal(); return; }
            if (k.Key == ConsoleKeyEx.Escape || k.Key == ConsoleKeyEx.Backspace) { if (secondaire != null) Secondaire(); return; }
            char ch = k.KeyChar;
            if (ch >= '1' && ch <= '9') ClicLigne(ch - '1');
        }
    }
}
