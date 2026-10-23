using System.Collections.Generic;
using System.Drawing;
using Cosmos.System;

namespace FileSystemOS.Graphique
{
    /// <summary>
    /// Console a ton style :
    ///  - le prompt est fixe EN HAUT ;
    ///  - les resultats s'affichent EN DESSOUS, le plus recent juste sous le prompt ;
    ///  - tout l'historique est conserve : on fait glisser la barre vers le bas
    ///    (ou fleches / PageDown) pour remonter dans tout ce qui a ete ecrit.
    /// </summary>
    public class ConsoleWindow : Window
    {
        private const int LineH = 18;
        private const int Pad = 10;
        private const int PromptH = 34;
        private const int ScrollW = 12;
        private const int MaxLines = 2000;

        private readonly List<string> lines = new List<string>(); // index 0 = le plus recent
        private string input = "";
        private int scroll;            // 0 = tout en haut
        private bool draggingThumb;
        private int grabOffset;

        public ConsoleWindow(int x, int y) : base("Console", x, y, 620, 470, Icons.ConsoleIcon)
        {
            var hello = new List<string>();
            hello.Add("Bienvenue dans " + Services.OsName + " " + Services.Version + " !");
            hello.Add("(c) " + Services.Company + " - " + Services.Author);
            hello.Add(FileSystemOS.Systeme.Config.Get("message_accueil", "Tapez 'aide' pour la liste des commandes."));
            PushBlock(hello);
        }

        protected override Color ContentBg => Theme.ConsoleBg;

        // ---- Geometrie ----
        private int OutX => X + Pad;
        private int OutY => Y + TitleH + PromptH + 8;
        private int OutH => H - TitleH - PromptH - 8 - Pad;
        private int TrackX => X + W - ScrollW - 6;
        private int TrackY => OutY;
        private int TrackH => OutH;
        private int Visible => OutH / LineH;
        private int Cols => (W - 2 * Pad - ScrollW - 12) / 8;
        private int MaxScroll { get { int m = lines.Count - Visible; return m < 0 ? 0 : m; } }

        private void Thumb(out int ty, out int th)
        {
            int total = lines.Count, vis = Visible;
            if (total <= vis) { ty = TrackY; th = TrackH; return; }
            th = TrackH * vis / total;
            if (th < 24) th = 24;
            ty = TrackY + (TrackH - th) * scroll / MaxScroll;
        }

        private void ScrollBy(int d)
        {
            scroll += d;
            if (scroll > MaxScroll) scroll = MaxScroll;
            if (scroll < 0) scroll = 0;
        }

        // ---- Historique ----
        private void Wrap(string text, List<string> dst)
        {
            int cols = Cols;
            if (text.Length == 0) { dst.Add(""); return; }
            for (int i = 0; i < text.Length; i += cols)
            {
                int n = text.Length - i < cols ? text.Length - i : cols;
                dst.Add(text.Substring(i, n));
            }
        }

        /// <summary>Ajoute un bloc (commande + resultat) tout en haut de l'historique.</summary>
        /// <summary>Affiche un message dans la console (depuis le systeme).</summary>
        public void Print(string s)
        {
            var l = new List<string>();
            l.Add(s);
            PushBlock(l);
            Dirty = true;
        }

        private void PushBlock(List<string> raw)
        {
            var wrapped = new List<string>();
            for (int i = 0; i < raw.Count; i++) Wrap(raw[i], wrapped);
            wrapped.Add("");

            for (int i = wrapped.Count - 1; i >= 0; i--) lines.Insert(0, wrapped[i]);
            while (lines.Count > MaxLines) lines.RemoveAt(lines.Count - 1);
            scroll = 0;
        }

        private void Execute()
        {
            string cmd = input.Trim();
            input = "";
            if (cmd.Length == 0) return;

            if (cmd.ToLower() == "effacer")
            {
                lines.Clear();
                scroll = 0;
                return;
            }

            var block = new List<string>();
            block.Add("> " + cmd);
            Shell.Run(cmd, block);
            PushBlock(block);
        }

