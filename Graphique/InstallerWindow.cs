using System;
using System.Collections.Generic;
using System.IO;
using Cosmos.System.FileSystem;
using Cosmos.System.FileSystem.VFS;
using FileSystemOS.Processus;
using FileSystemOS.Pilotes;
using FileSystemOS.Utils;

namespace FileSystemOS.Graphique
{
    /// <summary>Une cible d'installation : un lecteur FAT deja pret, ou un disque vierge.</summary>
    public class InstallTarget
    {
        public string Volume;   // lecteur existant (null = disque entier a formater)
        public int DiskIndex;   // index dans CosmosVFS.Disks (pour un disque a formater)
        public string Label;
        public bool Wipe;       // lecteur : effacer tout son contenu avant d'installer
        public Cosmos.HAL.BlockDevice.BlockDevice CloneTo;   // installation complete (copie demarrable)

        public bool IsClone => CloneTo != null;
        public bool Blank => Volume == null && CloneTo == null;   // vrai = on formate le disque entier
    }

    /// <summary>
    /// Installe l'environnement File System OS.
    ///  - Lecteur FAT32 existant : ajoute \FSOS et les exemples (rien n'est efface).
    ///  - Disque VIERGE (ex : disque neuf d'une machine virtuelle) : cree une partition,
    ///    la formate en FAT32, la monte, puis installe.
    /// S'ouvre tout seul au premier demarrage si File System OS n'est pas encore installe.
    /// </summary>
    public class InstallerWindow : Window
    {
        private const int DriveY = 64, DriveRowH = 26, MaxTargets = 5, LogY = 286;
        private const int OptionY = DriveY + MaxTargets * DriveRowH + 6;   // case "tout effacer"

        private readonly List<InstallTarget> targets = new List<InstallTarget>();
        private readonly List<string> log = new List<string>();
        private readonly Button btn;
        private int selected;
        private bool confirm, running, wipe;

        public InstallerWindow(int x, int y) : base("Installation", x, y, 500, 470, Icons.InstallerIcon)
        {
            btn = new Button("Installer", 20, OptionY + 32, 150, 32, Theme.Green, Theme.GreenHover);
        }

        /// <summary>Trouve le dossier FSOS sur l'un des lecteurs (null si non installe).</summary>
        public static string FindRoot()
        {
            if (!Services.FsReady) return null;
            try
            {
                var vols = VFSManager.GetVolumes();
                for (int i = 0; i < vols.Count; i++)
                {
                    string r = TextCodec.PathJoin(vols[i].mFullPath, "FSOS");
                    if (Directory.Exists(r)) return r;
                }
            }
            catch (Exception) { }
            return null;
        }

        private void ScanTargets()
        {
            targets.Clear();
            if (!Services.FsReady) return;

            // 1. Lecteurs FAT deja montes
            try
            {
                var vols = VFSManager.GetVolumes();
                for (int i = 0; i < vols.Count && targets.Count < MaxTargets; i++)
                {
                    if (FileSystemOS.Systeme.Live.IsLiveVolume(vols[i].mFullPath)) continue;   // disque memoire
                    var t = new InstallTarget();
                    t.Volume = vols[i].mFullPath;
                    t.Label = t.Volume;
                    try { t.Label += "   " + Fmt.Size(Services.FileSystem.GetTotalSize(t.Volume)) + "   pret"; }
                    catch (Exception) { }
                    targets.Add(t);
                }
            }
            catch (Exception) { }

            // 0. Installation COMPLETE et demarrable : copie du disque de demarrage
            try
            {
                if (FileSystemOS.Systeme.DiskClone.FindSource() != null)
                {
                    var dst = FileSystemOS.Systeme.DiskClone.Targets();
                    for (int i = 0; i < dst.Count && targets.Count < MaxTargets; i++)
                    {
                        var t = new InstallTarget();
                        t.CloneTo = dst[i];
                        t.Label = "Disque " + FileSystemOS.Systeme.DiskClone.Number(dst[i]) + " : installation COMPLETE (demarrable)";
                        targets.Insert(0, t);
                    }
                }
            }
            catch (Exception) { }

            // 2. Disques entiers (vierges ou non) : formatage complet
            try
            {
                var disks = Services.FileSystem.Disks;
                for (int i = 0; i < disks.Count && targets.Count < MaxTargets; i++)
                {
                    Disk d = disks[i];
                    if (d.Size < 64L * 1024 * 1024) continue;   // lecteur CD (0 o) ou disque trop petit
                    if (FileSystemOS.Systeme.Live.Disk != null && d.Size == FileSystemOS.Systeme.Live.Disk.Bytes) continue; // disque memoire
                    var t = new InstallTarget();
                    t.DiskIndex = i;
                    string genre = FileSystemOS.Systeme.Stockage.Genre(d.Size);
                    t.Label = "Disque " + (i + 1) + " " + genre + "   " + Fmt.Size(d.Size) + "   " +
                              (d.Partitions.Count == 0 ? "VIERGE" : d.Partitions.Count + " partition(s) - a formater");
                    targets.Add(t);
                }
            }
            catch (Exception) { }
        }

