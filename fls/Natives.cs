using System;
using System.Collections.Generic;
using System.Drawing;
using FileSystemOS.Graphique;
using FileSystemOS.Processus;

namespace FileSystemOS.Fsl
{
    /// <summary>Ce qu'un programme FSL possede : ses fenetres, ses evenements, son reveil.</summary>
    public class FslContexte
    {
        public readonly List<FslWindow> Fenetres = new List<FslWindow>();
        public readonly List<string> Evenements = new List<string>();
        public uint ReveilA;              // le programme dort jusqu'a cette heure (ms)
        public int ProchainId = 1;
        public string Programme = "";

        /// <summary>Pilote : ports materiels declares dans son fichier .config (null : programme ordinaire).</summary>
        public List<int> Ports;
        public string Pilote;
        private uint graine = 12345;

        public int Hasard(int n)
        {
            if (graine == 12345) graine ^= Horloge.Millis | 1;
            graine ^= graine << 13; graine ^= graine >> 17; graine ^= graine << 5;
            return n <= 0 ? 0 : (int)(graine % (uint)n);
        }

        public FslWindow Fenetre(int id)
        {
            for (int i = 0; i < Fenetres.Count; i++) if (Fenetres[i].Id == id) return Fenetres[i];
            throw new Exception("fenetre " + id + " inconnue");
        }

        public ControleFsl Controle(int id, out FslWindow f)
        {
            for (int i = 0; i < Fenetres.Count; i++)
                for (int j = 0; j < Fenetres[i].Controles.Count; j++)
                    if (Fenetres[i].Controles[j].Id == id) { f = Fenetres[i]; return Fenetres[i].Controles[j]; }
            throw new Exception("element " + id + " inconnu");
        }

        /// <summary>Fenetres fermees par l'utilisateur (croix) : evenement "fermeture:id".</summary>
        public void Surveiller()
        {
            for (int i = Fenetres.Count - 1; i >= 0; i--)
                if (!Services.Windows.All.Contains(Fenetres[i]))
                {
                    Evenements.Add("fermeture:" + Fenetres[i].Id);
                    Fenetres.RemoveAt(i);
                }
        }

        public void FermerTout()
        {
            for (int i = 0; i < Fenetres.Count; i++) { Services.Windows.Close(Fenetres[i]); Liberer(Fenetres[i]); }
            Fenetres.Clear();
        }

        /// <summary>Rend la memoire des dessins d'une fenetre.</summary>
        public static void Liberer(FslWindow f)
        {
            for (int i = 0; i < f.Controles.Count; i++)
                if (f.Controles[i].Image != null) { f.Controles[i].Image.Release(); f.Controles[i].Image = null; }
        }
    }

    /// <summary>
    /// FONCTIONS INTEGREES du File Syst Langage. Elles s'appellent comme en C :
    ///     int f = fenetre("Mon jeu", 400, 300);
    ///     int b = bouton(f, "Jouer", 20, 20, 120, 32);
    ///     tantque (vrai) { texte e = evenement(); si (e == "clic:" + vers_texte(b)) { ... } }
    /// Couleurs : un nombre, obtenu avec rgb(rouge, vert, bleu).
    /// </summary>
    public static class Natives
    {
        public static readonly string[] Noms =
        {
            "fenetre", "bouton", "etiquette", "champ", "zone", "dessin",
            "lire", "ecrire", "fermer", "evenement",
            "effacer", "rectangle", "point", "dessiner_texte", "rgb",
            "hasard", "temps", "attendre", "longueur", "vers_texte", "vers_nombre", "sous_texte",
            "port_lire", "port_ecrire", "port_lire16", "port_ecrire16", "port_lire32", "port_ecrire32", "journal",
            "majuscules", "minuscules", "contient", "position", "remplacer", "caractere", "code", "depuis_code",
            "abs", "min", "max", "racine", "puissance", "lire_fichier", "ecrire_fichier"
        };

