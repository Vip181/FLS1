using System;
using System.Collections.Generic;
using System.Drawing;
using Cosmos.System;
using FileSystemOS.Systeme;

namespace FileSystemOS.Graphique
{
    /// <summary>
    /// Navigateur internet (texte) : barre d'adresse, precedent, actualiser, accueil,
    /// liens cliquables. Les sites https et les recherches passent par FrogFind,
    /// qui renvoie des pages simplifiees en http.
    /// </summary>
    public class BrowserWindow : Window
    {
        private const int ToolH = 36, StatusH = 22, Pad = 10, LineH = 18;

        private readonly TextField address;
        private readonly Button btnBack, btnReload, btnHome;
        private readonly ScrollBar sb = new ScrollBar();
        private readonly List<string> history = new List<string>();
        private readonly List<string> links = new List<string>();
        private List<List<WebSeg>> blocks = new List<List<WebSeg>>();
        private List<WebLine> lines = new List<WebLine>();

        private string url = "";
        private string pending;     // page a charger
        private int loadDelay;
        private string status = "Tapez une adresse ou une recherche, puis Entree.";
        private bool editing;

        public BrowserWindow(int x, int y) : base("Navigateur", x, y, 700, 470, Icons.BrowserIcon)
        {
            btnBack = new Button(Glyphs.Back, "", 8, 5, 32, 26, Theme.Blue, Theme.BlueHover);
            btnReload = new Button(Glyphs.Refresh, "", 44, 5, 32, 26, Theme.Blue, Theme.BlueHover);
            btnHome = new Button(Glyphs.Up, "", 80, 5, 32, 26, Theme.Blue, Theme.BlueHover);
            address = new TextField(120, 5, 560, false);
            address.Max = 300;
        }

        private int TextY => TitleH + ToolH;
        private int TextH => H - TextY - StatusH;
        private int SbX => W - ScrollBar.Width - 4;
        private int Cols => (SbX - Pad * 2) / 8;

        public override void OnOpen()
        {
            if (url.Length == 0 && pending == null) Navigate(Http.Home, true);
        }

        public void Navigate(string target, bool remember)
        {
            if (remember && url.Length > 0) history.Add(url);
            pending = Http.Normalize(target);
            address.Text = pending;
            loadDelay = 3;      // laisse le temps d'afficher "Chargement..." avant de bloquer
            status = "Chargement de " + pending + " ...";
            editing = false;
            Dirty = true;
        }

        public override void Tick()
        {
            if (pending == null || Hidden) return;
            if (--loadDelay > 0) return;

            string target = pending;
            pending = null;
            links.Clear();
            try
            {
                byte[] body = Http.Get(target, out string final, out string type);

                // Pas une page : c'est un TELECHARGEMENT (PDF, image, fichier...)
                if (!type.StartsWith("text/html") && !type.StartsWith("text/plain") && type.Length > 0)
                {
                    string saved = Downloads.Save(Downloads.Folder(), Downloads.NameFromUrl(final, type), body);
                    status = "Telecharge : " + saved + " - " + Downloads.Open(saved);
                    address.Text = url;
                    Services.Taskbar.Invalidate();
                    Dirty = true;
                    return;
                }
                url = final;
                address.Text = final;
                string text = Html.Decode(body);
                if (type.StartsWith("text/plain"))
                {
                    blocks = new List<List<WebSeg>>();
                    string[] ls = text.Split('\n');
                    for (int i = 0; i < ls.Length; i++)
                    {
                        var b = new List<WebSeg>();
                        var s = new WebSeg(); s.Text = ls[i].Replace("\r", "").Replace("\t", "    ");
                        b.Add(s); blocks.Add(b);
                    }
                    Title = "Navigateur";
                }
                else
                {
                    blocks = Html.Parse(text, final, links, out string title);
                    Title = title.Length > 0 ? title : "Navigateur";
                }
                lines = Html.Layout(blocks, Cols);
                sb.Value = 0;
                status = final + "  -  " + Utils.Fmt.Size(body.Length) + ", " + links.Count + " lien(s)";
            }
            catch (Exception e)
            {
                url = target;
                blocks = new List<List<WebSeg>>();
                var b = new List<WebSeg>();
                var s = new WebSeg(); s.Text = "Impossible d'ouvrir la page : " + e.Message; s.Head = true;
                b.Add(s); blocks.Add(b);
                lines = Html.Layout(blocks, Cols);
                status = "Erreur : " + e.Message;
                Title = "Navigateur";
            }
            Services.Taskbar.Invalidate();
            Dirty = true;
        }