        public override void OnOpen()
        {
            if (!running)
            {
                ScanTargets();
                if (selected >= targets.Count) selected = 0;
                confirm = false;
                UpdateButton();
                log.Clear();
                if (targets.Count == 0)
                {
                    Log("Aucun disque utilisable. Etat du stockage :");
                    var st = FileSystemOS.Systeme.Stockage.Rapport();
                    for (int i = 0; i < st.Count; i++) Log(st[i]);
                    Log("-> " + FileSystemOS.Systeme.Stockage.Conseil());
                }
                string root = FindRoot();
                if (root != null) Log("Deja installe dans " + root + " (reinstaller = mettre a jour).");
                else Log("Bienvenue ! Choisissez un disque pour installer.");
                if (FileSystemOS.Systeme.DiskClone.Source != null)
                    Log("Demarre depuis un disque : installation COMPLETE possible (en bleu).");
                else Log("Demarre sur ISO/cle : pour un disque demarrable, voir le guide.");
            }
        }

        private void UpdateButton()
        {
            bool blank = targets.Count > 0 && targets[selected].Blank;
            bool clone = targets.Count > 0 && targets[selected].IsClone;
            bool danger = blank || clone || (targets.Count > 0 && wipe);
            btn.Label = confirm ? "Confirmer ?" : (clone ? "Installer (copie complete)" : (blank ? "Formater + installer" : (wipe ? "Effacer + installer" : "Installer")));
            btn.Normal = danger ? Theme.Red : Theme.Green;
            btn.HoverColor = danger ? Theme.RedHover : Theme.GreenHover;
            btn.W = btn.Label.Length * 8 + 24;
            Dirty = true;
        }

        public void Log(string s)
        {
            log.Add(s);
            while (log.Count > 9) log.RemoveAt(0);
            Dirty = true;
        }

        public void Finished()
        {
            running = false;
            ScanTargets();
            if (selected >= targets.Count) selected = 0;
            UpdateButton();
            Services.Taskbar.Invalidate();
            Services.ExplorerWin.Reload();
        }

