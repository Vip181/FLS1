using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Cosmos.System;
using FileSystemOS.Fsl;
using FileSystemOS.Utils;

namespace FileSystemOS.Graphique
{
    /// <summary>
    /// Editeur de texte : ouvrir, modifier, enregistrer (Ctrl+S).
    /// Pour les fichiers .fsl : coloration syntaxique, Executer (F5) et Compiler (.fsc).
    /// </summary>
    public class EditorWindow : Window
    {
        private const int ToolH = 36, StatusH = 22, Gutter = 52, LineH = 17, Pad = 8;

        private readonly List<string> lines = new List<string>();
        private readonly ScrollBar sb = new ScrollBar();
        private readonly Button btnSave, btnRun, btnCompile, btnSaveAs, btnNew;
        private bool savingAs;              // saisie du chemin "Enregistrer sous"
        private string savePath = "";

        private int row, col, hx;          // curseur + defilement horizontal
        private string path = "";
        private bool modified;
        private string status = "Ouvrez ou creez un fichier depuis l'explorateur.";
        private uint blink;

        public EditorWindow(int x, int y) : base("Editeur", x, y, 600, 440, Icons.EditorIcon)
        {
            btnSave = new Button(Glyphs.Save, "Enregistrer", 8, 5, 126, 26, Theme.Blue, Theme.BlueHover);
            btnRun = new Button(Glyphs.Play, "Executer", 140, 5, 104, 26, Theme.Green, Theme.GreenHover);
            btnCompile = new Button(Glyphs.Braces, "Compiler", 250, 5, 104, 26, Theme.Blue, Theme.BlueHover);
            btnSaveAs = new Button(Glyphs.Save, "Sous", 360, 5, 82, 26, Theme.Blue, Theme.BlueHover);
            btnNew = new Button(Glyphs.Plus, "", 448, 5, 32, 26, Theme.Green, Theme.GreenHover);
            lines.Add("");
        }

        protected override Color ContentBg => Theme.PaperBg;

        private bool IsFsl => path.ToLower().EndsWith(".fsl");
        private int TextY => TitleH + ToolH;
        private int TextH => H - TextY - StatusH;
        private int SbX => W - ScrollBar.Width - 2;
        private int VisRows => TextH / LineH;
        private int VisCols => (SbX - Gutter - Pad * 2) / 8;

        // ---- Fichiers ----
        public void Load(string p)
        {
            path = p;
            lines.Clear();
            try
            {
                lines.AddRange(TextCodec.ReadLines(p));
                status = lines.Count + " ligne(s)";
            }
            catch (Exception e) { status = "Erreur : " + e.Message; }
            if (lines.Count == 0) lines.Add("");
            row = col = hx = 0;
            sb.Value = 0;
            modified = false;
            UpdateTitle();
            Dirty = true;
        }

        private void UpdateTitle()
        {
            Title = "Editeur - " + (path.Length == 0 ? "sans nom" : TextCodec.FileName(path)) + (modified ? " *" : "");
            Services.Taskbar.Invalidate();
            Dirty = true;
        }

        /// <summary>Dossier Documents de l'utilisateur (cree si besoin).</summary>
        private static string DefaultFolder()
        {
            string home = FileSystemOS.Systeme.Comptes.Home();
            string root = InstallerWindow.FindRoot();
            string d = home != null ? home + "\\Documents" : (root != null ? root + "\\Documents" : "0:\\");
            try { FileSystemOS.Utils.FsTools.MakeDir(d); } catch (Exception) { }
            return d;
        }

        /// <summary>
        /// "Enregistrer sous" : on tape le chemin complet, sur N'IMPORTE QUEL disque
        /// (0:\ disque dur, 1:\ deuxieme disque ou cle virtuelle...). Les dossiers manquants sont crees.
        /// </summary>
        private void StartSaveAs()
        {
            savingAs = true;
            savePath = path.Length > 0 ? path : TextCodec.PathJoin(DefaultFolder(), "nouveau.txt");
            Dirty = true;
        }

