using System;
using System.Collections.Generic;
using System.IO;
using Cosmos.System;
using Cosmos.System.FileSystem.Listing;
using Cosmos.System.FileSystem.VFS;
using FileSystemOS.Systeme;
using FileSystemOS.Utils;

namespace FileSystemOS.Graphique
{
    /// <summary>
    /// CORBEILLE : les elements supprimes du bureau y attendent. On peut les RESTAURER sur le
    /// bureau, les supprimer definitivement un par un, ou vider toute la corbeille.
    /// </summary>
    public class CorbeilleWindow : Window
    {
        private const int ToolH = 36, RowH = 22, StatusH = 22;
        private readonly Button btnRestaurer, btnSupprimer, btnVider;
        private readonly ScrollBar sb = new ScrollBar();
        private readonly List<string> noms = new List<string>();
        private readonly List<bool> dossiers = new List<bool>();
        private readonly List<long> tailles = new List<long>();
        private int selection = -1, survol = -1;
        private string status = "";

        public CorbeilleWindow(int x, int y) : base("Corbeille", x, y, 560, 380, Icons.TrashIcon)
        {
            btnRestaurer = new Button(Glyphs.Back, "Restaurer", 8, 5, 130, 26, Theme.Blue, Theme.BlueHover);
            btnSupprimer = new Button(Glyphs.Trash, "Supprimer", 144, 5, 130, 26, Theme.Red, Theme.RedHover);
            btnVider = new Button(Glyphs.Trash, "Vider la corbeille", 280, 5, 190, 26, Theme.Red, Theme.RedHover);
        }

        private int ListY => TitleH + ToolH;
        private int ListH => H - ListY - StatusH;
        private int SbX => W - ScrollBar.Width - 4;

        public override void OnOpen() { Recharger(); status = "Selectionnez un element."; }

        private void Recharger()
        {
            noms.Clear(); dossiers.Clear(); tailles.Clear();
            string c = Bureau.DossierCorbeille();
            try
            {
                if (c != null && Directory.Exists(c))
                {
                    var l = VFSManager.GetDirectoryListing(c);
                    for (int i = 0; i < l.Count; i++)
                    {
                        noms.Add(l[i].mName);
                        dossiers.Add(l[i].mEntryType == DirectoryEntryTypeEnum.Directory);
                        tailles.Add(l[i].mSize);
                    }
                }
            }
            catch (Exception e) { status = "Erreur : " + e.Message; }
            if (selection >= noms.Count) selection = -1;
            sb.Total = noms.Count;
            sb.Visible = ListH / RowH;
            sb.Clamp();
            Bureau.Rafraichir();
            Dirty = true;
        }

        protected override void DrawContent(PixelBuffer b, int cx, int cy, int cw, int ch)
        {
            b.FillRect(Theme.ToolbarBg, 0, cy, W, ToolH);
            btnRestaurer.Draw(b, 0, cy);
            btnSupprimer.Draw(b, 0, cy);
            btnVider.Draw(b, 0, cy);

            int y0 = cy + ToolH;
            if (noms.Count == 0)
                b.DrawText("La corbeille est vide.", Theme.TextMuted, 20, y0 + 20);
            for (int i = 0; i < sb.Visible; i++)
            {
                int idx = sb.Value + i;
                if (idx >= noms.Count) break;
                int y = y0 + 4 + i * RowH;
                if (idx == selection) b.FillRect(Theme.RowSelected, 4, y, SbX - 8, RowH - 2);
                else if (idx == survol) b.FillRect(Theme.RowHover, 4, y, SbX - 8, RowH - 2);
                b.DrawText((dossiers[idx] ? "[dossier] " : "") + noms[idx], Theme.Text, 12, y + 3);
                if (!dossiers[idx])
                {
                    string t = Fmt.Size(tailles[idx]);
                    b.DrawText(t, Theme.TextMuted, SbX - 12 - t.Length * 8, y + 3);
                }
            }
            sb.Draw(b, SbX, y0 + 4, ListH - 4);

            b.FillRect(Theme.StatusBg, 0, H - StatusH, W, StatusH);
            string s = status.Length > (W - 20) / 8 ? status.Substring(0, (W - 20) / 8) : status;
            b.DrawText(s, status.StartsWith("Erreur") ? Theme.Red : Theme.TextMuted, 10, H - StatusH + 3);
        }

        // ---- Actions ----
        private string Choisi => selection >= 0 && selection < noms.Count ? noms[selection] : null;

