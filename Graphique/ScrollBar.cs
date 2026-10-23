namespace FileSystemOS.Graphique
{
    /// <summary>Barre de defilement reutilisable (coordonnees locales a la fenetre).</summary>
    public class ScrollBar
    {
        public const int Width = 12;

        public int Total, Visible, Value;
        public bool Dragging;
        private int grab;

        public int Max => Total > Visible ? Total - Visible : 0;

        public void Clamp()
        {
            if (Value > Max) Value = Max;
            if (Value < 0) Value = 0;
        }

        public void By(int d) { Value += d; Clamp(); }

        public void Thumb(int trackH, out int off, out int th)
        {
            if (Total <= Visible || Visible <= 0) { off = 0; th = trackH; return; }
            th = trackH * Visible / Total;
            if (th < 24) th = 24;
            if (th > trackH) th = trackH;
            off = (trackH - th) * Value / Max;
        }

        /// <summary>Renvoie vrai si le clic tombe sur la barre.</summary>
        public bool MouseDown(int lx, int ly, int x, int y, int h)
        {
            if (lx < x || lx >= x + Width || ly < y || ly >= y + h) return false;
            Thumb(h, out int off, out int th);
            if (ly >= y + off && ly < y + off + th) { Dragging = true; grab = ly - (y + off); }
            else By(ly < y + off ? -Visible : Visible);
            return true;
        }

        public void MouseDrag(int ly, int y, int h)
        {
            if (!Dragging) return;
            Thumb(h, out int off, out int th);
            int range = h - th;
            if (range <= 0) return;
            int rel = ly - grab - y;
            if (rel < 0) rel = 0;
            if (rel > range) rel = range;
            Value = (rel * Max + range / 2) / range;
        }

        public void MouseUp() { Dragging = false; }

        public void Draw(PixelBuffer b, int x, int y, int h)
        {
            b.FillRect(Theme.LightTrack, x, y, Width, h);
            Thumb(h, out int off, out int th);
            b.FillRect(Dragging ? Theme.LightThumbActive : Theme.LightThumb, x + 2, y + off + 2, Width - 4, th - 4);
        }
    }
}