        protected override void DrawContent(PixelBuffer b, int cx, int cy, int cw, int ch)
        {
            b.DrawText("Installer " + Services.OsName + " sur ce PC", Theme.Text, 20, cy + 14);
            string by = Services.Company;
            b.DrawText(by, Theme.TextMuted, W - by.Length * 8 - 20, cy + 14);
            b.DrawText("1. Choisissez le disque :", Theme.TextMuted, 20, cy + 40);

            for (int i = 0; i < targets.Count; i++)
            {
                int y = cy + DriveY + i * DriveRowH;
                if (i == selected) b.FillRect(Theme.RowSelected, 16, y, W - 32, DriveRowH - 2);
                b.FillCircle(Theme.TextMuted, 30, y + 12, 6);
                b.FillCircle(Theme.WindowBg, 30, y + 12, 4);
                if (i == selected) b.FillCircle(Theme.TitleActive, 30, y + 12, 3);
                b.DrawText(targets[i].Label, targets[i].IsClone ? Theme.Blue : (targets[i].Blank ? Theme.Red : Theme.Text), 46, y + 4);
            }

            // Option pour un lecteur existant : tout effacer avant d'installer
            bool isVolume = targets.Count > 0 && targets[selected].Volume != null;
            if (isVolume)
            {
                b.DrawRect(Theme.TextMuted, 20, cy + OptionY + 4, 16, 16);
                if (wipe) Glyphs.Check.DrawTinted(b, 23, cy + OptionY + 7, Theme.Red);
                b.DrawText("Supprimer tous les dossiers et fichiers du lecteur", wipe ? Theme.Red : Theme.Text, 44, cy + OptionY + 4);
            }
            else if (targets.Count > 0 && targets[selected].Blank && !Machine.Virtuel)
            {
                b.DrawText("Le disque sera formate en FAT32 (fichiers installes).", Theme.Red, 20, cy + OptionY + 4);
                b.DrawText("Pour qu'il DEMARRE seul : outil creer-disque.sh (voir le guide).", Theme.TextMuted, 20, cy + OptionY + 22);
            }
            else if (targets.Count > 0 && targets[selected].IsClone)
                b.DrawText("Copie du disque de demarrage : le disque choisi est EFFACE et demarrera seul.", Theme.Red, 20, cy + OptionY + 4);
            else if (targets.Count > 0)
                b.DrawText("Le disque entier sera formate en FAT32.", Theme.Red, 20, cy + OptionY + 4);

            btn.Draw(b, 0, cy);
            if (targets.Count > 0 && (targets[selected].Blank || targets[selected].IsClone || wipe))
                b.DrawText("Tout sera EFFACE", Theme.Red, btn.RX + btn.W + 14, cy + btn.RY + 8);

            b.FillRect(Theme.ConsoleBg, 16, cy + LogY - 30, W - 32, ch - LogY + 22);
            for (int i = 0; i < log.Count; i++)
            {
                string l = log[i];
                int max = (W - 52) / 8;
                if (l.Length > max) l = l.Substring(0, max);
                b.DrawText(l, l.StartsWith("Erreur") ? Theme.ConsoleError : Theme.ConsoleText, 26, cy + LogY - 22 + i * 18);
            }
        }

        public override void OnMouseMove(int mx, int my)
        {
            bool h = mx >= 0 && btn.Hit(X, Y + TitleH, mx, my);
            if (h != btn.Hover) { btn.Hover = h; Dirty = true; }
        }

        public override void OnMouseDown(int mx, int my)
        {
            int lx = mx - X, ly = my - Y - TitleH;
            btn.Pressed = btn.Hit(0, 0, lx, ly);
            if (!running && targets.Count > 0 && targets[selected].Volume != null &&
                ly >= OptionY && ly < OptionY + 24 && lx >= 16 && lx < W - 16)
            {
                wipe = !wipe;
                confirm = false;
                UpdateButton();
                return;
            }
            if (!running && ly >= DriveY && ly < DriveY + targets.Count * DriveRowH && lx > 16 && lx < W - 16)
            {
                selected = (ly - DriveY) / DriveRowH;
                confirm = false;
                UpdateButton();
            }
        }

        public override void OnMouseUp(int mx, int my)
        {
            int lx = mx - X, ly = my - Y - TitleH;
            if (btn.Pressed && btn.Hit(0, 0, lx, ly) && !running && targets.Count > 0)
            {
                InstallTarget t = targets[selected];
                t.Wipe = t.Volume != null && wipe;
                if (!confirm)
                {
                    confirm = true;
                    UpdateButton();
                    if (t.IsClone) Log("ATTENTION : le disque " + FileSystemOS.Systeme.DiskClone.Number(t.CloneTo) + " sera entierement remplace. Cliquez encore.");
                    else Log(t.Blank ? "ATTENTION : tout le disque sera formate. Cliquez encore."
                       : (t.Wipe ? "ATTENTION : tout le contenu de " + t.Volume + " sera supprime. Cliquez encore."
                                 : "Cliquez encore pour installer sur " + t.Volume));
                }
                else
                {
                    confirm = false;
                    running = true;
                    btn.Label = "En cours...";
                    Process p = Services.Scheduler.Spawn("installation", false);
                    if (t.IsClone) p.Start(new FileSystemOS.Systeme.CloneThread(this, FileSystemOS.Systeme.DiskClone.Source, t.CloneTo));
                    else p.Start(new InstallThread(this, t));
                    Log("Installation lancee (PID " + p.Pid + ")");
                }
            }
            btn.Pressed = false;
        }
    }