        // ---- Animation : le curseur clignote -> on redessine quand la phase change ----
        private uint blinkPhase;
        private uint BlinkNow => (Services.Scheduler.TotalTicks / 20) % 2;

        public override void Tick()
        {
            uint p = BlinkNow;
            if (p != blinkPhase) { blinkPhase = p; Dirty = true; }
        }

        // ---- Dessin dans le buffer de la fenetre (coordonnees locales) ----
        protected override void DrawContent(PixelBuffer b, int cx, int cy, int cw, int ch)
        {
            // Zone du prompt (fixe, en haut)
            b.FillRect(Theme.ConsolePromptBg, cx + 1, cy, cw - 2, PromptH);
            b.DrawText(">", Theme.ConsolePrompt, cx + Pad, cy + 9);

            int maxChars = Cols - 2;
            string shown = input.Length > maxChars ? input.Substring(input.Length - maxChars) : input;
            if (shown.Length > 0) b.DrawText(shown, Theme.ConsoleInput, cx + Pad + 16, cy + 9);

            if (blinkPhase == 0)
                b.FillRect(Theme.ConsolePrompt, cx + Pad + 16 + shown.Length * 8, cy + 24, 8, 2);

            b.FillRect(Theme.TitleActive, cx + 1, cy + PromptH, cw - 2, 2);

            // Resultats (en dessous) : positions ecran -> locales
            int outX = OutX - X, outY = OutY - Y;
            int vis = Visible;
            for (int i = 0; i < vis; i++)
            {
                int idx = scroll + i;
                if (idx >= lines.Count) break;
                string l = lines[idx];
                if (l.Length == 0) continue;

                Color col = Theme.ConsoleText;
                if (l.StartsWith("> ")) col = Theme.ConsoleEcho;
                else if (l.StartsWith("Erreur")) col = Theme.ConsoleError;

                b.DrawText(l, col, outX, outY + i * LineH);
            }

            // Barre de defilement
            int tx = TrackX - X, tyTrack = TrackY - Y;
            b.FillRect(Theme.ScrollTrack, tx, tyTrack, ScrollW, TrackH);
            Thumb(out int ty, out int th);
            b.FillRect(draggingThumb ? Theme.ScrollThumbActive : Theme.ScrollThumb,
                       tx + 2, ty - Y + 2, ScrollW - 4, th - 4);
        }

        // ---- Clavier ----
        public override void OnKey(KeyEvent k)
        {
            switch (k.Key)
            {
                case ConsoleKeyEx.Enter: Execute(); break;
                case ConsoleKeyEx.Backspace:
                    if (input.Length > 0) input = input.Substring(0, input.Length - 1);
                    break;
                case ConsoleKeyEx.UpArrow: ScrollBy(-1); break;
                case ConsoleKeyEx.DownArrow: ScrollBy(1); break;
                case ConsoleKeyEx.PageUp: ScrollBy(-Visible); break;
                case ConsoleKeyEx.PageDown: ScrollBy(Visible); break;
                default:
                    char ch = k.KeyChar;
                    if (ch >= ' ' && ch <= '~' && input.Length < 200) input += ch;
                    break;
            }
        }

        // ---- Souris (barre de defilement) ----
        public override void OnMouseDown(int mx, int my)
        {
            if (mx < TrackX || mx >= TrackX + ScrollW || my < TrackY || my >= TrackY + TrackH) return;

            Thumb(out int ty, out int th);
            if (my >= ty && my < ty + th)
            {
                draggingThumb = true;
                grabOffset = my - ty;
            }
            else
            {
                ScrollBy(my < ty ? -Visible : Visible); // clic sur la piste = page
            }
        }

        public override void OnMouseDrag(int mx, int my)
        {
            if (!draggingThumb) return;
            Thumb(out int ty, out int th);
            int range = TrackH - th;
            if (range <= 0) return;

            int rel = my - grabOffset - TrackY;
            if (rel < 0) rel = 0;
            if (rel > range) rel = range;
            scroll = (rel * MaxScroll + range / 2) / range;
        }

        public override void OnMouseUp(int mx, int my)
        {
            draggingThumb = false;
        }
    }
}