        private void FinishSaveAs()
        {
            savingAs = false;
            string p = savePath.Trim();
            if (p.Length < 4 || p.IndexOf(":\\") != 1) { status = "Chemin invalide (exemple : 0:\\Documents\\notes.txt)"; return; }
            try
            {
                int cut = p.LastIndexOf('\\');
                string dir = cut > 2 ? p.Substring(0, cut) : p.Substring(0, 3);
                FileSystemOS.Utils.FsTools.MakeDir(dir);
            }
            catch (Exception e) { status = "Erreur : " + e.Message; return; }
            path = p;
            Save();
        }

        private void NewDocument()
        {
            lines.Clear();
            lines.Add("");
            path = "";
            row = col = hx = 0;
            sb.Value = 0;
            modified = false;
            status = "Nouveau document : Ctrl+S pour choisir ou l'enregistrer.";
            UpdateTitle();
        }

        private bool Save()
        {
            if (path.Length == 0) { StartSaveAs(); return false; }
            try
            {
                FileSystemOS.Utils.FsTools.WriteBytes(path, TextCodec.Encode(lines));
                modified = false;
                status = "Enregistre : " + path;
                UpdateTitle();
                return true;
            }
            catch (Exception e) { status = "Erreur : " + e.Message; return false; }
        }

        private void RunProgram()
        {
            if (!IsFsl) { status = "Seuls les fichiers .fsl peuvent etre executes."; return; }
            if (modified && !Save()) return;
            Launcher.Run(path, out string msg);
            status = msg;
        }

        private void CompileProgram()
        {
            if (!IsFsl) { status = "Seuls les fichiers .fsl peuvent etre compiles."; return; }
            if (modified && !Save()) return;
            try { status = "Compile : " + Launcher.CompileToFile(path); }
            catch (Exception e) { status = "Erreur de compilation : " + e.Message; }
        }

        // ---- Edition ----
        private void Changed()
        {
            if (!modified) { modified = true; UpdateTitle(); }
        }

        private void Insert(string s)
        {
            string l = lines[row];
            lines[row] = l.Substring(0, col) + s + l.Substring(col);
            col += s.Length;
            Changed();
        }

        private void Backspace()
        {
            if (col > 0)
            {
                string l = lines[row];
                lines[row] = l.Substring(0, col - 1) + l.Substring(col);
                col--;
            }
            else if (row > 0)
            {
                col = lines[row - 1].Length;
                lines[row - 1] += lines[row];
                lines.RemoveAt(row);
                row--;
            }
            else return;
            Changed();
        }

        private void Delete()
        {
            string l = lines[row];
            if (col < l.Length) lines[row] = l.Substring(0, col) + l.Substring(col + 1);
            else if (row < lines.Count - 1) { lines[row] = l + lines[row + 1]; lines.RemoveAt(row + 1); }
            else return;
            Changed();
        }

        private void NewLine()
        {
            string l = lines[row];
            int indent = 0;
            while (indent < l.Length && indent < col && l[indent] == ' ') indent++; // garde l'indentation
            lines[row] = l.Substring(0, col);
            lines.Insert(row + 1, new string(' ', indent) + l.Substring(col));
            row++;
            col = indent;
            Changed();
        }

        private void EnsureCursor()
        {
            if (row < 0) row = 0;
            if (row >= lines.Count) row = lines.Count - 1;
            if (col > lines[row].Length) col = lines[row].Length;
            if (col < 0) col = 0;

            sb.Visible = VisRows;
            sb.Total = lines.Count;
            if (row < sb.Value) sb.Value = row;
            if (row >= sb.Value + VisRows) sb.Value = row - VisRows + 1;
            sb.Clamp();

            if (col < hx) hx = col;
            if (col >= hx + VisCols) hx = col - VisCols + 1;
        }

        // ---- Animation ----
        public override void Tick()
        {
            uint p = (Services.Scheduler.TotalTicks / 20) % 2;
            if (p != blink) { blink = p; Dirty = true; }
        }

