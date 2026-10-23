using Cosmos.System;
using FileSystemOS.Systeme;

namespace FileSystemOS.Graphique
{
    /// <summary>Gestion des comptes : creation et suppression (admin), changement de mot de passe.</summary>
    public class AccountsWindow : Window
    {
        private const int ListY = 40, RowH = 24, MaxRows = 5;
        private const int NewY = ListY + MaxRows * RowH + 44;     // section "Nouveau compte"
        private const int PwdY = NewY + 130;                       // section "Mon mot de passe"

        private readonly TextField newName, newPass, oldPwd, newPwd;
        private readonly Button btnCreate, btnDelete, btnChange;
        private bool newAdmin;
        private int selected = -1, focus;
        private string status = "";

        public AccountsWindow(int x, int y) : base("Comptes", x, y, 520, 560, Icons.AccountIcon)
        {
            newName = new TextField(130, NewY + 24, 200, false);
            newPass = new TextField(130, NewY + 54, 200, true);
            oldPwd = new TextField(130, PwdY + 24, 200, true);
            newPwd = new TextField(130, PwdY + 54, 200, true);
            btnCreate = new Button(Glyphs.Plus, "Creer", 340, NewY + 54, 110, 26, Theme.Green, Theme.GreenHover);
            btnDelete = new Button(Glyphs.Trash, "Supprimer", 340, ListY, 120, 26, Theme.Red, Theme.RedHover);
            btnChange = new Button(Glyphs.Check, "Changer", 340, PwdY + 54, 110, 26, Theme.Blue, Theme.BlueHover);
        }

        private bool IsAdmin => Comptes.Actuel != null && Comptes.Actuel.Admin;
        private TextField[] Fields => new TextField[] { newName, newPass, oldPwd, newPwd };

        public override void OnOpen()
        {
            Comptes.Load();
            if (Comptes.Actuel != null) Comptes.Actuel = Comptes.Find(Comptes.Actuel.Nom) ?? Comptes.Actuel;
            selected = -1;
            status = "Connecte : " + (Comptes.Actuel == null ? "?" : Comptes.Actuel.Nom);
            focus = IsAdmin ? 0 : 2;
        }

        protected override void DrawContent(PixelBuffer b, int cx, int cy, int cw, int ch)
        {
            b.DrawText("Comptes", Theme.Text, 20, cy + 12);
            for (int i = 0; i < Comptes.Liste.Count && i < MaxRows; i++)
            {
                int y = cy + ListY + i * RowH;
                if (i == selected) b.FillRect(Theme.RowSelected, 16, y, 310, RowH - 2);
                Compte c = Comptes.Liste[i];
                b.DrawText(c.Nom + (c.Admin ? "  (admin)" : "") + (c == Comptes.Actuel ? "  - connecte" : ""), Theme.Text, 24, y + 3);
            }
            if (IsAdmin) btnDelete.Draw(b, 0, cy);

            if (IsAdmin)
            {
                b.FillRect(Theme.Separator, 16, cy + NewY - 8, W - 32, 1);
                b.DrawText("Nouveau compte", Theme.Text, 20, cy + NewY);
                b.DrawText("Nom", Theme.TextMuted, 60, cy + newName.RY + 4);
                b.DrawText("Mot de passe", Theme.TextMuted, 20, cy + newPass.RY + 4);
                DrawField(b, newName, 0, cy); DrawField(b, newPass, 1, cy);
                b.DrawRect(Theme.TextMuted, 130, cy + NewY + 88, 16, 16);
                if (newAdmin) Glyphs.Check.DrawTinted(b, 133, cy + NewY + 91, Theme.TitleActive);
                b.DrawText("Administrateur", Theme.Text, 154, cy + NewY + 88);
                btnCreate.Draw(b, 0, cy);
            }

            b.FillRect(Theme.Separator, 16, cy + PwdY - 8, W - 32, 1);
            b.DrawText("Mon mot de passe", Theme.Text, 20, cy + PwdY);
            b.DrawText("Ancien", Theme.TextMuted, 60, cy + oldPwd.RY + 4);
            b.DrawText("Nouveau", Theme.TextMuted, 52, cy + newPwd.RY + 4);
            DrawField(b, oldPwd, 2, cy); DrawField(b, newPwd, 3, cy);
            btnChange.Draw(b, 0, cy);

            // Journal des connexions (3 dernieres lignes)
            b.FillRect(Theme.Separator, 16, H - 92, W - 32, 1);
            b.DrawText("Dernieres connexions (" + FileSystemOS.Systeme.Journal.Support() + ")", Theme.Text, 20, H - 86);
            var jl = FileSystemOS.Systeme.Journal.Dernieres(3);
            for (int i = 0; i < jl.Count; i++)
            {
                string l = jl[i];
                int max = (W - 40) / 8;
                if (l.Length > max) l = l.Substring(0, max);
                b.DrawText(l, Theme.TextMuted, 24, H - 68 + i * 16);
            }

            string s = status.Length > (W - 30) / 8 ? status.Substring(0, (W - 30) / 8) : status;
            b.DrawText(s, s.StartsWith("Erreur") ? Theme.Red : Theme.TextMuted, 20, H - 26);
        }

