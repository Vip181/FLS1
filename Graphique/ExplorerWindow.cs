using System;
using System.Collections.Generic;
using System.IO;
using Cosmos.System;
using Cosmos.System.FileSystem.Listing;
using Cosmos.System.FileSystem.VFS;
using FileSystemOS.Fsl;
using FileSystemOS.Utils;

namespace FileSystemOS.Graphique
{
    public class FileEntry
    {
        public string Name, FullPath;
        public bool IsDir, IsDrive;
        public long Size;

        public FileEntry(string name, string fullPath, bool isDir, bool isDrive, long size)
        {
            Name = name; FullPath = fullPath; IsDir = isDir; IsDrive = isDrive; Size = size;
        }
    }

    /// <summary>
    /// Explorateur de fichiers facon Windows :
    /// barre d'outils (precedent, dossier parent, actualiser, chemin),
    /// panneau de navigation (Ce PC + lecteurs), liste Nom / Type / Taille, barre d'etat.
    /// Double-clic : ouvrir un dossier, ou un fichier dans le lecteur texte.
    /// </summary>
    public class ExplorerWindow : Window
    {
        private const int ToolH = 38, SideW = 150, HeadH = 22, RowH = 22, StatusH = 22, SideRowH = 26;

        private readonly List<FileEntry> entries = new List<FileEntry>();
        private readonly List<string> drives = new List<string>();
        private readonly List<string> back = new List<string>();
        private readonly ScrollBar sb = new ScrollBar();
        private readonly Button btnBack, btnUp, btnRefresh, btnNewFile, btnNewDir, btnDelete;
        private readonly HexBitmap icoFolder, icoText, icoFile, icoDrive, icoApp, icoFsc;
        private readonly HexBitmap icoImage = new HexBitmap(Icons.ImageIcon, Icons.PaletteDe(Icons.ImageIcon));
        private readonly HexBitmap icoPdf = new HexBitmap(Icons.PdfIcon, Icons.PaletteDe(Icons.PdfIcon));

        // Saisie du nom d'un nouveau fichier / dossier (dans la barre d'etat)
        private bool prompting, promptDir;
        private bool confirmDelete;      // "Supprimer X ?" en attente de reponse
        private string promptText = "";

        private string path = "";      // "" = Ce PC
        private string status = "";
        private int selected = -1, hover = -1, sideHover = -1;
        private int lastClickIndex = -1;
        private uint lastClickTick;

        public ExplorerWindow(int x, int y) : base("Explorateur", x, y, 720, 430, Icons.Folder)
        {
            btnBack = new Button(Glyphs.Back, "", 8, 6, 32, 26, Theme.Blue, Theme.BlueHover);
            btnUp = new Button(Glyphs.Up, "", 44, 6, 32, 26, Theme.Blue, Theme.BlueHover);
            btnRefresh = new Button(Glyphs.Refresh, "", 80, 6, 32, 26, Theme.Blue, Theme.BlueHover);
            btnNewFile = new Button(Glyphs.Plus, "Fichier", 120, 6, 96, 26, Theme.Green, Theme.GreenHover);
            btnNewDir = new Button(Glyphs.NewFolder, "Dossier", 220, 6, 96, 26, Theme.Green, Theme.GreenHover);
            btnDelete = new Button(Glyphs.Trash, "", 320, 6, 32, 26, Theme.Red, Theme.RedHover);

            icoFolder = new HexBitmap(Icons.Folder, Icons.FolderPalette);
            icoText = new HexBitmap(Icons.TextFile, Icons.PaletteDe(Icons.TextFile));
            icoFile = new HexBitmap(Icons.PlainFile(), Icons.Palette);
            icoDrive = new HexBitmap(Icons.Drive, Icons.PaletteDe(Icons.Drive));
            icoApp = new HexBitmap(Icons.FslApp, Icons.PaletteDe(Icons.FslApp));
            icoFsc = new HexBitmap(Icons.FslCompiled(), Icons.Palette);

            Navigate("", false);
        }

        // ---- Geometrie (coordonnees locales) ----
        private int BodyY => TitleH + ToolH;
        private int BodyH => H - StatusH - BodyY;
        private int SideY0 => BodyY + 30;
        private int ListX => SideW + 1;
        private int RowsY => BodyY + HeadH;
        private int RowsH => H - StatusH - RowsY;
        private int SbX => W - ScrollBar.Width - 2;
        private int TypeX => W - 210;
        private int SizeX => W - 110;