        public static readonly int[] NbArgs =
        {
            3, 6, 4, 4, 5, 5, 1, 2, 1, 0, 2, 6, 4, 5, 3, 1, 0, 1, 1, 1, 1, 3,
            1, 2, 1, 2, 1, 2, 1,
            1, 1, 2, 2, 3, 2, 1, 1,
            1, 2, 2, 1, 2, 1, 2
        };

        public static readonly string[] Aides =
        {
            "fenetre(titre, largeur, hauteur) : cree une fenetre, renvoie son numero",
            "bouton(fenetre, texte, x, y, largeur, hauteur) : renvoie son numero ; clic -> evenement \"clic:N\"",
            "etiquette(fenetre, texte, x, y) : un texte fixe",
            "champ(fenetre, x, y, largeur) : zone de saisie d'une ligne ; Entree -> \"valider:N\"",
            "zone(fenetre, x, y, largeur, hauteur) : zone de texte de plusieurs lignes",
            "dessin(fenetre, x, y, largeur, hauteur) : surface pour dessiner ; clic -> \"souris:N:x:y\"",
            "lire(element) : le texte d'un champ, d'une zone, d'un bouton ou d'une etiquette",
            "ecrire(element, texte) : change ce texte",
            "fermer(fenetre)",
            "evenement() : prochain evenement (\"clic:N\", \"touche:a\", \"fermeture:N\"...) ou \"\"",
            "effacer(dessin, couleur)",
            "rectangle(dessin, x, y, largeur, hauteur, couleur)",
            "point(dessin, x, y, couleur)",
            "dessiner_texte(dessin, texte, x, y, couleur)",
            "rgb(rouge, vert, bleu) : une couleur (0 a 255 chacun)",
            "hasard(n) : nombre au hasard de 0 a n-1",
            "temps() : millisecondes depuis le demarrage",
            "attendre(ms) : met le programme en pause (les autres continuent)",
            "longueur(texte)",
            "vers_texte(nombre)",
            "vers_nombre(texte) : 0/nul si ce n'est pas un nombre",
            "sous_texte(texte, debut, longueur)",
            "port_lire(port) : lit un octet sur un port materiel (PILOTES seulement, ports du .config)",
            "port_ecrire(port, valeur) : ecrit un octet (PILOTES seulement)",
            "port_lire16(port) : lit 16 bits (PILOTES seulement)",
            "port_ecrire16(port, valeur) : ecrit 16 bits (PILOTES seulement)",
            "port_lire32(port) : lit 32 bits (PILOTES seulement)",
            "port_ecrire32(port, valeur) : ecrit 32 bits (PILOTES seulement)",
            "journal(texte) : ajoute une ligne au journal du systeme",
            "majuscules(texte)",
            "minuscules(texte)",
            "contient(texte, morceau) : vrai ou faux",
            "position(texte, morceau) : position du morceau, ou -1",
            "remplacer(texte, ancien, nouveau)",
            "caractere(texte, position) : un seul caractere",
            "code(texte) : numero du premier caractere (A = 65)",
            "depuis_code(nombre) : le caractere de ce numero",
            "abs(nombre) : valeur absolue",
            "min(a, b)",
            "max(a, b)",
            "racine(nombre) : racine carree entiere",
            "puissance(a, b) : a multiplie b fois par lui-meme",
            "lire_fichier(chemin) : contenu d'un fichier texte (chemin depuis \\FSOS, ex. \"Documents\\\\note.txt\")",
            "ecrire_fichier(chemin, texte) : cree ou remplace un fichier texte ; vrai si reussi"
        };

        /// <summary>Un port n'est accessible qu'a un pilote qui l'a declare dans son .config.</summary>
        private static ushort Port(FslContexte c, Value v, int fn)
        {
            int port = N(v, Noms[fn]);
            if (c.Ports == null) throw new Exception("reserve aux pilotes (fichier .config dans \\FSOS\\Systeme\\Pilotes)");
            for (int i = 0; i < c.Ports.Count; i++) if (c.Ports[i] == port) return (ushort)port;
            throw new Exception("le port " + port + " n'est pas declare dans le .config de " + c.Pilote);
        }