        private void Restaurer()
        {
            string n = Choisi;
            if (n == null) { status = "Selectionnez d'abord un element."; return; }
            string src = Bureau.DossierCorbeille() + "\\" + n;
            string bureau = Bureau.DossierBureau();
            string dest = bureau + "\\" + n;
            int k = 2;
            while (File.Exists(dest) || Directory.Exists(dest))
                dest = bureau + "\\" + Bureau.SansExtension(n) + " (" + (k++) + ")" + Bureau.Extension(n);
            try
            {
                Bureau.Deplacer(src, dest, dossiers[selection]);
                Live.SaveIfActive();
                status = "'" + n + "' restaure sur le bureau.";
                Bureau.Charger();
            }
            catch (Exception e) { status = "Erreur : " + e.Message; }
            selection = -1;
            Recharger();
        }

        private void SupprimerDefinitivement()
        {
            string n = Choisi;
            if (n == null) { status = "Selectionnez d'abord un element."; return; }
            try
            {
                string p = Bureau.DossierCorbeille() + "\\" + n;
                if (dossiers[selection]) FsTools.DeleteTree(p); else FileSystemOS.Utils.FsTools.DeleteFile(p);
                Live.SaveIfActive();
                status = "'" + n + "' supprime definitivement.";
            }
            catch (Exception e) { status = "Erreur : " + e.Message; }
            selection = -1;
            Recharger();
        }

        /// <summary>Supprime definitivement tout le contenu de la corbeille.</summary>
        public void Vider()
        {
            int n = 0;
            string c = Bureau.DossierCorbeille();
            try
            {
                var l = VFSManager.GetDirectoryListing(c);
                for (int i = 0; i < l.Count; i++)
                {
                    string p = c + "\\" + l[i].mName;
                    if (l[i].mEntryType == DirectoryEntryTypeEnum.Directory) FsTools.DeleteTree(p);
                    else FileSystemOS.Utils.FsTools.DeleteFile(p);
                    n++;
                }
                Live.SaveIfActive();
                status = "Corbeille videe : " + n + " element(s) supprime(s).";
            }
            catch (Exception e) { status = "Erreur : " + e.Message; }
            selection = -1;
            Recharger();
        }

        // ---- Souris et clavier ----
        private bool Hover(Button bt, int lx, int ly)
        {
            bool h = lx >= 0 && bt.Hit(0, 0, lx, ly);
            if (h == bt.Hover) return false;
            bt.Hover = h;
            return true;
        }

        private int LigneA(int my)
        {
            int ly = my - Y - ListY - 4;
            if (ly < 0 || ly >= ListH - 4) return -1;
            int i = sb.Value + ly / RowH;
            return i < noms.Count ? i : -1;
        }

        public override void OnMouseMove(int mx, int my)
        {
            int lx = mx < 0 ? -1 : mx - X, ly = my - Y - TitleH;
            bool c = Hover(btnRestaurer, lx, ly) | Hover(btnSupprimer, lx, ly) | Hover(btnVider, lx, ly);
            int s = mx < 0 || mx - X >= SbX ? -1 : LigneA(my);
            if (s != survol || c) { survol = s; Dirty = true; }
        }

        public override void OnMouseDown(int mx, int my)
        {
            int lx = mx - X, ly = my - Y - TitleH;
            if (sb.MouseDown(lx, my - Y, SbX, ListY + 4, ListH - 4)) return;
            btnRestaurer.Pressed = btnRestaurer.Hit(0, 0, lx, ly);
            btnSupprimer.Pressed = btnSupprimer.Hit(0, 0, lx, ly);
            btnVider.Pressed = btnVider.Hit(0, 0, lx, ly);
            if (lx < SbX)
            {
                int s = LigneA(my);
                if (s >= 0) { selection = s; status = "'" + noms[s] + "' selectionne."; }
            }
        }

        public override void OnMouseDrag(int mx, int my) => sb.MouseDrag(my - Y, ListY + 4, ListH - 4);

        public override void OnMouseUp(int mx, int my)
        {
            int lx = mx - X, ly = my - Y - TitleH;
            if (btnRestaurer.Pressed && btnRestaurer.Hit(0, 0, lx, ly)) Restaurer();
            if (btnSupprimer.Pressed && btnSupprimer.Hit(0, 0, lx, ly)) SupprimerDefinitivement();
            if (btnVider.Pressed && btnVider.Hit(0, 0, lx, ly)) Vider();
            btnRestaurer.Pressed = btnSupprimer.Pressed = btnVider.Pressed = false;
            sb.MouseUp();
        }

        public override void OnKey(KeyEvent k)
        {
            if (k.Key == ConsoleKeyEx.UpArrow && selection > 0) selection--;
            else if (k.Key == ConsoleKeyEx.DownArrow && selection < noms.Count - 1) selection++;
            else if (k.Key == ConsoleKeyEx.Delete) SupprimerDefinitivement();
            else if (k.Key == ConsoleKeyEx.F5) Recharger();
        }
    }
}