        private int RowAt(int lx, int ly)
        {
            if (lx < ListX || lx >= SbX || ly < RowsY || ly >= RowsY + RowsH) return -1;
            int idx = sb.Value + (ly - RowsY) / RowH;
            return idx < entries.Count ? idx : -1;
        }

        private int SideAt(int lx, int ly)
        {
            if (lx >= SideW || ly < SideY0 || ly >= BodyY + BodyH) return -1;
            int idx = (ly - SideY0) / SideRowH;
            return idx <= drives.Count ? idx : -1; // 0 = Ce PC, 1.. = lecteurs
        }

        // ---- Navigation ----
        public void Navigate(string p, bool remember)
        {
            if (remember) back.Add(path);
            path = p;
            selected = -1;
            sb.Value = 0;
            Reload();
        }

        private void GoBack()
        {
            if (back.Count == 0) return;
            string p = back[back.Count - 1];
            back.RemoveAt(back.Count - 1);
            Navigate(p, false);
        }

        private static string Parent(string p)
        {
            if (p.EndsWith("\\")) p = p.Substring(0, p.Length - 1);
            if (p.Length <= 2) return "";
            int i = p.LastIndexOf('\\');
            if (i < 0) return "";
            string r = p.Substring(0, i);
            if (r.EndsWith(":")) r += "\\";
            return r;
        }

        private void RefreshDrives()
        {
            drives.Clear();
            var vols = VFSManager.GetVolumes();
            for (int i = 0; i < vols.Count; i++) drives.Add(vols[i].mFullPath);
        }

        private static long DriveSize(string d)
        {
            try { return Services.FileSystem.GetTotalSize(d); }
            catch (Exception) { return -1; }
        }

        public void Reload()
        {
            entries.Clear();
            if (!Services.FsReady)
            {
                status = "Aucun systeme de fichiers (pas de disque FAT detecte)";
            }
            else
            {
                try
                {
                    RefreshDrives();
                    if (path.Length == 0)
                    {
                        for (int i = 0; i < drives.Count; i++)
                            entries.Add(new FileEntry(drives[i], drives[i], false, true, DriveSize(drives[i])));
                    }
                    else
                    {
                        var list = VFSManager.GetDirectoryListing(path);
                        for (int i = 0; i < list.Count; i++) // dossiers d'abord
                            if (list[i].mEntryType == DirectoryEntryTypeEnum.Directory)
                                entries.Add(new FileEntry(list[i].mName, list[i].mFullPath, true, false, 0));
                        for (int i = 0; i < list.Count; i++)
                            if (list[i].mEntryType == DirectoryEntryTypeEnum.File)
                                entries.Add(new FileEntry(list[i].mName, list[i].mFullPath, false, false, list[i].mSize));
                    }
                    status = entries.Count + " element(s)";
                }
                catch (Exception e)
                {
                    status = "Erreur : " + e.Message;
                }
            }
            sb.Total = entries.Count;
            Dirty = true;
        }

        private void Activate(int i)
        {
            FileEntry e = entries[i];
            if (e.IsDrive || e.IsDir) Navigate(e.FullPath, true);
            else if (Ext(e.Name) == "FSC") { Launcher.Run(e.FullPath, out string msg); status = msg; }
            else if (Ext(e.Name) == "FSP")
            {
                Services.StudioWin.Charger(e.FullPath);
                Services.Windows.Open(Services.StudioWin);
            }
            else if (Ext(e.Name) == "PDF")
            {
                Services.PdfWin.Load(e.FullPath);
                Services.Windows.Open(Services.PdfWin);
            }
            else if (ImageLoader.IsImage(e.Name))
            {
                Services.ImageWin.Load(e.FullPath);
                Services.Windows.Open(Services.ImageWin);
            }
            else
            {
                Services.EditorWin.Load(e.FullPath);
                Services.Windows.Open(Services.EditorWin);
            }
        }

        public override void OnOpen() => Reload();

        // ---- Suppression ----
        private void AskDelete()
        {
            if (selected < 0 || selected >= entries.Count) { status = "Selectionnez d'abord un element."; Dirty = true; return; }
            if (entries[selected].IsDrive)
            {
                status = "Pour vider un disque entier, utilisez l'Installation (option effacer).";
                Dirty = true;
                return;
            }
            prompting = false;
            confirmDelete = true;
            Dirty = true;
        }

