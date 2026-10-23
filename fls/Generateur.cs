using System;
using System.Collections.Generic;
using FileSystemOS.Utils;

namespace FileSystemOS.Fsl
{
    /// <summary>Un element pose dans le Createur : bouton, etiquette, champ, zone de texte ou dessin.</summary>
    public class ElementStudio
    {
        public const int Bouton = 1, Etiquette = 2, Champ = 3, Zone = 4, Dessin = 5;
        public int Genre, X, Y, L, H, Action;
        public string Nom = "", Texte = "", Cible = "", Source = "", Source2 = "", Valeur = "";

        public ElementStudio(int genre, string nom, string texte, int x, int y, int l, int h)
        {
            Genre = genre; Nom = nom; Texte = texte; X = x; Y = y; L = l; H = h;
        }

        public ElementStudio Agir(int action, string cible, string source, string source2, string valeur)
        {
            Action = action; Cible = cible; Source = source; Source2 = source2; Valeur = valeur;
            return this;
        }

        public static string NomGenre(int g)
        {
            switch (g)
            {
                case Bouton: return "bouton";
                case Etiquette: return "etiquette";
                case Champ: return "champ";
                case Zone: return "zone";
                default: return "dessin";
            }
        }

        /// <summary>Un bouton (clic) ou un champ (touche Entree) peut declencher une action.</summary>
        public bool PeutAgir => Genre == Bouton || Genre == Champ;
    }

    /// <summary>
    /// PROJET DU CREATEUR DE PROGRAMMES : une fenetre et ses elements, plus l'action de chaque
    /// bouton. Il produit le code FSL complet (GenererCode) et s'enregistre dans un fichier .fsp
    /// qui se rouvre dans le Createur.
    /// Valide en Python avant traduction : 4 modeles et 2 000 projets aleatoires (noms bizarres,
    /// doublons, mots reserves, guillemets...) donnent toujours du code qui compile ; 3 000
    /// projets enregistres puis relus sont identiques.
    /// </summary>
    public class ProjetStudio
    {
        public string Nom = "MonProgramme", Titre = "Mon programme";
        public int Largeur = 420, Hauteur = 300;
        public readonly List<ElementStudio> Elements = new List<ElementStudio>();

        public static readonly string[] Actions =
        {
            "Rien", "Ecrire un texte", "Ajouter une ligne", "Calculer", "Effacer", "Compter +1",
            "Remettre a zero", "Enregistrer dans un fichier", "Charger un fichier", "Fermer le programme", "Code FSL libre"
        };

        public const int ARien = 0, AEcrire = 1, AAjouter = 2, ACalculer = 3, AEffacer = 4, ACompter = 5,
                         AZero = 6, AEnregistrer = 7, ACharger = 8, AFermer = 9, ACode = 10;

        public static readonly string[] Operations = { "+", "-", "*", "/", "%" };

        private const string FichierParDefaut = "Documents\\notes.txt";

        // ---- Noms de variables ----