        /// <summary>"Documents\\note.txt" -> le fichier dans \\FSOS ; un chemin complet (1:\\...) reste tel quel.</summary>
        private static string Chemin(string ch)
        {
            if (ch.IndexOf(':') >= 0) return ch;
            string vol = FileSystemOS.Systeme.Live.Volume ?? "0:\\";
            return FileSystemOS.Utils.TextCodec.PathJoin(FileSystemOS.Utils.TextCodec.PathJoin(vol, "FSOS"), ch);
        }

        public static int Trouver(string nom)
        {
            for (int i = 0; i < Noms.Length; i++) if (Noms[i] == nom) return i;
            return -1;
        }

        private static int N(Value v, string fn)
        {
            if (v.Kind == Value.KInt) return v.I;
            throw new Exception(fn + " : nombre attendu");
        }

        private static string T(Value v) => v.Kind == Value.KNull ? "" : Vm.ToText(v);

        private static Color C(int rgb) => Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

        private static int Borne(int v, int min, int max) => v < min ? min : (v > max ? max : v);

        private static Value Nouveau(FslContexte c, FslWindow f, int genre, string texte, int x, int y, int l, int h)
        {
            var k = new ControleFsl
            {
                Id = c.ProchainId++, Genre = genre, Texte = texte,
                X = Borne(x, 0, 2000), Y = Borne(y, 0, 2000), L = Borne(l, 1, 2000), H = Borne(h, 1, 2000)
            };
            if (genre == ControleFsl.Dessin)
            {
                k.Image = new PixelBuffer("fsl-dessin", k.L, k.H);
                k.Image.Clear(Color.White);
            }
            f.Controles.Add(k);
            f.Dirty = true;
            return Value.Int(k.Id);
        }

        private static PixelBuffer Surface(FslContexte c, Value v, out FslWindow f)
        {
            ControleFsl k = c.Controle(N(v, "dessin"), out f);
            if (k.Genre != ControleFsl.Dessin) throw new Exception("l'element " + k.Id + " n'est pas un dessin");
            f.Dirty = true;
            return k.Image;
        }

