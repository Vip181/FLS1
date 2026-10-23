using System;
using Cosmos.System;

namespace FileSystemOS.Graphique
{
    /// <summary>
    /// CALCULATRICE : quatre operations, pourcentage, racine carree, memoire (M+, MR, MC).
    /// Les nombres sont geres en virgule fixe (3 decimales) : Cosmos n'a pas de calcul
    /// flottant fiable, donc tout est fait avec des entiers, multiplies par 1000.
    /// </summary>
    public class CalculatriceWindow : Window
    {
        private const int Mille = 1000;
        private const int CW = 72, CH = 46, Marge = 14, Ecran = 62;

        // Tableau A PLAT (4 colonnes) : IL2CPU ne compile pas les tableaux a deux dimensions.
        private static readonly string[] Touches =
        {
            "MC", "MR", "M+", "C",
            "7",  "8",  "9",  "/",
            "4",  "5",  "6",  "*",
            "1",  "2",  "3",  "-",
            "0",  ",",  "=",  "+",
            "%",  "V",  "+/-", "<"
        };

        private long affichage;        // valeur courante x1000
        private long memoire, gauche;
        private char operation;
        private bool nouvelleSaisie = true, virgule;
        private int decimales;
        private string erreur = "";
        private int survol = -1, presse = -1;

        // La taille est calculee AVANT d'appeler la classe de base : c'est elle qui cree le
        // buffer de pixels. (Avant, la hauteur valait 0 a ce moment-la : fenetre transparente.)
        private const int Largeur = Marge * 2 + CW * 4 + 12;
        private const int Hauteur = TitleH + Ecran + Marge + CH * 6 + Marge;

        public CalculatriceWindow(int x, int y)
            : base("Calculatrice", x, y, Largeur, Hauteur, Icons.CalcIcon) { }


        private int GrilleY => TitleH + Ecran + Marge;

        private int ToucheA(int lx, int ly)
        {
            int c = (lx - Marge) / CW, l = (ly - GrilleY) / CH;
            if (lx < Marge || c < 0 || c > 3 || l < 0 || l > 5) return -1;
            return l * 4 + c;
        }

        // ---- Affichage du nombre ----
        private string Texte(long v)
        {
            bool neg = v < 0;
            if (neg) v = -v;
            long ent = v / Mille, dec = v % Mille;
            string s = ent.ToString();
            if (dec > 0 || virgule)
            {
                string d = dec.ToString();
                while (d.Length < 3) d = "0" + d;                       // 5 -> "005"
                while (d.Length > 1 && d[d.Length - 1] == '0' && !virgule) d = d.Substring(0, d.Length - 1);
                s += "," + (virgule && dec == 0 ? "" : d);
            }
            return (neg ? "-" : "") + s;
        }

        protected override void DrawContent(PixelBuffer b, int cx, int cy, int cw, int ch)
        {
            // Ecran
            b.FillRect(Theme.ConsoleBg, Marge, cy + 10, W - Marge * 2, Ecran - 20);
            string t = erreur.Length > 0 ? erreur : Texte(affichage);
            int max = (W - Marge * 2 - 16) / 8;
            if (t.Length > max) t = t.Substring(0, max);
            b.DrawText(t, erreur.Length > 0 ? Theme.ConsoleError : Theme.ConsoleInput,
                       W - Marge - 8 - t.Length * 8, cy + 10 + (Ecran - 20 - 16) / 2);
            if (memoire != 0) b.DrawText("M", Theme.ConsolePrompt, Marge + 8, cy + 16);
            if (operation != '\0') b.DrawText(operation.ToString(), Theme.ConsoleEcho, Marge + 8, cy + 32);

            // Touches
            for (int l = 0; l < 6; l++)
                for (int c = 0; c < 4; c++)
                {
                    int i = l * 4 + c;
                    int x = Marge + c * CW, y = GrilleY - TitleH + cy + l * CH;
                    string s = Touches[i];
                    bool op = c == 3 || s == "=" || s == "%" || s == "V" || s == "+/-";
                    var fond = i == presse ? Theme.TitleActive : (i == survol ? Theme.RowHover
                              : (op ? Theme.HeaderBg : Theme.WindowBg));
                    b.FillRoundRect(fond, x + 3, y + 3, CW - 6, CH - 6, 8);
                    b.DrawRect(Theme.Separator, x + 3, y + 3, CW - 6, CH - 6);
                    string aff = s == "V" ? "sqrt" : s;
                    b.DrawText(aff, i == presse ? Theme.TitleText : Theme.Text,
                               x + (CW - aff.Length * 8) / 2, y + (CH - 16) / 2);
                }
        }