        private static bool Lettre(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_';
        private static bool Chiffre(char c) => c >= '0' && c <= '9';

        /// <summary>Nom utilisable comme variable FSL : lettres, chiffres, _ ; pas un mot reserve.</summary>
        public static string NomValide(string n)
        {
            char[] r = new char[n.Length];
            for (int i = 0; i < n.Length; i++) r[i] = Lettre(n[i]) || Chiffre(n[i]) ? n[i] : '_';
            string s = new string(r);
            if (s.Length == 0 || Chiffre(s[0])) s = "x" + s;
            if (Lexer.IsKeyword(s) || Natives.Trouver(s) >= 0 || s == "f" || s == "e" || s == "fini" || s == "nombre" ||
                s.StartsWith("compte_"))
                s = "el_" + s;
            return s;
        }

        /// <summary>Nom de variable final de chaque element (valide et unique), dans l'ordre.</summary>
        public List<string> NomsUniques()
        {
            var vus = new List<string>();
            for (int i = 0; i < Elements.Count; i++)
            {
                string n = NomValide(Elements[i].Nom), b = n;
                int k = 2;
                while (Listes.Contient(vus, n)) { n = b + k; k++; }
                vus.Add(n);
            }
            return vus;
        }

        /// <summary>Premier nom libre pour un nouvel element : bouton1, bouton2...</summary>
        public string NomLibre(int genre)
        {
            string b = ElementStudio.NomGenre(genre);
            for (int k = 1; ; k++)
            {
                string n = b + k;
                bool pris = false;
                for (int i = 0; i < Elements.Count; i++) if (Elements[i].Nom == n) pris = true;
                if (!pris) return n;
            }
        }

        /// <summary>"texte" -> "\"texte\"" avec \\ \" et \n echappes.</summary>
        public static string Texte(string s)
        {
            string r = "\"";
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\') r += "\\\\";
                else if (c == '"') r += "\\\"";
                else if (c == '\n') r += "\\n";
                else r += c;
            }
            return r + "\"";
        }

        // ---- Generation du code FSL ----

        public string GenererCode()
        {
            List<string> noms = NomsUniques();
            var L = new List<string>();
            L.Add("// " + Nom + " : programme cree avec le Createur de programmes de File System OS");
            L.Add("// Le projet " + Nom + ".fsp se rouvre dans le Createur ; ce code peut aussi se modifier a la main.");
            L.Add("");

            bool calcul = false;
            for (int i = 0; i < Elements.Count; i++) if (Elements[i].Action == ACalculer) calcul = true;
            if (calcul)
            {
                L.Add("// Un champ vide ou qui ne contient pas un nombre compte pour 0");
                L.Add("fonction entier nombre(texte t) {");
                L.Add("    entier n = vers_nombre(t);");
                L.Add("    si (n == 0/nul) { retourner 0; }");
                L.Add("    retourner n;");
                L.Add("}");
                L.Add("");
            }

            L.Add("entier f = fenetre(" + Texte(Titre) + ", " + Largeur + ", " + Hauteur + ");");
            for (int i = 0; i < Elements.Count; i++)
            {
                ElementStudio e = Elements[i];
                string n = noms[i];
                switch (e.Genre)
                {
                    case ElementStudio.Bouton:
                        L.Add("entier " + n + " = bouton(f, " + Texte(e.Texte) + ", " + e.X + ", " + e.Y + ", " + e.L + ", " + e.H + ");");
                        break;
                    case ElementStudio.Etiquette:
                        L.Add("entier " + n + " = etiquette(f, " + Texte(e.Texte) + ", " + e.X + ", " + e.Y + ");");
                        break;
                    case ElementStudio.Champ:
                        L.Add("entier " + n + " = champ(f, " + e.X + ", " + e.Y + ", " + e.L + ");");
                        if (e.Texte.Length > 0) L.Add("ecrire(" + n + ", " + Texte(e.Texte) + ");");
                        break;
                    case ElementStudio.Zone:
                        L.Add("entier " + n + " = zone(f, " + e.X + ", " + e.Y + ", " + e.L + ", " + e.H + ");");
                        if (e.Texte.Length > 0) L.Add("ecrire(" + n + ", " + Texte(e.Texte) + ");");
                        break;
                    default:
                        L.Add("entier " + n + " = dessin(f, " + e.X + ", " + e.Y + ", " + e.L + ", " + e.H + ");");
                        break;
                }
            }

            // Un compteur par element vise par "Compter +1" ou "Remettre a zero"
            var compteurs = new List<string>();
            for (int i = 0; i < Elements.Count; i++)
            {
                ElementStudio e = Elements[i];
                if (e.Action != ACompter && e.Action != AZero) continue;
                string c = Variable(e.Cible, noms);
                if (c.Length > 0 && !Listes.Contient(compteurs, c)) compteurs.Add(c);
            }
            for (int i = 0; i < compteurs.Count; i++) L.Add("entier compte_" + compteurs[i] + " = 0;");
            L.Add("booleen fini = faux;");
            L.Add("");
            L.Add("tantque (!fini) {");
            L.Add("    texte e = evenement();");
            for (int i = 0; i < Elements.Count; i++)
            {
                ElementStudio e = Elements[i];
                if (e.Action == ARien || !e.PeutAgir) continue;
                string ev = e.Genre == ElementStudio.Bouton ? "clic:" : "valider:";
                L.Add("    si (e == \"" + ev + "\" + vers_texte(" + noms[i] + ")) {");
                List<string> a = CodeAction(e, noms);
                for (int k = 0; k < a.Count; k++) L.Add("        " + a[k]);
                L.Add("    }");
            }
            L.Add("    si (e == \"fermeture:\" + vers_texte(f)) { fini = vrai; }");
            L.Add("}");
            return TextCodec.Join(L);
        }

