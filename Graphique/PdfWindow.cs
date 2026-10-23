using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Cosmos.System;
using FileSystemOS.Systeme;
using FileSystemOS.Utils;

namespace FileSystemOS.Graphique
{
    /// <summary>Lecteur PDF : affiche le texte de chaque page, avec navigation par page.</summary>
    public class PdfWindow : Window
    {
        private const int ToolH = 36, StatusH = 22, Pad = 14, LineH = 17;
        private readonly List<string> lines = new List<string>();
        private readonly List<int> pageStart = new List<int>();     // ligne de debut de chaque page
        private readonly ScrollBar sb = new ScrollBar();
        private readonly Button btnPrev, btnNext, btnSave;
        private List<string> pages = new List<string>();
        private string path = "";
        private string status = "Ouvrez un fichier .pdf depuis l'explorateur ou le navigateur.";

        public PdfWindow(int x, int y) : base("Lecteur PDF", x, y, 620, 470, Icons.PdfIcon)
        {
            btnPrev = new Button(Glyphs.Up, "Page", 8, 5, 86, 26, Theme.Blue, Theme.BlueHover);
            btnNext = new Button(Glyphs.Play, "Page", 98, 5, 86, 26, Theme.Blue, Theme.BlueHover);
            btnSave = new Button(Glyphs.Save, "Enregistrer en .txt", 190, 5, 190, 26, Theme.Green, Theme.GreenHover);
        }

        protected override Color ContentBg => Theme.PaperBg;

        private int TextY => TitleH + ToolH;
        private int TextH => H - TextY - StatusH;
        private int SbX => W - ScrollBar.Width - 4;
        private int Cols => (SbX - Pad * 2) / 8;

        public void Load(string p)
        {
            path = p;
            lines.Clear(); pageStart.Clear();
            try
            {
                pages = PdfText.Extract(FsTools.ReadBytes(p), out string info);
                status = TextCodec.FileName(p) + " - " + info;
                Layout();
                if (pages.Count == 0) lines.Add("Aucun texte trouve (PDF scanne, ou format non gere).");
            }
            catch (Exception e)
            {
                pages = new List<string>();
                lines.Add("Impossible de lire ce PDF : " + e.Message);
                status = "Erreur : " + e.Message;
            }
            sb.Value = 0;
            Title = "PDF - " + TextCodec.FileName(p);
            Services.Taskbar.Invalidate();
            Dirty = true;
        }

        private void Layout()
        {
            int cols = Cols;
            for (int pg = 0; pg < pages.Count; pg++)
            {
                pageStart.Add(lines.Count);
                lines.Add("=== Page " + (pg + 1) + " / " + pages.Count + " ===");
                string[] ls = pages[pg].Split('\n');
                for (int i = 0; i < ls.Length; i++)
                {
                    string l = ls[i];
                    if (l.Length == 0) continue;
                    while (l.Length > cols)
                    {
                        int cut = l.LastIndexOf(' ', cols);
                        if (cut <= 0) cut = cols;
                        lines.Add(l.Substring(0, cut));
                        l = l.Substring(cut).TrimStart();
                    }
                    lines.Add(l);
                }
                lines.Add("");
            }
        }

        private int CurrentPage()
        {
            int p = 0;
            for (int i = 0; i < pageStart.Count; i++) if (pageStart[i] <= sb.Value) p = i;
            return p;
        }

        private void GoPage(int p)
        {
            if (pageStart.Count == 0) return;
            if (p < 0) p = 0;
            if (p >= pageStart.Count) p = pageStart.Count - 1;
            sb.Value = pageStart[p];
            sb.Clamp();
        }

        private void SaveTxt()
        {
            if (pages.Count == 0) { status = "Rien a enregistrer."; return; }
            try
            {
                string all = "";
                for (int i = 0; i < pages.Count; i++) all += "=== Page " + (i + 1) + " ===\n" + pages[i] + "\n";
                byte[] data = new byte[all.Length];
                for (int i = 0; i < all.Length; i++) data[i] = all[i] < 128 ? (byte)all[i] : (byte)'?';
                string name = TextCodec.FileName(path);
                int dot = name.LastIndexOf('.');
                string saved = Downloads.Save(Downloads.Folder(), (dot > 0 ? name.Substring(0, dot) : name) + ".txt", data);
                status = "Texte enregistre : " + saved;
            }
            catch (Exception e) { status = "Erreur : " + e.Message; }
        }