    /// <summary>
    /// Une etape tous les 15 tours (on voit la progression).
    /// Etapes 0-2 : preparation d'un disque vierge ; etapes 10+ : copie des fichiers.
    /// </summary>
    public class InstallThread : KThread
    {
        private readonly InstallerWindow win;
        private readonly InstallTarget target;
        private string root;
        private int step, wait;
        private List<string> volumesBefore;
        private List<string> delPaths;
        private List<bool> delIsDir;
        private int delIndex;

        public InstallThread(InstallerWindow w, InstallTarget t) : base("install")
        {
            win = w;
            target = t;
            if (t.Blank) step = 0;
            else
            {
                root = TextCodec.PathJoin(t.Volume, "FSOS");
                step = t.Wipe ? 20 : 10;
            }
        }

        public override void Step()
        {
            // La suppression tourne a chaque tour (par lots) ; les autres etapes sont espacees
            if (step < 20 && ++wait < 15) return;
            wait = 0;
            try
            {
                switch (step)
                {
                    // ---- Disque vierge ----
                    case 0:
                        {
                            volumesBefore = CurrentVolumes();
                            Disk d = Services.FileSystem.Disks[target.DiskIndex];
                            d.Clear();
                            int sizeMb = (int)(d.Size / (1024 * 1024)) - 1;
                            if (sizeMb < 64) throw new Exception("disque trop petit (64 Mo minimum)");
                            if (sizeMb > 2048) sizeMb = 2048;   // FAT32 de Cosmos plus fiable sur 2 Go max
                            d.CreatePartition(sizeMb);
                            win.Log("Partition creee (" + sizeMb + " Mo)");
                            step = 1;
                            return;
                        }
                    case 1:
                        Services.FileSystem.Disks[target.DiskIndex].FormatPartition(0, "FAT32", true);
                        win.Log("Formatage FAT32 termine");
                        step = 2;
                        return;
                    case 2:
                        {
                            Services.FileSystem.Disks[target.DiskIndex].MountPartition(0);
                            string vol = NewVolume();
                            if (vol == null)
                            {
                                var now = CurrentVolumes();
                                if (now.Count == 0) throw new Exception("partition creee mais non montee : redemarrez");
                                vol = now[0]; // disque deja monte avant : meme lettre de lecteur
                            }
                            win.Log("Lecteur monte : " + vol);
                            root = TextCodec.PathJoin(vol, "FSOS");
                            step = 10;
                            return;
                        }

                    // ---- Effacement du contenu d'un lecteur (par lots : l'OS reste fluide) ----
                    case 20:
                        delPaths = new List<string>();
                        delIsDir = new List<bool>();
                        FsTools.Collect(target.Volume, delPaths, delIsDir);
                        delIndex = 0;
                        win.Log(delPaths.Count + " element(s) a supprimer sur " + target.Volume);
                        step = 21;
                        return;
                    case 21:
                        {
                            int end = delIndex + 25 < delPaths.Count ? delIndex + 25 : delPaths.Count;
                            for (; delIndex < end; delIndex++) FsTools.DeleteEntry(delPaths[delIndex], delIsDir[delIndex]);
                            if (delIndex >= delPaths.Count)
                            {
                                win.Log("Lecteur vide : " + delPaths.Count + " element(s) supprime(s)");
                                step = 10;
                            }
                            else if (delIndex % 100 == 0) win.Log("Suppression " + delIndex + " / " + delPaths.Count);
                            return;
                        }

                    // ---- Copie des fichiers ----
                    case 10: MakeDir(root); break;
                    case 11: MakeDir(root + "\\Systeme"); break;
                    case 12: MakeDir(root + "\\Applications"); break;
                    case 13: MakeDir(root + "\\Documents"); break;
                    case 14: Write(root + "\\Systeme\\version.txt", Samples.Version); break;
                    case 15: Write(root + "\\Documents\\Bienvenue.txt", Samples.Welcome); break;
                    case 16: Write(root + "\\Applications\\Bonjour.fsl", Samples.Hello); break;
                    case 17: Write(root + "\\Applications\\Compteur.fsl", Samples.Counter); break;
                    case 18: Write(root + "\\Applications\\Nul.fsl", Samples.Nul); break;
                    case 19:
                        // Fichiers internes charges a chaque demarrage
                        for (int i = 0; i < FileSystemOS.Systeme.Config.Files.Length; i++)
                        {
                            string f = FileSystemOS.Systeme.Config.Files[i];
                            string p = root + "\\Systeme\\" + f;
                            if (!File.Exists(p)) Write(p, FileSystemOS.Systeme.Config.Default(f));
                        }
                        break;
                    default:
                        win.Log("Installation terminee !");
                        win.Log("Les applications sont dans le menu.");
                        win.Finished();
                        Owner.Kill();
                        return;
                }
                step++;
            }
            catch (Exception e)
            {
                win.Log("Erreur : " + e.Message);
                string m = e.Message.ToLower();
                if (m.IndexOf("unallocated directory entry") >= 0)
                {
                    win.Log("-> Le FAT32 de ce lecteur n'est pas lisible");
                    win.Log("   par Cosmos. Utilisez un disque VHD");
                    win.Log("   formate en FAT32 par Windows (voir guide).");
                }
                else if (m.IndexOf("size") >= 0)
                    win.Log("-> Ce disque ne peut pas etre partitionne ici.");
                win.Finished();
                Owner.Kill();
            }
        }

