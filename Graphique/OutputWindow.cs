using System.Collections.Generic;
using System.Drawing;
using Cosmos.System;

namespace FileSystemOS.Graphique
{
    /// <summary>Fenetre de sortie des programmes FSL (ce qu'affiche 'afficher').</summary>
    public class OutputWindow : Window
    {
        private const int Pad = 10, LineH = 17, MaxLines = 1000;
        private readonly List<string> lines = new List<string>();
        private readonly ScrollBar sb = new ScrollBar();

        public OutputWindow(int x, int y) : base("Sortie FSL", x, y, 520, 300, Icons.OutputIcon) { }

        protected override Color ContentBg => Theme.ConsoleBg;

        private int TextY => TitleH + 6;
        private int TextH => H - TextY - 6;
        private int SbX => W - ScrollBar.Width - 4;
        private int Cols => (SbX - Pad * 2) / 8;

        public void Print(string s)
        {
            int cols = Cols;
            if (s.Length == 0) lines.Add("");
            for (int i = 0; i < s.Length; i += cols)
                lines.Add(s.Substring(i, s.Length - i < cols ? s.Length - i : cols));
            while (lines.Count > MaxLines) lines.RemoveAt(0);

            sb.Visible = TextH / LineH;
            sb.Total = lines.Count;
            sb.Value = sb.Max;          // defile automatiquement vers la fin
            Dirty = true;
        }

        protected override void DrawContent(PixelBuffer b, int cx, int cy, int cw, int ch)
        {
            sb.Visible = TextH / LineH;
            sb.Total = lines.Count;
            sb.Clamp();
            for (int i = 0; i < sb.Visible; i++)
            {
                int idx = sb.Value + i;
                if (idx >= lines.Count) break;
                string l = lines[idx];
                if (l.Length == 0) continue;
                Color col = Theme.ConsoleText;
                if (l.StartsWith("==") || l.StartsWith("--")) col = Theme.ConsoleEcho;
                else if (l.StartsWith("Erreur")) col = Theme.ConsoleError;
                b.DrawText(l, col, Pad, TextY + i * LineH);
            }
            sb.Draw(b, SbX, TextY, TextH);
        }

        public override void OnKey(KeyEvent k)
        {
            if (k.Key == ConsoleKeyEx.UpArrow) sb.By(-1);
            else if (k.Key == ConsoleKeyEx.DownArrow) sb.By(1);
            else if (k.Key == ConsoleKeyEx.PageUp) sb.By(-sb.Visible);
            else if (k.Key == ConsoleKeyEx.PageDown) sb.By(sb.Visible);
        }

        public override void OnMouseDown(int mx, int my) => sb.MouseDown(mx - X, my - Y, SbX, TextY, TextH);
        public override void OnMouseDrag(int mx, int my) => sb.MouseDrag(my - Y, TextY, TextH);
        public override void OnMouseUp(int mx, int my) => sb.MouseUp();
    }
}
