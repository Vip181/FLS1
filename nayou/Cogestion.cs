using System.Collections.Generic;
using FileSystemOS.Graphique;
using FileSystemOS.Processus;

namespace FileSystemOS.Noyau
{
    /// <summary>
    /// COGESTION threads / processus / buffers de l'interface graphique.
    ///
    /// Chaque fenetre garde son processus et son thread, mais ceux-ci ne dessinent plus
    /// eux-memes : ils deposent une DEMANDE DE RENDU dans un buffer commun.
    /// Le thread "rendus" (processus "cogestion", priorite haute) traite au plus
    /// Budget demandes par tour d'ordonnanceur, la fenetre active en premier.
    /// Resultat : meme avec beaucoup de fenetres, un tour reste court -> pas de lag.
    /// </summary>
    public static class Cogestion
    {
        public static int Budget = 2;          // rendus maximum par tour (interface.cfg)
        public static uint Requests, Renders;

        private static readonly List<Window> queue = new List<Window>();

        public static int Pending => queue.Count;

        public static void Request(Window w)
        {
            if (w.Queued) return;
            w.Queued = true;
            queue.Add(w);
            Requests++;
        }

        public static void Forget(Window w)
        {
            if (!w.Queued) return;
            queue.Remove(w);
            w.Queued = false;
        }

        public static void Work()
        {
            if (queue.Count == 0) return;
            Window focused = Services.Windows.Focused;
            int done = 0;
            try
            {
                // La fenetre active d'abord, sans deplacer les autres dans la file
                if (focused != null && focused.Queued)
                {
                    int fi = queue.IndexOf(focused);
                    if (fi >= 0)
                    {
                        queue.RemoveAt(fi);
                        if (Rendre(focused, true)) done++;
                    }
                }
                while (done < Budget && queue.Count > 0)
                {
                    Window w = queue[0];
                    queue.RemoveAt(0);
                    if (Rendre(w, w == focused)) done++;
                }
            }
            catch (System.Exception e)
            {
                // File abimee (fenetre fermee pendant un rendu...) : on la vide proprement,
                // chaque fenetre redemandera son dessin au tour suivant.
                Reinitialiser();
                Diagnostic.Report("cogestion", "file des rendus reinitialisee (" + e.Message + ")");
            }
        }

        private static bool Rendre(Window w, bool active)
        {
            if (w == null) return false;
            w.Queued = false;
            if (w.Hidden) return false;           // en attente / reduite : inutile de dessiner
            w.Dirty = false;
            try { w.Render(active); }
            catch (System.Exception e) { Diagnostic.Report("dessin de " + w.Title, e.Message); }
            Renders++;
            Services.ScreenDirty = true;
            return true;
        }

        private static void Reinitialiser()
        {
            queue.Clear();
            var toutes = Services.Windows.All;
            for (int i = 0; i < toutes.Count; i++)
            {
                toutes[i].Queued = false;
                toutes[i].Dirty = true;
            }
        }
    }

    public class CogestionThread : KThread
    {
        public CogestionThread() : base("rendus") { }
        public override void Step() => Cogestion.Work();
    }
}
