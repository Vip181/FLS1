using System;
using System.IO;
using FileSystemOS.Graphique;
using FileSystemOS.Utils;

namespace FileSystemOS.Systeme
{
    /// <summary>Telechargements : dossier \FSOS\Utilisateurs\nom\Telechargements et ouverture du bon logiciel.</summary>
    public static class Downloads
    {
        public static string Folder()
        {
            string home = Comptes.Home();
            string root = InstallerWindow.FindRoot();
            string f = home != null ? home + "\\Telechargements" : (root != null ? root + "\\Telechargements" : null);
            if (f == null) return null;
            if (home != null) FileSystemOS.Utils.FsTools.MakeDir(home);
            FileSystemOS.Utils.FsTools.MakeDir(f);
            return f;
        }

        public static string ExtFor(string contentType)
        {
            string t = contentType.ToLower();
            if (t.StartsWith("application/pdf")) return ".pdf";
            if (t.StartsWith("image/png")) return ".png";
            if (t.StartsWith("image/jpeg")) return ".jpg";
            if (t.StartsWith("image/bmp") || t.StartsWith("image/x-ms-bmp")) return ".bmp";
            if (t.StartsWith("text/plain")) return ".txt";
            if (t.StartsWith("text/html")) return ".htm";
            return "";
        }

        /// <summary>Nom de fichier propre a partir d'une URL.</summary>
        public static string NameFromUrl(string url, string contentType)
        {
            string u = url;
            int q = u.IndexOf('?');
            if (q >= 0) u = u.Substring(0, q);
            string last = u.Substring(u.LastIndexOf('/') + 1);
            string n = "";
            for (int i = 0; i < last.Length && n.Length < 40; i++)
            {
                char c = last[i];
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '.' || c == '_' || c == '-') n += c;
            }
            if (n.Length == 0 || n == ".") n = "fichier";
            if (n.IndexOf('.') < 0) n += ExtFor(contentType);
            return n;
        }

        /// <summary>Enregistre sans ecraser (nom_2, nom_3...). Renvoie le chemin complet.</summary>
        public static string Save(string folder, string name, byte[] data)
        {
            if (folder == null) throw new Exception("aucun disque pour enregistrer");
            string baseName = name, ext = "";
            int dot = name.LastIndexOf('.');
            if (dot > 0) { baseName = name.Substring(0, dot); ext = name.Substring(dot); }
            string p = TextCodec.PathJoin(folder, name);
            int k = 2;
            while (File.Exists(p)) p = TextCodec.PathJoin(folder, baseName + "_" + (k++) + ext);
            FileSystemOS.Utils.FsTools.WriteBytes(p, data);
            Live.SaveIfActive();
            return p;
        }

        /// <summary>Ouvre un fichier avec le logiciel adapte. Renvoie un message.</summary>
        public static string Open(string path)
        {
            string n = path.ToLower();
            if (n.EndsWith(".pdf")) { Services.PdfWin.Load(path); Services.Windows.Open(Services.PdfWin); return "ouvert dans le Lecteur PDF"; }
            if (ImageLoader.IsImage(n)) { Services.ImageWin.Load(path); Services.Windows.Open(Services.ImageWin); return "ouvert dans Images"; }
            if (n.EndsWith(".txt") || n.EndsWith(".md") || n.EndsWith(".csv") || n.EndsWith(".fsl") || n.EndsWith(".htm") || n.EndsWith(".html"))
            {
                Services.EditorWin.Load(path);
                Services.Windows.Open(Services.EditorWin);
                return "ouvert dans l'editeur";
            }
            return "enregistre (format non reconnu)";
        }
    }
}