        private static List<string> CurrentVolumes()
        {
            var r = new List<string>();
            var vols = VFSManager.GetVolumes();
            for (int i = 0; i < vols.Count; i++) r.Add(vols[i].mFullPath);
            return r;
        }

        /// <summary>Le lecteur apparu apres le montage.</summary>
        private string NewVolume()
        {
            var now = CurrentVolumes();
            for (int i = 0; i < now.Count; i++)
                if (!FileSystemOS.Utils.Listes.Contient(volumesBefore, now[i])) return now[i];
            return null;
        }

        private void MakeDir(string p)
        {
            FileSystemOS.Utils.FsTools.MakeDir(p);
            win.Log("Dossier  " + p);
        }

        private void Write(string p, string content)
        {
            FileSystemOS.Utils.FsTools.WriteText(p, content);
            win.Log("Fichier  " + p);
        }
    }

    /// <summary>Fichiers installes par defaut.</summary>
    public static class Samples
    {
        public const string Version = "File System OS\nVersion 1.0\nEditeur : File System OS Company\nAuteur : Vincent Senut\nLangage : File Syst Langage (FSL)\n";

        public const string Welcome =
            "Bienvenue dans File System OS !\n\n" +
            "- Explorateur : '+ Fichier' et '+ Dossier' pour creer.\n" +
            "- Editeur : Ctrl+S pour enregistrer, F5 pour executer un .fsl.\n" +
            "- Ajoutez vos propres programmes .fsl dans \\FSOS\\Applications :\n" +
            "  ils apparaissent automatiquement dans le menu.\n";

        public const string Calculs =
            "// Le File Syst Langage, version 2 : fonctions, retourner, booleens, boucles pour\n" +
            "fonction entier factorielle(entier n) {\n" +
            "    si (n <= 1) { retourner 1; }\n" +
            "    retourner n * factorielle(n - 1);\n" +
            "}\n" +
            "\n" +
            "fonction booleen est_premier(entier n) {\n" +
            "    si (n < 2) { retourner faux; }\n" +
            "    pour (entier d = 2; d * d <= n; d = d + 1) {\n" +
            "        si (n % d == 0) { retourner faux; }\n" +
            "    }\n" +
            "    retourner vrai;\n" +
            "}\n" +
            "\n" +
            "fonction texte oui_non(booleen b) {\n" +
            "    si (b) { retourner \"oui\"; }\n" +
            "    retourner \"non\";\n" +
            "}\n" +
            "\n" +
            "pour (entier i = 1; i <= 6; i = i + 1) {\n" +
            "    afficher(vers_texte(i) + \"! = \" + vers_texte(factorielle(i)));\n" +
            "}\n" +
            "texte liste = \"\";\n" +
            "pour (entier i = 2; i < 40; i = i + 1) {\n" +
            "    si (est_premier(i)) { liste = liste + vers_texte(i) + \" \"; }\n" +
            "}\n" +
            "afficher(\"Nombres premiers : \" + liste);\n" +
            "afficher(\"97 est premier ? \" + oui_non(est_premier(97)));\n";