        protected override void DrawContent(PixelBuffer b, int cx, int cy, int cw, int ch)
        {
            b.FillRect(Theme.ToolbarBg, 0, cy, W, ToolH);
            btnPrev.Draw(b, 0, cy);
            btnNext.Draw(b, 0, cy);
            btnSave.Draw(b, 0, cy);
            if (pageStart.Count > 0)
            {
                string pg = "Page " + (CurrentPage() + 1) + " / " + pageStart.Count;
                b.DrawText(pg, Theme.TextMuted, W - pg.Length * 8 - 14, cy + 10);
            }

            sb.Visible = TextH / LineH;
            sb.Total = lines.Count;
            sb.Clamp();
            for (int i = 0; i < sb.Visible; i++)
            {
                int idx = sb.Value + i;
                if (idx >= lines.Count) break;
                string l = lines[idx];
                if (l.Length == 0) continue;
                bool head = l.StartsWith("=== Page");
                int y = TextY + 4 + i * LineH;
                if (head) b.FillRect(Theme.HeaderBg, 0, y - 2, SbX, LineH);
                b.DrawText(l, head ? Theme.TitleActive : Theme.Text, Pad, y);
            }
            sb.Draw(b, SbX, TextY, TextH);

            b.FillRect(Theme.StatusBg, 0, H - StatusH, W, StatusH);
            string st = status.Length > (W - 20) / 8 ? status.Substring(0, (W - 20) / 8) : status;
            b.DrawText(st, st.StartsWith("Erreur") ? Theme.Red : Theme.TextMuted, 10, H - StatusH + 3);
        }

        public override void OnKey(KeyEvent k)
        {
            switch (k.Key)
            {
                case ConsoleKeyEx.UpArrow: sb.By(-1); break;
                case ConsoleKeyEx.DownArrow: sb.By(1); break;
                case ConsoleKeyEx.PageUp: GoPage(CurrentPage() - 1); break;
                case ConsoleKeyEx.PageDown: GoPage(CurrentPage() + 1); break;
                case ConsoleKeyEx.Home: sb.Value = 0; break;
                case ConsoleKeyEx.End: sb.Value = sb.Max; break;
            }
        }

        private bool HoverBtn(Button bt, int lx, int ly)
        {
            bool h = lx >= 0 && bt.Hit(0, 0, lx, ly);
            if (h == bt.Hover) return false;
            bt.Hover = h;
            return true;
        }

        public override void OnMouseMove(int mx, int my)
        {
            int lx = mx < 0 ? -1 : mx - X, ly = my - Y - TitleH;
            if (HoverBtn(btnPrev, lx, ly) | HoverBtn(btnNext, lx, ly) | HoverBtn(btnSave, lx, ly)) Dirty = true;
        }

        public override void OnMouseDown(int mx, int my)
        {
            int lx = mx - X, ly = my - Y - TitleH;
            if (sb.MouseDown(lx, my - Y, SbX, TextY, TextH)) return;
            btnPrev.Pressed = btnPrev.Hit(0, 0, lx, ly);
            btnNext.Pressed = btnNext.Hit(0, 0, lx, ly);
            btnSave.Pressed = btnSave.Hit(0, 0, lx, ly);
        }

        public override void OnMouseDrag(int mx, int my) => sb.MouseDrag(my - Y, TextY, TextH);

        public override void OnMouseUp(int mx, int my)
        {
            int lx = mx - X, ly = my - Y - TitleH;
            if (btnPrev.Pressed && btnPrev.Hit(0, 0, lx, ly)) GoPage(CurrentPage() - 1);
            if (btnNext.Pressed && btnNext.Hit(0, 0, lx, ly)) GoPage(CurrentPage() + 1);
            if (btnSave.Pressed && btnSave.Hit(0, 0, lx, ly)) SaveTxt();
            btnPrev.Pressed = btnNext.Pressed = btnSave.Pressed = false;
            sb.MouseUp();
        }
    }
}