        // ---- Dessin ----
        protected override void DrawContent(PixelBuffer b, int cx, int cy, int cw, int ch)
        {
            b.FillRect(Theme.ToolbarBg, 0, cy, W, ToolH);
            btnSave.Draw(b, 0, cy);
            btnRun.Draw(b, 0, cy);
            btnCompile.Draw(b, 0, cy);
            btnSaveAs.Draw(b, 0, cy);
            btnNew.Draw(b, 0, cy);
            string lang = IsFsl ? "File Syst Langage" : "Texte";
            b.DrawText(lang, Theme.TextMuted, W - lang.Length * 8 - 14, cy + 10);
            b.FillRect(Theme.Separator, 0, TextY - 1, W, 1);

            b.FillRect(Theme.GutterBg, 1, TextY, Gutter - 1, TextH);

            sb.Visible = VisRows;
            sb.Total = lines.Count;
            sb.Clamp();
            int cols = VisCols;
            for (int i = 0; i < VisRows; i++)
            {
                int idx = sb.Value + i;
                if (idx >= lines.Count) break;
                int y = TextY + 2 + i * LineH;

                string num = (idx + 1).ToString();
                b.DrawText(num, Theme.TextMuted, Gutter - 8 - num.Length * 8, y);

                string l = lines[idx];
                if (l.Length <= hx) continue;
                string part = l.Substring(hx, l.Length - hx < cols ? l.Length - hx : cols);
                if (IsFsl) DrawCode(b, part, Gutter + Pad, y);
                else b.DrawText(part, Theme.Text, Gutter + Pad, y);
            }

            // Curseur clignotant
            if (blink == 0 && row >= sb.Value && row < sb.Value + VisRows)
                b.FillRect(Theme.Text, Gutter + Pad + (col - hx) * 8, TextY + 2 + (row - sb.Value) * LineH, 2, 16);

            sb.Draw(b, SbX, TextY, TextH);

            // Barre d'etat
            b.FillRect(Theme.StatusBg, 0, H - StatusH, W, StatusH);
            b.FillRect(Theme.Separator, 0, H - StatusH, W, 1);
            string pos = "Ln " + (row + 1) + ", Col " + (col + 1);
            int maxS = (W - pos.Length * 8 - 40) / 8;
            if (savingAs)
            {
                b.FillRect(Theme.PathBg, 0, H - StatusH + 1, W, StatusH - 1);
                string q = "Enregistrer sous : " + savePath + "_";
                if (q.Length > (W - 20) / 8) q = "..." + q.Substring(q.Length - (W - 20) / 8 + 3);
                b.DrawText(q, Theme.Text, 10, H - StatusH + 3);
                return;
            }
            string st = status.Length > maxS ? status.Substring(0, maxS) : status;
            b.DrawText(st, Theme.TextMuted, 10, H - StatusH + 3);
            b.DrawText(pos, Theme.TextMuted, W - pos.Length * 8 - 12, H - StatusH + 3);
        }

        /// <summary>Coloration syntaxique d'une ligne FSL.</summary>
        private static void DrawCode(PixelBuffer b, string s, int x, int y)
        {
            int i = 0;
            while (i < s.Length)
            {
                int start = i;
                char c = s[i];
                Color col = Theme.Text;

                if (c == '/' && i + 1 < s.Length && s[i + 1] == '/') { i = s.Length; col = Theme.CodeComment; }
                else if (c == '"')
                {
                    i++;
                    while (i < s.Length && s[i] != '"') i++;
                    if (i < s.Length) i++;
                    col = Theme.CodeString;
                }
                else if (Lexer.IsNullAt(s, i)) { i += 5; col = Theme.CodeNull; }
                else if (Lexer.IsDigit(c))
                {
                    while (i < s.Length && Lexer.IsDigit(s[i])) i++;
                    col = Theme.CodeNumber;
                }
                else if (Lexer.IsLetter(c))
                {
                    while (i < s.Length && (Lexer.IsLetter(s[i]) || Lexer.IsDigit(s[i]))) i++;
                    if (Lexer.IsKeyword(s.Substring(start, i - start))) col = Theme.CodeKeyword;
                }
                else i++;

                b.DrawText(s.Substring(start, i - start), col, x + start * 8, y);
            }
        }

