using FileSystemOS.Processus;

namespace FileSystemOS.Graphique
{
    /// <summary>
    /// Barre du haut : les applications EN ATTENTE y sont epinglees.
    /// Clic sur une puce = reprendre l'application.
    /// </summary>
    public class TopBar
    {
        public const int H = 28;
        private const int ChipX0 = 128, ChipW = 190, ChipGap = 6;

        public readonly PixelBuffer Buffer;
        public bool Dirty = true;
        private int hoverChip = -1;
        private int hoverBtn = -1;   // 0 = tout mettre en attente, 1 = tout reprendre
        private const int BtnW = 30;
        private int BtnX(int i) => Services.ScreenW - 290 + i * (BtnW + 4);

        private int BtnAt(int mx, int my)
        {
            if (my < 0 || my >= H) return -1;
            for (int i = 0; i < 2; i++) if (mx >= BtnX(i) && mx < BtnX(i) + BtnW) return i;
            return -1;
        }

        public TopBar()
        {
            Buffer = new PixelBuffer("barre d'attente", Services.ScreenW, H);
        }

        private int MaxChips => (Services.ScreenW - ChipX0 - 300) / (ChipW + ChipGap);

        private int ChipAt(int mx, int my)
        {
            if (my < 0 || my >= H || mx < ChipX0) return -1;
            int idx = (mx - ChipX0) / (ChipW + ChipGap);
            if ((mx - ChipX0) % (ChipW + ChipGap) >= ChipW) return -1;
            if (idx >= MaxChips || idx >= Services.Wait.Entries.Count) return -1;
            return idx;
        }

        public bool IsOver(int mx, int my) => my < H;

        public void UpdateHover(int mx, int my)
        {
            int c = ChipAt(mx, my);
            int k = BtnAt(mx, my);
            if (c != hoverChip || k != hoverBtn) { hoverChip = c; hoverBtn = k; Dirty = true; }
        }

        public bool HandleClick(int mx, int my)
        {
            if (my >= H) return false;
            if (!Services.LoggedIn) return true;
            int c = ChipAt(mx, my);
            if (c >= 0) Services.Wait.Resume(Services.Wait.Entries[c].Win);
            int k = BtnAt(mx, my);
            if (k == 0) Services.Wait.SuspendAll();
            else if (k == 1) Services.Wait.ResumeAll();
            return true;
        }

        public void Render()
        {
            var b = Buffer;
            if (Desktop.VerreHaut != null && Desktop.VerreHaut.W == b.W) b.CopierZone(Desktop.VerreHaut, 0, 0, 0, 0, b.W, H);
            else b.FillRect(Theme.TopBarBg, 0, 0, b.W, H);
            b.FillRoundRectAlpha(System.Drawing.Color.White, 40, 0, H - 1, b.W, 1, 0);

            Glyphs.Wait.DrawTinted(b, 10, (H - 10) / 2, Theme.TabTextMuted);
            b.DrawText("En attente", Theme.TaskbarText, 28, 6);

            var list = Services.Wait.Entries;
            if (list.Count == 0)
                b.DrawText("Aucune - bouton || d'une fenetre pour l'y epingler", Theme.TabTextMuted, ChipX0, 6);

            int shown = list.Count < MaxChips ? list.Count : MaxChips;
            for (int i = 0; i < shown; i++)
            {
                WaitEntry e = list[i];
                int x = ChipX0 + i * (ChipW + ChipGap);
                b.FillRoundRect(i == hoverChip ? Theme.ChipHover : Theme.ChipBg, x, 3, ChipW, H - 6, 8);
                b.Blit(e.Thumb, x + 6, 4);
                e.Win.Icon.Draw(b, x + 44, 6);
                string t = e.Win.Title;
                int max = (ChipW - 72) / 8;
                if (t.Length > max) t = t.Substring(0, max);
                b.DrawText(t, Theme.TitleText, x + 66, 6);
            }
            if (list.Count > shown)
                b.DrawText("+" + (list.Count - shown), Theme.TaskbarText, ChipX0 + shown * (ChipW + ChipGap), 6);

            // Boutons : tout mettre en attente / tout reprendre (glyphes hexa)
            for (int i = 0; i < 2; i++)
            {
                int x = BtnX(i);
                b.FillRoundRect(i == hoverBtn ? Theme.ChipHover : Theme.ChipBg, x, 3, BtnW, H - 6, 6);
                HexBitmap g = i == 0 ? Glyphs.Wait : Glyphs.Play;
                g.DrawTinted(b, x + (BtnW - g.W) / 2, (H - g.H) / 2, Theme.TitleText);
            }

            // A droite : charge du systeme
            int actifs = 0, gele = 0;
            var procs = Services.Scheduler.Processes;
            for (int i = 0; i < procs.Count; i++) { if (procs[i].Suspended) gele++; else actifs++; }
            string info = "Actifs " + actifs + "  Geles " + gele + "  " + Services.Fps + " i/s";
            b.DrawText(info, Theme.TabTextMuted, b.W - info.Length * 8 - 12, 6);
        }
    }
}
