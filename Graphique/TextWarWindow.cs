using System;
using System.Collections.Generic;
using System.IO;
using Cosmos.System;
using FileSystemOS.Systeme;
using FileSystemOS.Utils;

namespace FileSystemOS.Graphique
{
    /// <summary>
    /// TextWar : telechargeur de DOCUMENTS TEXTE.
    /// Donne une adresse (ou une recherche) : la page web, le fichier .txt ou meme le PDF
    /// est converti en texte propre et enregistre en .txt, pret a ouvrir dans l'editeur.
    /// </summary>
    public class TextWarWindow : Window
    {
        private const int Cols = 78;               // largeur des textes enregistres
        private const int ListY = 132, RowH = 24, MaxRows = 8;

        private readonly TextField urlField, nameField;
        private readonly Button btnGet;
        private readonly List<string> recent = new List<string>();
        private string pending;
        private int delay, focus;
        private string status = "Adresse http, https ou mots-cles, puis Telecharger.";

        public TextWarWindow(int x, int y) : base("TextWar", x, y, 560, 400, Icons.TextWarIcon)
        {
            urlField = new TextField(110, 14, 430, false);
            urlField.Max = 300;
            nameField = new TextField(110, 46, 250, false);
            nameField.Max = 40;
            btnGet = new Button(Glyphs.Save, "Telecharger", 372, 44, 168, 28, Theme.Green, Theme.GreenHover);
        }

        public override void OnOpen() { RefreshList(); }

        private void RefreshList()
        {
            recent.Clear();
            string f = Downloads.Folder();
            if (f == null) return;
            try
            {
                var list = Cosmos.System.FileSystem.VFS.VFSManager.GetDirectoryListing(f);
                for (int i = list.Count - 1; i >= 0 && recent.Count < MaxRows; i--)
                    if (list[i].mName.ToLower().EndsWith(".txt")) recent.Add(list[i].mFullPath);
            }
            catch (Exception) { }
        }

        private void Start()
        {
            if (urlField.Text.Trim().Length == 0) { status = "Erreur : tapez une adresse ou une recherche"; return; }
            pending = Http.Normalize(urlField.Text);
            delay = 3;
            status = "Telechargement de " + pending + " ...";
        }

        public override void Tick()
        {
            if (pending == null || Hidden) return;
            if (--delay > 0) return;
            string target = pending;
            pending = null;
            try
            {
                byte[] body = Http.Get(target, out string final, out string type);
                var outLines = new List<string>();
                outLines.Add("Source : " + final);
                outLines.Add("Telecharge avec TextWar - " + Services.OsName + " " + Services.Version);
                outLines.Add("");

                string title = "";
                if (type.StartsWith("application/pdf"))
                {
                    var pages = PdfText.Extract(body, out string info);
                    for (int i = 0; i < pages.Count; i++)
                    {
                        outLines.Add("=== Page " + (i + 1) + " ===");
                        string[] ls = pages[i].Split('\n');
                        for (int k = 0; k < ls.Length; k++) Wrap(ls[k], outLines);
                    }
                }
                else if (type.StartsWith("text/plain"))
                {
                    string[] ls = Html.Decode(body).Split('\n');
                    for (int k = 0; k < ls.Length; k++) Wrap(ls[k].Replace("\r", ""), outLines);
                }
                else if (type.StartsWith("text/html") || type.Length == 0)
                {
                    var links = new List<string>();
                    var blocks = Html.Parse(Html.Decode(body), final, links, out title);
                    var lines = Html.Layout(blocks, Cols);
                    for (int i = 0; i < lines.Count; i++) outLines.Add(lines[i].Text);
                }
                else throw new Exception("ce n'est pas un document texte (" + type + ")");

                string name = nameField.Text.Trim();
                if (name.Length == 0) name = title.Length > 0 ? Clean(title) : Downloads.NameFromUrl(final, "text/plain");
                int dot = name.LastIndexOf('.');
                if (dot > 0) name = name.Substring(0, dot);
                name += ".txt";

                string saved = Downloads.Save(Downloads.Folder(), name, TextCodec.Encode(outLines));
                status = "Enregistre : " + saved + " (" + outLines.Count + " lignes)";
                nameField.Text = "";
                RefreshList();
            }
            catch (Exception e) { status = "Erreur : " + e.Message; }
            Dirty = true;
        }