        private void DoDelete()
        {
            confirmDelete = false;
            FileEntry e = entries[selected];
            try
            {
                int n = 1;
                if (e.IsDir) n = FsTools.DeleteTree(e.FullPath);
                else FileSystemOS.Utils.FsTools.DeleteFile(e.FullPath);
                status = "Supprime : " + e.Name + (n > 1 ? " (" + n + " elements)" : "");
                selected = -1;
                Reload();
            }
            catch (Exception ex) { status = "Erreur : " + ex.Message; }
            Dirty = true;
        }

        // ---- Creation de fichiers / dossiers ----
        private void StartPrompt(bool dir)
        {
            if (path.Length == 0) { status = "Ouvrez d'abord un lecteur ou un dossier."; Dirty = true; return; }
            prompting = true;
            promptDir = dir;
            promptText = "";
            Dirty = true;
        }

        private void CreateEntry()
        {
            prompting = false;
            string name = promptText.Trim();
            if (name.Length == 0) return;
            string full = TextCodec.PathJoin(path, name);
            try
            {
                if (promptDir)
                {
                    FileSystemOS.Utils.FsTools.MakeDir(full);
                    Reload();
                    status = "Dossier cree : " + name;
                }
                else
                {
                    if (name.IndexOf('.') < 0) full += ".txt";
                    if (!File.Exists(full)) FileSystemOS.Utils.FsTools.WriteText(full, "");
                    Reload();
                    status = "Fichier cree : " + TextCodec.FileName(full);
                    Services.EditorWin.Load(full);
                    Services.Windows.Open(Services.EditorWin);
                }
            }
            catch (Exception e) { status = "Erreur : " + e.Message; }
            Dirty = true;
        }

        private static string Ext(string name)
        {
            int i = name.LastIndexOf('.');
            return i < 0 ? "" : name.Substring(i + 1).ToUpper();
        }

        public static bool IsText(string name)
        {
            string e = Ext(name);
            return e == "TXT" || e == "MD" || e == "FSL" || e == "LOG" || e == "INI" || e == "CFG" || e == "CSV" ||
                   e == "CS" || e == "JSON" || e == "XML" || e == "HTM" || e == "HTML" || e == "BAT";
        }

        private void EnsureVisible()
        {
            if (selected < sb.Value) sb.Value = selected;
            if (selected >= sb.Value + sb.Visible) sb.Value = selected - sb.Visible + 1;
            sb.Clamp();
        }