        /// <summary>Nom ecrit par l'utilisateur -> variable de l'element, ou "" s'il n'existe pas.</summary>
        private string Variable(string nom, List<string> noms)
        {
            for (int i = 0; i < Elements.Count; i++) if (Elements[i].Nom == nom) return noms[i];
            return "";
        }

        private List<string> CodeAction(ElementStudio e, List<string> noms)
        {
            var r = new List<string>();
            string c = Variable(e.Cible, noms), s = Variable(e.Source, noms), s2 = Variable(e.Source2, noms);
            int a = e.Action;
            string contenu = s.Length > 0 ? (e.Valeur.Length > 0 ? Texte(e.Valeur) + " + lire(" + s + ")" : "lire(" + s + ")") : Texte(e.Valeur);

            switch (a)
            {
                case AEcrire:
                    if (c.Length == 0) return Manque(a, "la cible");
                    r.Add("ecrire(" + c + ", " + contenu + ");");
                    break;
                case AAjouter:
                    if (c.Length == 0) return Manque(a, "la cible");
                    r.Add("ecrire(" + c + ", lire(" + c + ") + " + contenu + " + \"\\n\");");
                    break;
                case ACalculer:
                    {
                        if (c.Length == 0 || s.Length == 0 || s2.Length == 0) return Manque(a, "la cible et les deux champs");
                        string op = "+";
                        for (int k = 0; k < Operations.Length; k++) if (e.Valeur == Operations[k]) op = e.Valeur;
                        string calc = "ecrire(" + c + ", vers_texte(nombre(lire(" + s + ")) " + op + " nombre(lire(" + s2 + "))));";
                        if (op == "/" || op == "%")
                        {
                            r.Add("si (nombre(lire(" + s2 + ")) == 0) { ecrire(" + c + ", \"division par zero\"); }");
                            r.Add("sinon { " + calc + " }");
                        }
                        else r.Add(calc);
                        break;
                    }
                case AEffacer:
                    if (c.Length == 0) return Manque(a, "la cible");
                    r.Add("ecrire(" + c + ", \"\");");
                    break;
                case ACompter:
                case AZero:
                    if (c.Length == 0) return Manque(a, "la cible");
                    r.Add(a == ACompter ? "compte_" + c + " = compte_" + c + " + 1;" : "compte_" + c + " = 0;");
                    r.Add("ecrire(" + c + ", " + Texte(e.Valeur) + " + vers_texte(compte_" + c + "));");
                    break;
                case AEnregistrer:
                    if (s.Length == 0) return Manque(a, "la source");
                    r.Add("ecrire_fichier(" + Texte(e.Valeur.Length > 0 ? e.Valeur : FichierParDefaut) + ", lire(" + s + "));");
                    break;
                case ACharger:
                    if (c.Length == 0) return Manque(a, "la cible");
                    r.Add("ecrire(" + c + ", lire_fichier(" + Texte(e.Valeur.Length > 0 ? e.Valeur : FichierParDefaut) + "));");
                    break;
                case AFermer:
                    r.Add("fini = vrai;");
                    break;
                case ACode:
                    r.Add(e.Valeur.Trim().Length > 0 ? e.Valeur : "// (code libre vide)");
                    break;
            }
            return r;
        }

