using Cosmos.System;

namespace FileSystemOS.Graphique
{
    /// <summary>Champ de saisie d'une ligne (texte ou mot de passe masque), coordonnees locales.</summary>
    public class TextField
    {
        public int RX, RY, W;
        public string Text = "";
        public bool Password;
        public int Max = 32;
        public const int H = 24;

        public TextField(int rx, int ry, int w, bool password)
        {
            RX = rx; RY = ry; W = w; Password = password;
        }

        public bool Hit(int lx, int ly) => lx >= RX && lx < RX + W && ly >= RY && ly < RY + H;

        public void Draw(PixelBuffer b, bool focused, bool caret)
        {
            b.FillRect(Theme.PathBg, RX, RY, W, H);
            b.DrawRect(focused ? Theme.TitleActive : Theme.PathBorder, RX, RY, W, H);
            string s = Password ? new string('*', Text.Length) : Text;
            int max = (W - 16) / 8;
            if (s.Length > max) s = s.Substring(s.Length - max);
            b.DrawText(s, Theme.Text, RX + 6, RY + 4);
            if (focused && caret) b.FillRect(Theme.Text, RX + 6 + s.Length * 8, RY + 4, 2, 16);
        }

        /// <summary>Renvoie vrai si la touche a ete utilisee.</summary>
        public bool Key(KeyEvent k)
        {
            if (k.Key == ConsoleKeyEx.Backspace)
            {
                if (Text.Length > 0) Text = Text.Substring(0, Text.Length - 1);
                return true;
            }
            char c = k.KeyChar;
            if (c >= ' ' && c <= '~' && Text.Length < Max) { Text += c; return true; }
            return false;
        }
    }
}
