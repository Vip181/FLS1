using System.Drawing;
using Cosmos.System;
using FileSystemOS.Demarrage;
using FileSystemOS.Systeme;

namespace FileSystemOS.Graphique
{
    /// <summary>
    /// Ecran de connexion. Au tout premier demarrage : creation du compte administrateur.
    /// Ensuite : choix du compte + mot de passe. Ne peut pas etre fermee ni reduite.
    /// </summary>
    public class LoginWindow : Window
    {
        private const int ListY = 150, RowH = 26, MaxRows = 5;
        private readonly TextField name, pass, confirm;
        private readonly Button btn;
        private int focus, selected;
        private string message = "";
        private uint frame;
        private bool setup;

        public LoginWindow(int x, int y) : base("Connexion", x, y, 440, 400, Icons.LockIcon)
        {
            Fixed = true;
            name = new TextField(120, 0, 280, false);
            pass = new TextField(120, 0, 280, true);
            confirm = new TextField(120, 0, 280, true);
            btn = new Button(Glyphs.Check, "Se connecter", 120, 0, 180, 30, Theme.Blue, Theme.BlueHover);
        }

        protected override Color ContentBg => BootScreen.Bg;

        public override void OnOpen()
        {
            Comptes.Load();
            setup = Comptes.Liste.Count == 0;
            name.Text = pass.Text = confirm.Text = "";
            message = setup ? "Premier demarrage : creez le compte administrateur." : "";
            selected = 0;
            if (Comptes.Actuel != null)
                for (int i = 0; i < Comptes.Liste.Count; i++) if (Comptes.Liste[i] == Comptes.Actuel) selected = i;
            focus = setup ? 0 : 1;
            Layout();
        }

        private void Layout()
        {
            if (setup)
            {
                name.RY = ListY; pass.RY = ListY + 40; confirm.RY = ListY + 80;
                btn.RY = ListY + 124; btn.Label = "Creer le compte";
            }
            else
            {
                int rows = Comptes.Liste.Count < MaxRows ? Comptes.Liste.Count : MaxRows;
                pass.RY = ListY + rows * RowH + 14;
                btn.RY = pass.RY + 40; btn.Label = "Se connecter";
            }
        }

        protected override void DrawContent(PixelBuffer b, int cx, int cy, int cw, int ch)
        {
            VLogo.Draw(b, W / 2, TitleH + 52, 64, frame, BootScreen.Bg);
            Color light = Color.FromArgb(205, 208, 220), muted = Color.FromArgb(120, 124, 142);
            string t = "Bienvenue sur " + Services.OsName + " " + Services.Version;
            b.DrawText(t, light, (W - t.Length * 8) / 2, TitleH + 100);
            bool caret = (frame / 20) % 2 == 0;

            if (setup)
            {
                b.DrawText("Nom", muted, 30, name.RY + 4);
                b.DrawText("Mot de passe", muted, 14, pass.RY + 4);
                b.DrawText("Confirmer", muted, 30, confirm.RY + 4);
                name.Draw(b, focus == 0, caret);
                pass.Draw(b, focus == 1, caret);
                confirm.Draw(b, focus == 2, caret);
            }
            else
            {
                for (int i = 0; i < Comptes.Liste.Count && i < MaxRows; i++)
                {
                    int y = ListY + i * RowH;
                    if (i == selected) b.FillRoundRect(Theme.ChipHover, 120, y, 280, RowH - 4, 6);
                    Compte c = Comptes.Liste[i];
                    b.DrawText(c.Nom + (c.Admin ? "  (admin)" : ""), light, 132, y + 3);
                }
                b.DrawText("Utilisateur", muted, 22, ListY + 3);
                b.DrawText("Mot de passe", muted, 14, pass.RY + 4);
                pass.Draw(b, true, caret);
            }
            btn.Draw(b, 0, 0);
            if (message.Length > 0)
            {
                string m = message.Length > (W - 20) / 8 ? message.Substring(0, (W - 20) / 8) : message;
                b.DrawText(m, message.StartsWith("Premier") ? light : Theme.ConsoleError, (W - m.Length * 8) / 2, btn.RY + 44);
            }
        }

        public override void Tick()
        {
            frame++;
            if ((frame & 1) == 0 && !Dirty && !Hidden)
            {
                VLogo.Draw(Buffer, W / 2, TitleH + 52, 64, frame, BootScreen.Bg);
                Services.ScreenDirty = true;
            }
            if (frame % 20 == 0) Dirty = true;   // curseur clignotant
        }

        private void Submit()
        {
            if (setup)
            {
                if (pass.Text != confirm.Text) { message = "Les mots de passe ne correspondent pas"; return; }
                string err = Comptes.Create(name.Text, pass.Text, true);
                if (err != null) { message = err; return; }
                FileSystemOS.Systeme.Journal.Ajouter("creation administrateur", name.Text);
                Session.Start(Comptes.Find(name.Text));
                return;
            }
            if (Comptes.Liste.Count == 0) return;
            Compte c = Comptes.Check(Comptes.Liste[selected].Nom, pass.Text);
            pass.Text = "";
            if (c == null)
            {
                message = "Mot de passe incorrect";
                FileSystemOS.Systeme.Journal.Ajouter("echec de connexion", Comptes.Liste[selected].Nom);
                return;
            }
            Session.Start(c);
        }

        public override void OnKey(KeyEvent k)
        {
            if (k.Key == ConsoleKeyEx.Enter) { Submit(); return; }
            if (k.Key == ConsoleKeyEx.Tab) { focus = setup ? (focus + 1) % 3 : 1; return; }
            if (!setup && (k.Key == ConsoleKeyEx.UpArrow || k.Key == ConsoleKeyEx.DownArrow))
            {
                int n = Comptes.Liste.Count < MaxRows ? Comptes.Liste.Count : MaxRows;
                if (n > 0) selected = (selected + (k.Key == ConsoleKeyEx.UpArrow ? n - 1 : 1)) % n;
                return;
            }
            TextField f = setup ? (focus == 0 ? name : (focus == 1 ? pass : confirm)) : pass;
            f.Key(k);
        }

        public override void OnMouseMove(int mx, int my)
        {
            bool h = mx >= 0 && btn.Hit(X, Y, mx, my);
            if (h != btn.Hover) { btn.Hover = h; Dirty = true; }
        }

        public override void OnMouseDown(int mx, int my)
        {
            int lx = mx - X, ly = my - Y;
            btn.Pressed = btn.Hit(0, 0, lx, ly);
            if (setup)
            {
                if (name.Hit(lx, ly)) focus = 0;
                else if (pass.Hit(lx, ly)) focus = 1;
                else if (confirm.Hit(lx, ly)) focus = 2;
            }
            else if (lx >= 120 && lx < 400 && ly >= ListY && ly < ListY + MaxRows * RowH)
            {
                int i = (ly - ListY) / RowH;
                if (i < Comptes.Liste.Count) { selected = i; message = ""; }
            }
        }

        public override void OnMouseUp(int mx, int my)
        {
            if (btn.Pressed && btn.Hit(0, 0, mx - X, my - Y)) Submit();
            btn.Pressed = false;
        }
    }
}