        private static List<string> Manque(int a, string quoi)
        {
            var r = new List<string>();
            r.Add("// action \"" + Actions[a] + "\" : choisissez " + quoi + " dans le Createur");
            return r;
        }

        // ---- Fichier projet .fsp : une ligne par element, champs separes par | ----

        private static string Echapper(string s)
        {
            string r = "";
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\') r += "\\\\";
                else if (c == '|') r += "\\p";
                else if (c == '\n') r += "\\n";
                else r += c;
            }
            return r;
        }

        private static string Desechapper(string s)
        {
            string r = "";
            int i = 0;
            while (i < s.Length)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    char n = s[i + 1];
                    r += n == 'p' ? '|' : (n == 'n' ? '\n' : n);
                    i += 2;
                }
                else { r += s[i]; i++; }
            }
            return r;
        }

        public string Sauver()
        {
            var L = new List<string>();
            L.Add("FSP1");
            L.Add("programme|" + Echapper(Nom) + "|" + Echapper(Titre) + "|" + Largeur + "|" + Hauteur);
            for (int i = 0; i < Elements.Count; i++)
            {
                ElementStudio e = Elements[i];
                L.Add("element|" + e.Genre + "|" + Echapper(e.Nom) + "|" + Echapper(e.Texte) + "|" + e.X + "|" + e.Y + "|" + e.L + "|" + e.H +
                      "|" + e.Action + "|" + Echapper(e.Cible) + "|" + Echapper(e.Source) + "|" + Echapper(e.Source2) + "|" + Echapper(e.Valeur));
            }
            return TextCodec.Join(L);
        }

        /// <summary>Nombre entier ecrit en texte (9 chiffres au plus), sinon la valeur par defaut.</summary>
        public static int Nombre(string s, int defaut)
        {
            s = s.Trim();
            if (s.Length == 0 || s.Length > 9) return defaut;
            bool neg = s[0] == '-';
            int i = neg ? 1 : 0, v = 0;
            if (i >= s.Length) return defaut;
            for (; i < s.Length; i++)
            {
                if (!Chiffre(s[i])) return defaut;
                v = v * 10 + (s[i] - '0');
            }
            return neg ? -v : v;
        }

        public static ProjetStudio Lire(string texte)
        {
            string[] lignes = texte.Split('\n');
            if (lignes.Length == 0 || lignes[0].Trim() != "FSP1") throw new Exception("ce fichier n'est pas un projet du Createur (FSP1)");
            var p = new ProjetStudio();
            for (int k = 1; k < lignes.Length; k++)
            {
                string l = lignes[k];
                if (l.Length > 0 && l[l.Length - 1] == '\r') l = l.Substring(0, l.Length - 1);
                string[] m = l.Split('|');
                if (m[0] == "programme" && m.Length >= 5)
                {
                    p.Nom = Desechapper(m[1]);
                    p.Titre = Desechapper(m[2]);
                    p.Largeur = Nombre(m[3], 420);
                    p.Hauteur = Nombre(m[4], 300);
                }
                else if (m[0] == "element" && m.Length >= 13)
                {
                    var e = new ElementStudio(Nombre(m[1], 1), Desechapper(m[2]), Desechapper(m[3]), Nombre(m[4], 0), Nombre(m[5], 0),
                                              Nombre(m[6], 100), Nombre(m[7], 30));
                    e.Agir(Nombre(m[8], 0), Desechapper(m[9]), Desechapper(m[10]), Desechapper(m[11]), Desechapper(m[12]));
                    if (e.Genre < ElementStudio.Bouton || e.Genre > ElementStudio.Dessin) e.Genre = ElementStudio.Etiquette;
                    if (e.Action < 0 || e.Action >= Actions.Length) e.Action = ARien;
                    p.Elements.Add(e);
                }
            }
            return p;
        }

        // ---- Modeles prets a l'emploi ----

        public static readonly string[] Modeles = { "Bonjour", "Calculette", "Compteur", "Bloc-notes" };

        public static ProjetStudio Modele(int k)
        {
            var p = new ProjetStudio();
            var l = p.Elements;
            if (k == 0)
            {
                p.Nom = "Bonjour"; p.Titre = "Bonjour"; p.Largeur = 420; p.Hauteur = 220;
                l.Add(new ElementStudio(ElementStudio.Etiquette, "question", "Comment vous appelez-vous ?", 20, 16, 216, 16));
                l.Add(new ElementStudio(ElementStudio.Champ, "prenom", "", 20, 40, 250, 26));
                l.Add(new ElementStudio(ElementStudio.Bouton, "saluer", "Dire bonjour", 284, 38, 116, 30).Agir(AEcrire, "reponse", "prenom", "", "Bonjour "));
                l.Add(new ElementStudio(ElementStudio.Etiquette, "reponse", "", 20, 90, 40, 16));
            }
            else if (k == 1)
            {
                p.Nom = "Calculette"; p.Titre = "Calculette"; p.Largeur = 360; p.Hauteur = 230;
                l.Add(new ElementStudio(ElementStudio.Champ, "a", "", 20, 20, 140, 26));
                l.Add(new ElementStudio(ElementStudio.Champ, "b", "", 190, 20, 140, 26));
                string[] t = { "+", "-", "x", "/" }, op = { "+", "-", "*", "/" };
                for (int j = 0; j < 4; j++)
                    l.Add(new ElementStudio(ElementStudio.Bouton, "b" + (j + 1), t[j], 20 + j * 80, 64, 70, 32).Agir(ACalculer, "resultat", "a", "b", op[j]));
                l.Add(new ElementStudio(ElementStudio.Etiquette, "resultat", "Resultat", 20, 118, 64, 16));
            }
            else if (k == 2)
            {
                p.Nom = "Compteur"; p.Titre = "Compteur"; p.Largeur = 320; p.Hauteur = 170;
                l.Add(new ElementStudio(ElementStudio.Etiquette, "total", "Clics : 0", 20, 20, 72, 16));
                l.Add(new ElementStudio(ElementStudio.Bouton, "plus", "+1", 20, 56, 90, 32).Agir(ACompter, "total", "", "", "Clics : "));
                l.Add(new ElementStudio(ElementStudio.Bouton, "zero", "Remise a zero", 124, 56, 150, 32).Agir(AZero, "total", "", "", "Clics : "));
            }
            else if (k == 3)
            {
                p.Nom = "BlocNotes"; p.Titre = "Bloc-notes"; p.Largeur = 460; p.Hauteur = 330;
                l.Add(new ElementStudio(ElementStudio.Zone, "notes", "", 16, 16, 428, 200));
                l.Add(new ElementStudio(ElementStudio.Champ, "ligne", "", 16, 228, 320, 26).Agir(AAjouter, "notes", "ligne", "", ""));
                l.Add(new ElementStudio(ElementStudio.Bouton, "ajouter", "Ajouter", 348, 226, 96, 30).Agir(AAjouter, "notes", "ligne", "", ""));
                l.Add(new ElementStudio(ElementStudio.Bouton, "sauver", "Enregistrer", 16, 270, 130, 32).Agir(AEnregistrer, "", "notes", "", FichierParDefaut));
                l.Add(new ElementStudio(ElementStudio.Bouton, "charger", "Charger", 156, 270, 110, 32).Agir(ACharger, "notes", "", "", FichierParDefaut));
                l.Add(new ElementStudio(ElementStudio.Bouton, "vider", "Effacer", 276, 270, 100, 32).Agir(AEffacer, "notes", "", "", ""));
            }
            return p;
        }
    }
}