        // ---- Dessin ----
        protected override void DrawContent(PixelBuffer b, int cx, int cy, int cw, int ch)
        {
            // Barre d'outils
            b.FillRect(Theme.ToolbarBg, 0, cy, W, ToolH);
            btnBack.Draw(b, 0, cy);
            btnUp.Draw(b, 0, cy);
            btnRefresh.Draw(b, 0, cy);

            btnNewFile.Draw(b, 0, cy);
            btnNewDir.Draw(b, 0, cy);
            btnDelete.Draw(b, 0, cy);

            int px = 362, pw = W - px - 10;
            b.FillRect(Theme.PathBg, px, cy + 6, pw, 26);
            b.DrawRect(Theme.PathBorder, px, cy + 6, pw, 26);
            string shown = path.Length == 0 ? "Ce PC" : "Ce PC > " + path;
            int maxP = (pw - 16) / 8;
            if (shown.Length > maxP) shown = "..." + shown.Substring(shown.Length - maxP + 3);
            b.DrawText(shown, Theme.Text, px + 8, cy + 11);
            b.FillRect(Theme.Separator, 0, BodyY - 1, W, 1);

            // Panneau de navigation
            b.FillRect(Theme.SidebarBg, 0, BodyY, SideW, BodyH);
            b.DrawText("Navigation", Theme.TextMuted, 10, BodyY + 8);
            for (int i = 0; i <= drives.Count; i++)
            {
                int y = SideY0 + i * SideRowH;
                bool current = i == 0 ? path.Length == 0 : path.StartsWith(drives[i - 1]);
                if (current) b.FillRect(Theme.RowSelected, 4, y, SideW - 8, SideRowH - 2);
                else if (i == sideHover) b.FillRect(Theme.RowHover, 4, y, SideW - 8, SideRowH - 2);
                icoDrive.Draw(b, 10, y + 4);
                b.DrawText(i == 0 ? "Ce PC" : drives[i - 1], Theme.Text, 32, y + 4);
            }
            b.FillRect(Theme.Separator, SideW, BodyY, 1, BodyH);

            // En-tetes de colonnes
            b.FillRect(Theme.HeaderBg, ListX, BodyY, W - ListX, HeadH);
            b.DrawText("Nom", Theme.TextMuted, ListX + 28, BodyY + 3);
            b.DrawText("Type", Theme.TextMuted, TypeX, BodyY + 3);
            b.DrawText("Taille", Theme.TextMuted, SizeX, BodyY + 3);
            b.FillRect(Theme.Separator, ListX, RowsY - 1, W - ListX, 1);

            // Lignes
            sb.Visible = RowsH / RowH;
            sb.Total = entries.Count;
            sb.Clamp();
            int maxName = (TypeX - ListX - 40) / 8;
            for (int i = 0; i < sb.Visible; i++)
            {
                int idx = sb.Value + i;
                if (idx >= entries.Count) break;
                FileEntry e = entries[idx];
                int y = RowsY + i * RowH;

                if (idx == selected) b.FillRect(Theme.RowSelected, ListX, y, SbX - ListX - 2, RowH);
                else if (idx == hover) b.FillRect(Theme.RowHover, ListX, y, SbX - ListX - 2, RowH);

                string ext = Ext(e.Name);
                HexBitmap ico = e.IsDrive ? icoDrive : (e.IsDir ? icoFolder :
                               (ext == "FSL" ? icoApp : (ext == "FSC" ? icoFsc : (ImageLoader.IsImage(e.Name) ? icoImage : (ext == "PDF" ? icoPdf : (IsText(e.Name) ? icoText : icoFile))))));
                ico.Draw(b, ListX + 6, y + 3);

                string name = e.Name.Length > maxName ? e.Name.Substring(0, maxName - 2) + ".." : e.Name;
                b.DrawText(name, Theme.Text, ListX + 28, y + 3);

                string type = e.IsDrive ? "Disque" : (e.IsDir ? "Dossier" :
                              (ext == "FSL" ? "Prog. FSL" : (ext == "FSC" ? "FSL compile" : (ImageLoader.IsImage(e.Name) ? "Image " + ext : (ext == "PDF" ? "Document PDF" : "Fichier " + ext)))));
                if (type.Length > 12) type = type.Substring(0, 12);
                b.DrawText(type, Theme.TextMuted, TypeX, y + 3);

                if (!e.IsDir && e.Size >= 0) b.DrawText(Fmt.Size(e.Size), Theme.TextMuted, SizeX, y + 3);
            }
            if (entries.Count == 0 && Services.FsReady)
                b.DrawText("(vide)", Theme.TextMuted, ListX + 28, RowsY + 8);

            sb.Draw(b, SbX, RowsY, RowsH);

            // Barre d'etat
            b.FillRect(Theme.StatusBg, 0, H - StatusH, W, StatusH);
            b.FillRect(Theme.Separator, 0, H - StatusH, W, 1);
            if (confirmDelete && selected >= 0 && selected < entries.Count)
            {
                b.FillRect(Theme.RedHover, 0, H - StatusH + 1, W, StatusH - 1);
                b.DrawText("Supprimer '" + entries[selected].Name + "' ?   Entree = oui, Echap = non",
                           Theme.TitleText, 10, H - StatusH + 3);
            }
            else if (prompting)
            {
                b.FillRect(Theme.PathBg, 0, H - StatusH + 1, W, StatusH - 1);
                string q = "Nom du nouveau " + (promptDir ? "dossier" : "fichier") + " : " + promptText + "_   (Entree / Echap)";
                b.DrawText(q, Theme.Text, 10, H - StatusH + 3);
            }
            else
            {
                string st = status.Length > (W - 20) / 8 ? status.Substring(0, (W - 20) / 8) : status;
                b.DrawText(st, Theme.TextMuted, 10, H - StatusH + 3);
            }
        }

        // ---- Souris ----
        private bool Hover(Button bt, int lx, int ly, bool inside)
        {
            bool h = inside && bt.Hit(0, TitleH, lx, ly);
            if (h == bt.Hover) return false;
            bt.Hover = h;
            return true;
        }

        public override void OnMouseMove(int mx, int my)
        {
            bool inside = mx >= 0;
            int lx = mx - X, ly = my - Y;
            int h = inside ? RowAt(lx, ly) : -1;
            int sh = inside ? SideAt(lx, ly) : -1;
            bool changed = Hover(btnBack, lx, ly, inside) | Hover(btnUp, lx, ly, inside) | Hover(btnRefresh, lx, ly, inside) |
                           Hover(btnNewFile, lx, ly, inside) | Hover(btnNewDir, lx, ly, inside) |
                           Hover(btnDelete, lx, ly, inside);
            if (h != hover || sh != sideHover || changed)
            {
                hover = h;
                sideHover = sh;
                Dirty = true;
            }
        }

