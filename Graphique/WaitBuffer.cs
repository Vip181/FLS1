using System.Collections.Generic;

namespace FileSystemOS.Graphique
{
    public class WaitEntry
    {
        public Window Win;
        public PixelBuffer Thumb;   // miniature affichee dans la barre d'attente
        public int Row;             // lignes de miniature deja calculees
    }

    /// <summary>
    /// BUFFER D'ATTENTE : les fenetres mises en attente y sont rangees.
    ///  - leur processus est GELE (l'ordonnanceur ne l'execute plus) ;
    ///  - elles ne sont plus dessinees ni composees a l'ecran ;
    ///  - une miniature est calculee petit a petit par le thread "tampon"
    ///    (quelques lignes par tour, pour ne jamais faire laguer l'OS).
    /// </summary>
    public class WaitBuffer
    {
        public const int ThumbW = 32, ThumbH = 20, RowsPerStep = 4;
        public readonly List<WaitEntry> Entries = new List<WaitEntry>();

        private WaitEntry Find(Window w)
        {
            for (int i = 0; i < Entries.Count; i++) if (Entries[i].Win == w) return Entries[i];
            return null;
        }

        private void Changed()
        {
            Services.TopBar.Dirty = true;
            Services.Taskbar.Invalidate();
            Services.ScreenDirty = true;
        }

        public void Suspend(Window w)
        {
            if (w.Suspended) return;
            w.Suspended = true;
            if (w.Proc != null) w.Proc.Suspended = true;

            var e = new WaitEntry();
            e.Win = w;
            e.Thumb = new PixelBuffer("miniature " + w.Title, ThumbW, ThumbH);
            Entries.Add(e);
            Changed();
        }

        public void Resume(Window w)
        {
            WaitEntry e = Find(w);
            if (e != null) { Entries.Remove(e); e.Thumb.Release(); }
            w.Suspended = false;
            w.Minimized = false;
            if (w.Proc != null) w.Proc.Suspended = false;
            w.Dirty = true;
            Services.Windows.BringToFront(w);
            Changed();
        }

        /// <summary>Met en attente toutes les fenetres visibles.</summary>
        public void SuspendAll()
        {
            var list = Services.Windows.All;
            for (int i = 0; i < list.Count; i++) if (!list[i].Hidden && !list[i].Fixed) Suspend(list[i]);
        }

        public void ResumeAll()
        {
            while (Entries.Count > 0) Resume(Entries[0].Win);
        }

        /// <summary>
        /// Anti-saturation : au-dela de MaxActive fenetres visibles, la plus ancienne
        /// (tout au fond, jamais celle qui a le focus) part automatiquement en attente.
        /// </summary>
        public static int MaxActive = 4;
        public bool AutoBalance = true;

        public void Balance()
        {
            if (!AutoBalance) return;
            var z = Services.Windows.Windows;
            int visible = 0;
            for (int i = 0; i < z.Count; i++) if (!z[i].Hidden) visible++;
            if (visible <= MaxActive) return;
            Window focused = Services.Windows.Focused;
            for (int i = 0; i < z.Count; i++)
            {
                if (z[i].Hidden || z[i] == focused || z[i].Fixed) continue;
                Suspend(z[i]);
                return;
            }
        }

        /// <summary>Fenetre fermee : on l'oublie.</summary>
        public void Forget(Window w)
        {
            WaitEntry e = Find(w);
            if (e == null) return;
            Entries.Remove(e);
            e.Thumb.Release();
            w.Suspended = false;
            Changed();
        }

        /// <summary>Travail du thread "tampon" : avance les miniatures. Vrai si quelque chose a change.</summary>
        public bool Work()
        {
            bool did = false;
            for (int i = 0; i < Entries.Count; i++)
            {
                WaitEntry e = Entries[i];
                PixelBuffer src = e.Win.Buffer;
                for (int n = 0; n < RowsPerStep && e.Row < ThumbH; n++, e.Row++)
                {
                    int sy = e.Row * src.H / ThumbH;
                    for (int x = 0; x < ThumbW; x++)
                        e.Thumb.Data[e.Row * ThumbW + x] = src.Data[sy * src.W + x * src.W / ThumbW];
                    did = true;
                }
            }
            return did;
        }
    }
}
