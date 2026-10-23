using System;
using System.Collections.Generic;
using System.Drawing;
using Cosmos.System;
using FileSystemOS.Pilotes;
using FileSystemOS.Utils;
using FileSystemOS.Systeme;

namespace FileSystemOS.Graphique
{
    /// <summary>
    /// Fenetre Reseau : toutes les cartes detectees (Ethernet et Wi-Fi) avec leur pilote,
    /// connexion a la box (DHCP), test d'internet, et liste de TOUS les peripheriques PCI.
    /// </summary>
    public class ReseauWindow : Window
    {
        private const int ToolH = 36, StatusH = 22, LineH = 18, Pad = 12;
        private readonly Button btnConnect, btnTest, btnDevices, btnExp;
        private readonly ScrollBar sb = new ScrollBar();
        private readonly List<string> lines = new List<string>();
        private readonly List<Color> colors = new List<Color>();
        private bool showAll;
        private int pendingAction, delay;    // 1 = connexion, 2 = test
        private string status = "";

        public ReseauWindow(int x, int y) : base("Reseau", x, y, 680, 460, Icons.NetworkIcon)
        {
            btnConnect = new Button(Glyphs.Refresh, "Se connecter", 8, 5, 150, 26, Theme.Green, Theme.GreenHover);
            btnTest = new Button(Glyphs.Play, "Tester internet", 164, 5, 170, 26, Theme.Blue, Theme.BlueHover);
            btnDevices = new Button(Glyphs.Plus, "Peripheriques", 340, 5, 160, 26, Theme.Blue, Theme.BlueHover);
            btnExp = new Button(Glyphs.Play, "Essayer le pilote I219", 506, 5, 250, 26, Theme.Red, Theme.RedHover);
        }

        protected override Color ContentBg => Theme.PaperBg;
        private int TextY => TitleH + ToolH;
        private int TextH => H - TextY - StatusH;
        private int SbX => W - ScrollBar.Width - 4;

        public override void OnOpen()
        {
            if (PciCatalogue.Tous.Count == 0) PciCatalogue.Scan();
            Reseau.Refresh();
            Build();
        }

        private void Add(string t, Color c) { lines.Add(t); colors.Add(c); }

        private void Build()
        {
            lines.Clear(); colors.Clear();
            int cols = (SbX - Pad * 2) / 8;

            Add("ORDINATEUR RECONNU", Theme.TitleActive);
            var m = Machine.Report();
            for (int i = 0; i < m.Count; i++) Add("  " + m[i], Theme.Text);
            Add("", Theme.Text);

            Add("CONNEXION", Theme.TitleActive);
            Add("Etat : " + Reseau.Status, Reseau.Connected ? Theme.Green : Theme.Text);
            Add("Adresse IP : " + Reseau.Ip + "    Box (passerelle) : " + Reseau.Gateway + "    DNS : " + Reseau.Dns, Theme.Text);
            Add("", Theme.Text);

            var cards = PciCatalogue.Reseau();
            Add("CARTES RESEAU DETECTEES (" + cards.Count + ")", Theme.TitleActive);
            if (cards.Count == 0) Add("  Aucune carte reseau sur le bus PCI.", Theme.TextMuted);
            bool wifi = false;
            for (int i = 0; i < cards.Count; i++)
            {
                Peripherique c = cards[i];
                if (c.Wifi) wifi = true;
                Add("  " + (c.Wifi ? "[Wi-Fi]    " : "[Ethernet] ") + c.Name, Theme.Text);
                Add("     " + c.VendorName + "  id " + c.Id + "  emplacement " + c.Location, Theme.TextMuted);
                Add("     " + c.Driver, c.Supported ? Theme.Green : Theme.Red);
            }
            for (int i = 0; i < E1000.Cards.Count; i++)
                Add("  E1000 actif : MAC " + E1000.Cards[i].MacText() + (E1000.Cards[i].LinkUp ? ", cable branche" : ", pas de lien (cable ?)"), Theme.TextMuted);
            Add("", Theme.Text);

            Add("PILOTES : " + PiloteReseau.Journal, Theme.TitleActive);
            Add("  Un pilote depend de la PUCE reseau, pas de la marque du PC.", Theme.TextMuted);
            Add("  Les puces sans pilote ne sont jamais demarrees (securite).", Theme.TextMuted);
            Add("", Theme.Text);

            Add("BOX WI-FI DISPONIBLES", Theme.TitleActive);
            var box = FileSystemOS.Pilotes.Wifi.Rechercher();
            for (int i = 0; i < box.Count; i++) Add("  " + box[i], Theme.Text);
            Add("", Theme.Text);

            Add("WI-FI ET MOT DE PASSE DE LA BOX", Theme.TitleActive);
            Add("  Carte Ethernet Intel I219 : un pilote EXPERIMENTAL existe. Branchez un cable", Theme.Text);
            Add("  sur la box, puis cliquez sur \"Essayer le pilote I219\" (bouton rouge).", Theme.Text);
            Add("  En cas de blocage, redemarrez : il reste desactive tant qu'on ne l'active pas.", Theme.TextMuted);
            Add("", Theme.Text);

            string[] info =
            {
                wifi ? "Une carte Wi-Fi est detectee, mais File System OS ne peut pas encore l'utiliser :"
                     : "Aucune carte Wi-Fi utilisable.",
                "chaque puce Wi-Fi demande son propre pilote (et un micrologiciel fourni par le fabricant),",
                "plus le chiffrement WPA2 de la box. Ce n'est pas encore disponible.",
                "SOLUTION : reliez l'ordinateur a la box avec un CABLE Ethernet, puis cliquez sur",
                "\"Se connecter\". Par cable, la box donne l'adresse IP toute seule : aucun mot de passe.",
                "Dans VirtualBox / VMware, la carte de la machine virtuelle est vue comme un cable."
            };
            for (int i = 0; i < info.Length; i++) Add("  " + info[i], Theme.Text);

            if (showAll)
            {
                Add("", Theme.Text);
                Add("TOUS LES PERIPHERIQUES PCI (" + PciCatalogue.Tous.Count + ")", Theme.TitleActive);
                for (int i = 0; i < PciCatalogue.Tous.Count; i++)
                {
                    Peripherique p = PciCatalogue.Tous[i];
                    Add("  " + p.Location + "  [" + p.Id + "]  " + p.VendorName + " - " + p.Kind +
                        (p.Network ? " : " + p.Name : ""), p.Network ? Theme.Blue : Theme.Text);
                }
            }

            for (int i = 0; i < lines.Count; i++)
                if (lines[i].Length > cols) lines[i] = lines[i].Substring(0, cols);
            Dirty = true;
        }