        public static Value Appeler(int fn, Value[] a, FslContexte c)
        {
            FslWindow f;
            switch (fn)
            {
                case 0:   // fenetre
                    {
                        if (c.Fenetres.Count >= 8) throw new Exception("8 fenetres au plus par programme");
                        int w = Borne(N(a[1], "fenetre"), 120, Services.ScreenW - 20);
                        int h = Borne(N(a[2], "fenetre"), 60, Services.ScreenH - 80);
                        var nf = new FslWindow(c.ProchainId++, T(a[0]), w, h, c.Evenements);
                        c.Fenetres.Add(nf);
                        Services.Windows.Open(nf);
                        return Value.Int(nf.Id);
                    }
                case 1: f = c.Fenetre(N(a[0], "bouton")); return Nouveau(c, f, ControleFsl.Bouton, T(a[1]), N(a[2], "bouton"), N(a[3], "bouton"), N(a[4], "bouton"), N(a[5], "bouton"));
                case 2: f = c.Fenetre(N(a[0], "etiquette")); return Nouveau(c, f, ControleFsl.Etiquette, T(a[1]), N(a[2], "etiquette"), N(a[3], "etiquette"), T(a[1]).Length * 8, 16);
                case 3: f = c.Fenetre(N(a[0], "champ")); return Nouveau(c, f, ControleFsl.Champ, "", N(a[1], "champ"), N(a[2], "champ"), N(a[3], "champ"), 26);
                case 4: f = c.Fenetre(N(a[0], "zone")); return Nouveau(c, f, ControleFsl.Zone, "", N(a[1], "zone"), N(a[2], "zone"), N(a[3], "zone"), N(a[4], "zone"));
                case 5:
                    {
                        f = c.Fenetre(N(a[0], "dessin"));
                        int l = Borne(N(a[3], "dessin"), 1, 1600), h = Borne(N(a[4], "dessin"), 1, 1000);
                        return Nouveau(c, f, ControleFsl.Dessin, "", N(a[1], "dessin"), N(a[2], "dessin"), l, h);
                    }
                case 6: return Value.Text(c.Controle(N(a[0], "lire"), out f).Texte);
                case 7:
                    {
                        ControleFsl k = c.Controle(N(a[0], "ecrire"), out f);
                        k.Texte = T(a[1]);
                        if (k.Genre == ControleFsl.Etiquette) k.L = k.Texte.Length * 8;
                        f.Dirty = true;
                        return new Value();
                    }
                case 8:
                    {
                        f = c.Fenetre(N(a[0], "fermer"));
                        Services.Windows.Close(f);
                        FslContexte.Liberer(f);
                        c.Fenetres.Remove(f);
                        return new Value();
                    }
                case 9:   // evenement : s'il n'y en a pas, le programme cede la main un instant
                    {
                        c.Surveiller();
                        if (c.Evenements.Count == 0) { c.ReveilA = Horloge.Millis + 15; return Value.Text(""); }
                        string e = c.Evenements[0];
                        c.Evenements.RemoveAt(0);
                        return Value.Text(e);
                    }
                case 10: Surface(c, a[0], out f).Clear(C(N(a[1], "effacer"))); return new Value();
                case 11:
                    Surface(c, a[0], out f).FillRect(C(N(a[5], "rectangle")), N(a[1], "rectangle"), N(a[2], "rectangle"),
                                                     N(a[3], "rectangle"), N(a[4], "rectangle"));
                    return new Value();
                case 12:
                    {
                        PixelBuffer s = Surface(c, a[0], out f);
                        s.SetPixel(N(a[1], "point"), N(a[2], "point"), PixelBuffer.Argb(C(N(a[3], "point"))));
                        return new Value();
                    }
                case 13: Surface(c, a[0], out f).DrawText(T(a[1]), C(N(a[4], "dessiner_texte")), N(a[2], "dessiner_texte"), N(a[3], "dessiner_texte")); return new Value();
                case 14: return Value.Int((Borne(N(a[0], "rgb"), 0, 255) << 16) | (Borne(N(a[1], "rgb"), 0, 255) << 8) | Borne(N(a[2], "rgb"), 0, 255));
                case 15: return Value.Int(c.Hasard(N(a[0], "hasard")));
                case 16: return Value.Int((int)Horloge.Millis);
                case 17: c.ReveilA = Horloge.Millis + (uint)Borne(N(a[0], "attendre"), 0, 600000); return new Value();
                case 18: return Value.Int(T(a[0]).Length);
                case 19: return Value.Text(T(a[0]));
                case 20:
                    {
                        string s = T(a[0]).Trim();
                        if (s.Length == 0 || s.Length > 10) return new Value();
                        int v = 0, i = 0;
                        bool neg = s[0] == '-';
                        if (neg) i = 1;
                        if (i >= s.Length) return new Value();
                        for (; i < s.Length; i++)
                        {
                            if (s[i] < '0' || s[i] > '9') return new Value();
                            v = v * 10 + (s[i] - '0');
                        }
                        return Value.Int(neg ? -v : v);
                    }
                case 21:
                    {
                        string s = T(a[0]);
                        int d = Borne(N(a[1], "sous_texte"), 0, s.Length);
                        int n = Borne(N(a[2], "sous_texte"), 0, s.Length - d);
                        return Value.Text(s.Substring(d, n));
                    }
                case 22: return Value.Int(new Cosmos.Core.IOPort(Port(c, a[0], fn)).Byte);
                case 23: new Cosmos.Core.IOPort(Port(c, a[0], fn)).Byte = (byte)N(a[1], "port_ecrire"); return new Value();
                case 24: return Value.Int(new Cosmos.Core.IOPort(Port(c, a[0], fn)).Word);
                case 25: new Cosmos.Core.IOPort(Port(c, a[0], fn)).Word = (ushort)N(a[1], "port_ecrire16"); return new Value();
                case 26: return Value.Int((int)new Cosmos.Core.IOPort(Port(c, a[0], fn)).DWord);
                case 27: new Cosmos.Core.IOPort(Port(c, a[0], fn)).DWord = (uint)N(a[1], "port_ecrire32"); return new Value();
                case 28:
                    FileSystemOS.Systeme.Journal.Ajouter((c.Pilote != null ? "pilote " + c.Pilote : "FSL " + c.Programme) + " : " + T(a[0]),
                        FileSystemOS.Systeme.Comptes.Actuel != null ? FileSystemOS.Systeme.Comptes.Actuel.Nom : "systeme");
                    return new Value();
                case 29: return Value.Text(T(a[0]).ToUpper());
                case 30: return Value.Text(T(a[0]).ToLower());
                case 31: return Value.Int(T(a[0]).IndexOf(T(a[1])) >= 0 ? 1 : 0);
                case 32: return Value.Int(T(a[0]).IndexOf(T(a[1])));
                case 33:
                    {
                        string s = T(a[0]), ancien = T(a[1]), nouveau = T(a[2]);
                        if (ancien.Length == 0) return Value.Text(s);
                        string r = "";
                        int i = 0;
                        while (i < s.Length)
                        {
                            int k = s.IndexOf(ancien, i);
                            if (k < 0) { r += s.Substring(i); break; }
                            r += s.Substring(i, k - i) + nouveau;
                            i = k + ancien.Length;
                        }
                        return Value.Text(r);
                    }
                case 34:
                    {
                        string s = T(a[0]);
                        int i = N(a[1], "caractere");
                        return Value.Text(i >= 0 && i < s.Length ? s[i].ToString() : "");
                    }
                case 35: { string s = T(a[0]); return Value.Int(s.Length > 0 ? s[0] : 0); }
                case 36: return Value.Text(((char)Borne(N(a[0], "depuis_code"), 0, 65535)).ToString());
                case 37: { int v = N(a[0], "abs"); return Value.Int(v < 0 ? -v : v); }
                case 38: { int x = N(a[0], "min"), y = N(a[1], "min"); return Value.Int(x < y ? x : y); }
                case 39: { int x = N(a[0], "max"), y = N(a[1], "max"); return Value.Int(x > y ? x : y); }
                case 40:
                    {
                        int v = N(a[0], "racine");
                        if (v < 0) throw new Exception("racine d'un nombre negatif");
                        int r = 0;
                        while ((long)(r + 1) * (r + 1) <= v) r++;
                        return Value.Int(r);
                    }
                case 41:
                    {
                        int b = N(a[0], "puissance"), e = N(a[1], "puissance"), r = 1;
                        if (e < 0) throw new Exception("puissance negative");
                        for (int i = 0; i < e && i < 64; i++) r *= b;
                        return Value.Int(r);
                    }
                case 42:
                    {
                        string ch = Chemin(T(a[0]));
                        if (!FileSystemOS.Utils.FsTools.Exists(ch)) return new Value();
                        byte[] o = FileSystemOS.Utils.FsTools.ReadBytes(ch);
                        char[] t = new char[o.Length];
                        for (int i = 0; i < o.Length; i++) t[i] = (char)o[i];
                        return Value.Text(new string(t));
                    }
                case 43:
                    {
                        string s = T(a[1]);
                        byte[] o = new byte[s.Length];
                        for (int i = 0; i < s.Length; i++) o[i] = s[i] < 256 ? (byte)s[i] : (byte)'?';
                        try { FileSystemOS.Utils.FsTools.WriteBytes(Chemin(T(a[0])), o); return Value.Int(1); }
                        catch { return Value.Int(0); }
                    }
            }
            throw new Exception("fonction " + fn + " inconnue");
        }
    }
}