        public const string Fenetre =
            "// Fenetres, boutons et zones de texte en File Syst Langage\n" +
            "int f = fenetre(\"Bonjour en FSL\", 420, 300);\n" +
            "int titre = etiquette(f, \"Ecrivez votre prenom :\", 20, 20);\n" +
            "int nom = champ(f, 20, 44, 260);\n" +
            "int ok = bouton(f, \"Dire bonjour\", 290, 42, 110, 30);\n" +
            "int journal = zone(f, 20, 90, 380, 150);\n" +
            "int fini = 0;\n" +
            "tantque (fini == 0) {\n" +
            "    texte e = evenement();\n" +
            "    si (e == \"clic:\" + vers_texte(ok) || e == \"valider:\" + vers_texte(nom)) {\n" +
            "        ecrire(journal, lire(journal) + \"Bonjour \" + lire(nom) + \" !\\n\");\n" +
            "        ecrire(nom, \"\");\n" +
            "    }\n" +
            "    si (e == \"fermeture:\" + vers_texte(f)) { fini = 1; }\n" +
            "}\n";

        public const string Balle =
            "// Petit jeu : rattrapez la balle avec la raquette (fleches gauche et droite)\n" +
            "int f = fenetre(\"Balle\", 420, 330);\n" +
            "int d = dessin(f, 10, 10, 400, 280);\n" +
            "int info = etiquette(f, \"Score : 0\", 10, 300);\n" +
            "int x = 200; int y = 40; int dx = 4; int dy = 4;\n" +
            "int r = 160; int score = 0; int jeu = 1;\n" +
            "int fond = rgb(20, 24, 40); int blanc = rgb(255, 255, 255); int bleu = rgb(80, 140, 255);\n" +
            "tantque (jeu == 1) {\n" +
            "    texte e = evenement();\n" +
            "    tantque (e != \"\") {\n" +
            "        si (e == \"touche:gauche\") { r = r - 28; }\n" +
            "        si (e == \"touche:droite\") { r = r + 28; }\n" +
            "        si (e == \"fermeture:\" + vers_texte(f)) { jeu = 0; }\n" +
            "        e = evenement();\n" +
            "    }\n" +
            "    si (r < 0) { r = 0; }\n" +
            "    si (r > 320) { r = 320; }\n" +
            "    x = x + dx; y = y + dy;\n" +
            "    si (x < 0 || x > 390) { dx = -dx; }\n" +
            "    si (y < 0) { dy = -dy; }\n" +
            "    si (dy > 0 && y > 256 && y < 268 && x + 10 > r && x < r + 80) {\n" +
            "        dy = -dy; score = score + 1;\n" +
            "        ecrire(info, \"Score : \" + vers_texte(score));\n" +
            "    }\n" +
            "    si (y > 290) { jeu = 2; }\n" +
            "    effacer(d, fond);\n" +
            "    rectangle(d, x, y, 10, 10, blanc);\n" +
            "    rectangle(d, r, 268, 80, 8, bleu);\n" +
            "    attendre(30);\n" +
            "}\n" +
            "si (jeu == 2) {\n" +
            "    ecrire(info, \"Perdu ! Score : \" + vers_texte(score) + \"  (fermez la fenetre)\");\n" +
            "    tantque (evenement() != \"fermeture:\" + vers_texte(f)) { attendre(50); }\n" +
            "}\n";

        public const string Hello =
            "// Mon premier programme en File Syst Langage\n" +
            "texte nom = \"File System OS\";\n" +
            "afficher(\"Bonjour depuis \" + nom + \" !\");\n";

        public const string Counter =
            "// Compte de 1 a 10\n" +
            "int i = 1;\n" +
            "tantque (i <= 10) {\n" +
            "    si (i % 2 == 0) {\n" +
            "        afficher(i + \" est pair\");\n" +
            "    } sinon {\n" +
            "        afficher(i + \" est impair\");\n" +
            "    }\n" +
            "    i = i + 1;\n" +
            "}\n";

        public const string Nul =
            "// Les variables nulles : 0/nul ou 0.nul\n" +
            "int a = 0;       // le nombre zero\n" +
            "int b = 0.nul;   // variable nulle\n" +
            "int c;           // sans valeur : nulle aussi\n" +
            "afficher(\"a = \" + a);\n" +
            "afficher(\"b = \" + b);\n" +
            "si (b == 0/nul) {\n" +
            "    afficher(\"b est nulle\");\n" +
            "}\n" +
            "b = 5;\n" +
            "afficher(\"b vaut maintenant \" + b);\n";
    }
}