        public override void Tick()
        {
            if (pendingAction == 0 || Hidden) return;
            if (--delay > 0) return;
            int a = pendingAction;
            pendingAction = 0;
            if (a == 3)
            {
                E1000.Experimental = true;
                Config.SetAndSave("reseau.cfg", "pilote_i219", "oui");
                PciCatalogue.Scan();
                status = "Pilote I219 : " + Reseau.Init(Config.GetBool("dhcp", true));
            }
            else if (a == 1) status = "Connexion : " + Reseau.Init(Config.GetBool("dhcp", true));
            else
            {
                try
                {
                    byte[] page = Http.Get("http://frogfind.com/", out string f, out string t);
                    status = "Internet fonctionne : " + page.Length + " octets recus de frogfind.com";
                }
                catch (Exception e) { status = "Erreur : " + e.Message; }
            }
            Build();
        }

        protected override void DrawContent(PixelBuffer b, int cx, int cy, int cw, int ch)
        {
            b.FillRect(Theme.ToolbarBg, 0, cy, W, ToolH);
            btnConnect.Draw(b, 0, cy);
            btnTest.Draw(b, 0, cy);
            btnDevices.Draw(b, 0, cy);
            btnExp.Draw(b, 0, cy);

            sb.Visible = TextH / LineH;
            sb.Total = lines.Count;
            sb.Clamp();
            for (int i = 0; i < sb.Visible; i++)
            {
                int idx = sb.Value + i;
                if (idx >= lines.Count) break;
                if (lines[idx].Length > 0) b.DrawText(lines[idx], colors[idx], Pad, TextY + 6 + i * LineH);
            }
            sb.Draw(b, SbX, TextY, TextH);

            b.FillRect(Theme.StatusBg, 0, H - StatusH, W, StatusH);
            string st = status.Length > (W - 20) / 8 ? status.Substring(0, (W - 20) / 8) : status;
            b.DrawText(st, st.StartsWith("Erreur") ? Theme.Red : Theme.TextMuted, 10, H - StatusH + 3);
        }

        public override void OnKey(KeyEvent k)
        {
            if (k.Key == ConsoleKeyEx.UpArrow) sb.By(-1);
            else if (k.Key == ConsoleKeyEx.DownArrow) sb.By(1);
            else if (k.Key == ConsoleKeyEx.PageUp) sb.By(-sb.Visible);
            else if (k.Key == ConsoleKeyEx.PageDown) sb.By(sb.Visible);
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
            if (HoverBtn(btnConnect, lx, ly) | HoverBtn(btnTest, lx, ly) | HoverBtn(btnDevices, lx, ly) |
                HoverBtn(btnExp, lx, ly)) Dirty = true;
        }

        public override void OnMouseDown(int mx, int my)
        {
            int lx = mx - X, ly = my - Y - TitleH;
            if (sb.MouseDown(lx, my - Y, SbX, TextY, TextH)) return;
            btnConnect.Pressed = btnConnect.Hit(0, 0, lx, ly);
            btnTest.Pressed = btnTest.Hit(0, 0, lx, ly);
            btnDevices.Pressed = btnDevices.Hit(0, 0, lx, ly);
            btnExp.Pressed = btnExp.Hit(0, 0, lx, ly);
        }

        public override void OnMouseDrag(int mx, int my) => sb.MouseDrag(my - Y, TextY, TextH);

        public override void OnMouseUp(int mx, int my)
        {
            int lx = mx - X, ly = my - Y - TitleH;
            if (btnConnect.Pressed && btnConnect.Hit(0, 0, lx, ly)) { pendingAction = 1; delay = 3; status = "Connexion a la box..."; }
            if (btnTest.Pressed && btnTest.Hit(0, 0, lx, ly)) { pendingAction = 2; delay = 3; status = "Test de frogfind.com..."; }
            if (btnDevices.Pressed && btnDevices.Hit(0, 0, lx, ly)) { showAll = !showAll; Build(); }
            if (btnExp.Pressed && btnExp.Hit(0, 0, lx, ly)) { pendingAction = 3; delay = 3; status = "Activation du pilote experimental I219..."; }
            btnConnect.Pressed = btnTest.Pressed = btnDevices.Pressed = btnExp.Pressed = false;
            sb.MouseUp();
        }
    }
}