        // ---- Clavier ----
        public override void OnKey(KeyEvent k)
        {
            if (savingAs)
            {
                if (k.Key == ConsoleKeyEx.Enter) FinishSaveAs();
                else if (k.Key == ConsoleKeyEx.Escape) { savingAs = false; status = "Enregistrement annule"; }
                else if (k.Key == ConsoleKeyEx.Backspace) { if (savePath.Length > 0) savePath = savePath.Substring(0, savePath.Length - 1); }
                else if (k.KeyChar >= ' ' && k.KeyChar <= '~' && savePath.Length < 120) savePath += k.KeyChar;
                return;
            }
            bool ctrl = (k.Modifiers & ConsoleModifiers.Control) == ConsoleModifiers.Control;
            if (ctrl && k.Key == ConsoleKeyEx.S) { Save(); return; }
            if (ctrl && k.Key == ConsoleKeyEx.N) { NewDocument(); return; }

            switch (k.Key)
            {
                case ConsoleKeyEx.F5: RunProgram(); break;
                case ConsoleKeyEx.Enter: NewLine(); break;
                case ConsoleKeyEx.Backspace: Backspace(); break;
                case ConsoleKeyEx.Delete: Delete(); break;
                case ConsoleKeyEx.Tab: Insert("    "); break;
                case ConsoleKeyEx.LeftArrow:
                    if (col > 0) col--;
                    else if (row > 0) { row--; col = lines[row].Length; }
                    break;
                case ConsoleKeyEx.RightArrow:
                    if (col < lines[row].Length) col++;
                    else if (row < lines.Count - 1) { row++; col = 0; }
                    break;
                case ConsoleKeyEx.UpArrow: row--; break;
                case ConsoleKeyEx.DownArrow: row++; break;
                case ConsoleKeyEx.PageUp: row -= VisRows; break;
                case ConsoleKeyEx.PageDown: row += VisRows; break;
                case ConsoleKeyEx.Home: col = 0; break;
                case ConsoleKeyEx.End: col = lines[row].Length; break;
                default:
                    char c = k.KeyChar;
                    if (c >= ' ' && c <= '~') Insert(c.ToString());
                    break;
            }
            EnsureCursor();
        }

        // ---- Souris ----
        private bool HoverBtn(Button bt, int lx, int ly, bool inside)
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
            if (HoverBtn(btnSave, lx, ly, inside) | HoverBtn(btnRun, lx, ly, inside) | HoverBtn(btnCompile, lx, ly, inside) |
                HoverBtn(btnSaveAs, lx, ly, inside) | HoverBtn(btnNew, lx, ly, inside))
                Dirty = true;
        }

        public override void OnMouseDown(int mx, int my)
        {
            int lx = mx - X, ly = my - Y;
            if (sb.MouseDown(lx, ly, SbX, TextY, TextH)) return;

            btnSave.Pressed = btnSave.Hit(0, TitleH, lx, ly);
            btnRun.Pressed = btnRun.Hit(0, TitleH, lx, ly);
            btnCompile.Pressed = btnCompile.Hit(0, TitleH, lx, ly);
            btnSaveAs.Pressed = btnSaveAs.Hit(0, TitleH, lx, ly);
            btnNew.Pressed = btnNew.Hit(0, TitleH, lx, ly);

            if (ly >= TextY && ly < TextY + TextH && lx >= Gutter && lx < SbX)
            {
                row = sb.Value + (ly - TextY - 2) / LineH;
                col = hx + (lx - Gutter - Pad + 4) / 8;
                EnsureCursor();
            }
        }

        public override void OnMouseDrag(int mx, int my) => sb.MouseDrag(my - Y, TextY, TextH);

        public override void OnMouseUp(int mx, int my)
        {
            int lx = mx - X, ly = my - Y;
            if (btnSave.Pressed && btnSave.Hit(0, TitleH, lx, ly)) Save();
            if (btnRun.Pressed && btnRun.Hit(0, TitleH, lx, ly)) RunProgram();
            if (btnCompile.Pressed && btnCompile.Hit(0, TitleH, lx, ly)) CompileProgram();
            if (btnSaveAs.Pressed && btnSaveAs.Hit(0, TitleH, lx, ly)) StartSaveAs();
            if (btnNew.Pressed && btnNew.Hit(0, TitleH, lx, ly)) NewDocument();
            btnSave.Pressed = btnRun.Pressed = btnCompile.Pressed = btnSaveAs.Pressed = btnNew.Pressed = false;
            sb.MouseUp();
        }
    }
}