        private static void Wrap(string l, List<string> o)
        {
            while (l.Length > Cols)
            {
                int cut = l.LastIndexOf(' ', Cols);
                if (cut <= 0) cut = Cols;
                o.Add(l.Substring(0, cut));
                l = l.Substring(cut).TrimStart();
            }
            o.Add(l);
        }

        private static string Clean(string t)
        {
            string r = "";
            for (int i = 0; i < t.Length && r.Length < 36; i++)
            {
                char c = t[i];
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_') r += c;
                else if (c == ' ' && r.Length > 0 && !r.EndsWith("_")) r += '_';
            }
            return r.Length == 0 ? "document" : r;
        }

        protected override void DrawContent(PixelBuffer b, int cx, int cy, int cw, int ch)
        {
            b.DrawText("Adresse", Theme.TextMuted, 40, cy + 18);
            b.DrawText("Nom (opt.)", Theme.TextMuted, 16, cy + 50);
            Field(b, urlField, focus == 0, cy);
            Field(b, nameField, focus == 1, cy);
            btnGet.Draw(b, 0, cy);

            b.DrawText("Ex : frogfind.com/?q=histoire   ou   http://site/doc.txt   ou   https://...pdf", Theme.TextMuted, 16, cy + 84);
            b.FillRect(Theme.Separator, 16, cy + 104, W - 32, 1);
            b.DrawText("Documents texte telecharges (clic = ouvrir) :", Theme.Text, 16, cy + 110);
            for (int i = 0; i < recent.Count; i++)
            {
                int y = cy + ListY + i * RowH;
                IconSetText.Draw(b, 18, y + 2);
                b.DrawText(TextCodec.FileName(recent[i]), Theme.Blue, 42, y + 3);
            }
            if (recent.Count == 0) b.DrawText("(aucun pour l'instant)", Theme.TextMuted, 42, cy + ListY + 3);

            string st = status.Length > (W - 30) / 8 ? status.Substring(0, (W - 30) / 8) : status;
            b.DrawText(st, st.StartsWith("Erreur") ? Theme.Red : Theme.TextMuted, 16, H - 26);
        }

        private static readonly HexBitmap IconSetText = new HexBitmap(Icons.TextFile, Icons.PaletteDe(Icons.TextFile));

        private void Field(PixelBuffer b, TextField f, bool focused, int cy)
        {
            int ry = f.RY;
            f.RY = ry + cy;
            f.Draw(b, focused, true);
            f.RY = ry;
        }

        public override void OnKey(KeyEvent k)
        {
            if (k.Key == ConsoleKeyEx.Tab) { focus = 1 - focus; return; }
            if (k.Key == ConsoleKeyEx.Enter) { Start(); return; }
            (focus == 0 ? urlField : nameField).Key(k);
        }

        public override void OnMouseMove(int mx, int my)
        {
            bool h = mx >= 0 && btnGet.Hit(X, Y + TitleH, mx, my);
            if (h != btnGet.Hover) { btnGet.Hover = h; Dirty = true; }
        }

        public override void OnMouseDown(int mx, int my)
        {
            int lx = mx - X, ly = my - Y - TitleH;
            btnGet.Pressed = btnGet.Hit(0, 0, lx, ly);
            if (urlField.Hit(lx, ly)) focus = 0;
            else if (nameField.Hit(lx, ly)) focus = 1;
            else if (ly >= ListY && ly < ListY + recent.Count * RowH)
            {
                int i = (ly - ListY) / RowH;
                status = TextCodec.FileName(recent[i]) + " : " + Downloads.Open(recent[i]);
            }
        }

        public override void OnMouseUp(int mx, int my)
        {
            if (btnGet.Pressed && btnGet.Hit(X, Y + TitleH, mx, my)) Start();
            btnGet.Pressed = false;
        }
    }
}