        public override void OnMouseDown(int mx, int my)
        {
            int lx = mx - X, ly = my - Y;

            if (sb.MouseDown(lx, ly, SbX, RowsY, RowsH)) return;

            btnBack.Pressed = btnBack.Hit(0, TitleH, lx, ly);
            btnUp.Pressed = btnUp.Hit(0, TitleH, lx, ly);
            btnRefresh.Pressed = btnRefresh.Hit(0, TitleH, lx, ly);
            btnNewFile.Pressed = btnNewFile.Hit(0, TitleH, lx, ly);
            btnNewDir.Pressed = btnNewDir.Hit(0, TitleH, lx, ly);
            btnDelete.Pressed = btnDelete.Hit(0, TitleH, lx, ly);

            int s = SideAt(lx, ly);
            if (s >= 0)
            {
                Navigate(s == 0 ? "" : drives[s - 1], true);
                return;
            }

            int r = RowAt(lx, ly);
            if (r < 0) return;
            uint now = Services.Scheduler.TotalTicks;
            if (r == lastClickIndex && now - lastClickTick < 60)   // double-clic
            {
                lastClickIndex = -1;
                Activate(r);
            }
            else
            {
                selected = r;
                lastClickIndex = r;
                lastClickTick = now;
            }
        }

        public override void OnMouseDrag(int mx, int my)
        {
            sb.MouseDrag(my - Y, RowsY, RowsH);
        }

        public override void OnMouseUp(int mx, int my)
        {
            int lx = mx - X, ly = my - Y;
            if (btnBack.Pressed && btnBack.Hit(0, TitleH, lx, ly)) GoBack();
            if (btnUp.Pressed && btnUp.Hit(0, TitleH, lx, ly) && path.Length > 0) Navigate(Parent(path), true);
            if (btnRefresh.Pressed && btnRefresh.Hit(0, TitleH, lx, ly)) Reload();
            if (btnNewFile.Pressed && btnNewFile.Hit(0, TitleH, lx, ly)) StartPrompt(false);
            if (btnNewDir.Pressed && btnNewDir.Hit(0, TitleH, lx, ly)) StartPrompt(true);
            if (btnDelete.Pressed && btnDelete.Hit(0, TitleH, lx, ly)) AskDelete();
            btnBack.Pressed = btnUp.Pressed = btnRefresh.Pressed = btnNewFile.Pressed = btnNewDir.Pressed = false;
            btnDelete.Pressed = false;
            sb.MouseUp();
        }

        // ---- Clavier ----
        public override void OnKey(KeyEvent k)
        {
            if (confirmDelete)
            {
                if (k.Key == ConsoleKeyEx.Enter) DoDelete();
                else if (k.Key == ConsoleKeyEx.Escape) { confirmDelete = false; status = "Suppression annulee"; }
                return;
            }
            if (k.Key == ConsoleKeyEx.Delete) { AskDelete(); return; }

            if (prompting)
            {
                if (k.Key == ConsoleKeyEx.Escape) prompting = false;
                else if (k.Key == ConsoleKeyEx.Enter) CreateEntry();
                else if (k.Key == ConsoleKeyEx.Backspace)
                {
                    if (promptText.Length > 0) promptText = promptText.Substring(0, promptText.Length - 1);
                }
                else
                {
                    char c = k.KeyChar;
                    if (c >= ' ' && c <= '~' && "\\/:*?\"<>|".IndexOf(c) < 0 && promptText.Length < 40) promptText += c;
                }
                return;
            }

            switch (k.Key)
            {
                case ConsoleKeyEx.UpArrow:
                    if (selected > 0) { selected--; EnsureVisible(); }
                    break;
                case ConsoleKeyEx.DownArrow:
                    if (selected < entries.Count - 1) { selected++; EnsureVisible(); }
                    break;
                case ConsoleKeyEx.Enter:
                    if (selected >= 0) Activate(selected);
                    break;
                case ConsoleKeyEx.Backspace:
                    if (path.Length > 0) Navigate(Parent(path), true);
                    break;
                case ConsoleKeyEx.F5:
                    Reload();
                    break;
            }
        }
    }
}