        // ---- Calcul ----
        private void Chiffre(int d)
        {
            erreur = "";
            if (nouvelleSaisie) { affichage = 0; nouvelleSaisie = false; virgule = false; decimales = 0; }
            if (!virgule) affichage = affichage * 10 + d * Mille;
            else if (decimales < 3)
            {
                decimales++;
                long pas = Mille;
                for (int i = 0; i < decimales; i++) pas /= 10;
                affichage += (affichage < 0 ? -d : d) * pas;
            }
        }

        private void Appliquer()
        {
            if (operation == '\0') { gauche = affichage; return; }
            long r = gauche;
            switch (operation)
            {
                case '+': r = gauche + affichage; break;
                case '-': r = gauche - affichage; break;
                case '*': r = gauche * affichage / Mille; break;
                case '/':
                    if (affichage == 0) { erreur = "Division par zero"; operation = '\0'; nouvelleSaisie = true; return; }
                    r = gauche * Mille / affichage;
                    break;
            }
            affichage = r;
            gauche = r;
        }

        private void Racine()
        {
            if (affichage < 0) { erreur = "Racine d'un nombre negatif"; return; }
            // Racine carree par la methode de Newton, en virgule fixe
            long v = affichage * Mille;          // on travaille en millionieme
            long x = v > 0 ? v : 1;
            for (int i = 0; i < 40 && x > 0; i++) x = (x + v / x) / 2;
            affichage = x;
            nouvelleSaisie = true;
        }

        private void Touche(string s)
        {
            if (s.Length == 1 && s[0] >= '0' && s[0] <= '9') { Chiffre(s[0] - '0'); return; }
            switch (s)
            {
                case ",": if (!virgule) { if (nouvelleSaisie) { affichage = 0; nouvelleSaisie = false; } virgule = true; decimales = 0; } break;
                case "C": affichage = 0; gauche = 0; operation = '\0'; nouvelleSaisie = true; virgule = false; erreur = ""; break;
                case "<":
                    if (!nouvelleSaisie)
                    {
                        if (virgule && decimales > 0) { decimales--; long pas = Mille; for (int i = 0; i <= decimales; i++) pas /= 10; affichage -= affichage % (pas * 10); }
                        else { virgule = false; affichage = affichage / (10 * Mille) * (10 * Mille); }
                    }
                    break;
                case "+": case "-": case "*": case "/":
                    Appliquer();
                    operation = s[0];
                    nouvelleSaisie = true; virgule = false;
                    break;
                case "=":
                    Appliquer();
                    operation = '\0';
                    nouvelleSaisie = true; virgule = false;
                    break;
                case "%": affichage = affichage / 100; nouvelleSaisie = true; break;
                case "V": Racine(); break;
                case "+/-": affichage = -affichage; break;
                case "MC": memoire = 0; break;
                case "MR": affichage = memoire; nouvelleSaisie = true; break;
                case "M+": memoire += affichage; nouvelleSaisie = true; break;
            }
        }

        public override void OnKey(KeyEvent k)
        {
            char c = k.KeyChar;
            if (c >= '0' && c <= '9') { Touche(c.ToString()); Dirty = true; return; }
            if (c == '.' || c == ',') { Touche(","); Dirty = true; return; }
            if (c == '+' || c == '-' || c == '*' || c == '/') { Touche(c.ToString()); Dirty = true; return; }
            if (c == '=' || k.Key == ConsoleKeyEx.Enter) { Touche("="); Dirty = true; return; }
            if (k.Key == ConsoleKeyEx.Backspace) { Touche("<"); Dirty = true; return; }
            if (k.Key == ConsoleKeyEx.Escape || c == 'c' || c == 'C') { Touche("C"); Dirty = true; }
        }

        public override void OnMouseMove(int mx, int my)
        {
            int s = mx < 0 ? -1 : ToucheA(mx - X, my - Y);
            if (s != survol) { survol = s; Dirty = true; }
        }

        public override void OnMouseDown(int mx, int my)
        {
            presse = ToucheA(mx - X, my - Y);
            Dirty = true;
        }

        public override void OnMouseUp(int mx, int my)
        {
            int i = ToucheA(mx - X, my - Y);
            if (i >= 0 && i == presse) Touche(Touches[i]);
            presse = -1;
            Dirty = true;
        }
    }
}