        protected override void DrawContent(PixelBuffer b, int cx, int cy, int cw, int ch)
        {
            b.FillRect(Theme.ToolbarBg, 0, cy, W, ToolH);
            btnBack.Draw(b, 0, cy);
            btnReload.Draw(b, 0, cy);
            btnHome.Draw(b, 0, cy);
            address.W = W - address.RX - 10;
            int ry = address.RY;
            address.RY = ry + cy;
            address.Draw(b, editing, true);
            address.RY = ry;

            b.FillRect(Theme.PaperBg, 0, TextY, W, TextH);
            sb.Visible = TextH / LineH;
            sb.Total = lines.Count;
            sb.Clamp();
            for (int i = 0; i < sb.Visible; i++)
            {
                int idx = sb.Value + i;
                if (idx >= lines.Count) break;
                WebLine l = lines[idx];
                int y = TextY + 4 + i * LineH;
                for (int k = 0; k < l.Segs.Count; k++)
                {
                    WebSeg s = l.Segs[k];
                    int x = Pad + l.Starts[k] * 8;
                    Color col = s.Link >= 0 ? Theme.Blue : (s.Head ? Theme.TitleActive : Theme.Text);
                    b.DrawText(s.Text, col, x, y);
                    if (s.Link >= 0) b.FillRect(Theme.Blue, x, y + 15, s.Text.Length * 8, 1);
                }
            }
            sb.Draw(b, SbX, TextY, TextH);

            b.FillRect(Theme.StatusBg, 0, H - StatusH, W, StatusH);
            string st = status.Length > (W - 20) / 8 ? status.Substring(0, (W - 20) / 8) : status;
            b.DrawText(st, st.StartsWith("Erreur") ? Theme.Red : Theme.TextMuted, 10, H - StatusH + 3);
        }

        private int LinkAt(int lx, int ly)
        {
            if (ly < TextY || ly >= TextY + TextH || lx < Pad || lx >= SbX) return -1;
            int idx = sb.Value + (ly - TextY - 4) / LineH;
            if (idx < 0 || idx >= lines.Count) return -1;
            WebLine l = lines[idx];
            int col = (lx - Pad) / 8;
            for (int k = 0; k < l.Segs.Count; k++)
                if (col >= l.Starts[k] && col < l.Starts[k] + l.Segs[k].Text.Length) return l.Segs[k].Link;
            return -1;
        }

        public override void OnKey(KeyEvent k)
        {
            if (editing)
            {
                if (k.Key == ConsoleKeyEx.Enter) { Navigate(address.Text, true); return; }
                if (k.Key == ConsoleKeyEx.Escape) { editing = false; address.Text = url; return; }
                address.Key(k);
                return;
            }
            switch (k.Key)
            {
                case ConsoleKeyEx.UpArrow: sb.By(-1); break;
                case ConsoleKeyEx.DownArrow: sb.By(1); break;
                case ConsoleKeyEx.PageUp: sb.By(-sb.Visible); break;
                case ConsoleKeyEx.PageDown: case ConsoleKeyEx.Spacebar: sb.By(sb.Visible); break;
                case ConsoleKeyEx.Backspace: GoBack(); break;
                case ConsoleKeyEx.F5: Navigate(url.Length > 0 ? url : Http.Home, false); break;
                default:
                    char c = k.KeyChar;
                    if (c > ' ' && c <= '~') { editing = true; address.Text = c.ToString(); }
                    break;
            }
        }

        private void GoBack()
        {
            if (history.Count == 0) return;
            string p = history[history.Count - 1];
            history.RemoveAt(history.Count - 1);
            Navigate(p, false);
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
            if (HoverBtn(btnBack, lx, ly) | HoverBtn(btnReload, lx, ly) | HoverBtn(btnHome, lx, ly)) Dirty = true;
        }

        public override void OnMouseDown(int mx, int my)
        {
            int lx = mx - X, cly = my - Y - TitleH;
            if (sb.MouseDown(lx, my - Y, SbX, TextY, TextH)) return;
            btnBack.Pressed = btnBack.Hit(0, 0, lx, cly);
            btnReload.Pressed = btnReload.Hit(0, 0, lx, cly);
            btnHome.Pressed = btnHome.Hit(0, 0, lx, cly);
            if (address.Hit(lx, cly)) { editing = true; address.Text = ""; return; }

            int link = LinkAt(lx, my - Y);
            if (link >= 0 && link < links.Count) Navigate(links[link], true);
        }

        public override void OnMouseDrag(int mx, int my) => sb.MouseDrag(my - Y, TextY, TextH);

        public override void OnMouseUp(int mx, int my)
        {
            int lx = mx - X, cly = my - Y - TitleH;
            if (btnBack.Pressed && btnBack.Hit(0, 0, lx, cly)) GoBack();
            if (btnReload.Pressed && btnReload.Hit(0, 0, lx, cly)) Navigate(url.Length > 0 ? url : Http.Home, false);
            if (btnHome.Pressed && btnHome.Hit(0, 0, lx, cly)) Navigate(Http.Home, true);
            btnBack.Pressed = btnReload.Pressed = btnHome.Pressed = false;
            sb.MouseUp();
        }
    }
}