        private void DrawField(PixelBuffer b, TextField f, int index, int cy)
        {
            int ry = f.RY;
            f.RY = ry + cy;
            f.Draw(b, focus == index, true);
            f.RY = ry;
        }

        public override void OnKey(KeyEvent k)
        {
            if (k.Key == ConsoleKeyEx.Tab) { focus = IsAdmin ? (focus + 1) % 4 : (focus == 2 ? 3 : 2); return; }
            if (k.Key == ConsoleKeyEx.Enter) { if (focus < 2) Create(); else Change(); return; }
            Fields[focus].Key(k);
        }

        private void Create()
        {
            if (!IsAdmin) return;
            string err = Comptes.Create(newName.Text, newPass.Text, newAdmin);
            status = err == null ? "Compte '" + newName.Text + "' cree" : "Erreur : " + err;
            if (err == null) { newName.Text = ""; newPass.Text = ""; newAdmin = false; }
            Live.SaveIfActive();
        }

        private void Change()
        {
            if (Comptes.Actuel == null) return;
            string err = Comptes.ChangePassword(Comptes.Actuel, oldPwd.Text, newPwd.Text);
            status = err == null ? "Mot de passe change" : "Erreur : " + err;
            oldPwd.Text = ""; newPwd.Text = "";
            Live.SaveIfActive();
        }

        private bool HoverBtn(Button bt, int lx, int ly)
        {
            bool h = lx >= 0 && bt.Hit(0, 0, lx, ly);
            if (h == bt.Hover) return false;
            bt.Hover = h;
            return true;
        }

        public override void OnMouseMove(int mx, int my)
        {
            int lx = mx < 0 ? -1 : mx - X, ly = my - Y - TitleH;
            if (HoverBtn(btnCreate, lx, ly) | HoverBtn(btnDelete, lx, ly) | HoverBtn(btnChange, lx, ly)) Dirty = true;
        }

        public override void OnMouseDown(int mx, int my)
        {
            int lx = mx - X, ly = my - Y - TitleH;
            btnCreate.Pressed = btnCreate.Hit(0, 0, lx, ly);
            btnDelete.Pressed = btnDelete.Hit(0, 0, lx, ly);
            btnChange.Pressed = btnChange.Hit(0, 0, lx, ly);

            var f = Fields;
            for (int i = 0; i < f.Length; i++) if (f[i].Hit(lx, ly) && (i >= 2 || IsAdmin)) focus = i;
            if (IsAdmin && lx >= 130 && lx < 300 && ly >= NewY + 86 && ly < NewY + 106) newAdmin = !newAdmin;
            if (lx >= 16 && lx < 326 && ly >= ListY && ly < ListY + MaxRows * RowH)
            {
                int i = (ly - ListY) / RowH;
                selected = i < Comptes.Liste.Count ? i : -1;
            }
        }

        public override void OnMouseUp(int mx, int my)
        {
            int lx = mx - X, ly = my - Y - TitleH;
            if (btnCreate.Pressed && btnCreate.Hit(0, 0, lx, ly)) Create();
            if (btnChange.Pressed && btnChange.Hit(0, 0, lx, ly)) Change();
            if (btnDelete.Pressed && btnDelete.Hit(0, 0, lx, ly) && IsAdmin)
            {
                if (selected < 0) status = "Selectionnez un compte a supprimer";
                else
                {
                    string n = Comptes.Liste[selected].Nom;
                    string err = Comptes.Delete(n);
                    status = err == null ? "Compte '" + n + "' supprime" : "Erreur : " + err;
                    selected = -1;
                    Live.SaveIfActive();
                }
            }
            btnCreate.Pressed = btnDelete.Pressed = btnChange.Pressed = false;
        }
    }
}
