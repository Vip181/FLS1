using System.Drawing;

namespace FileSystemOS.Graphique
{
    public static class Theme
    {
        // Bureau
        public static readonly Color DesktopTop = Color.FromArgb(24, 28, 52);
        public static readonly Color DesktopBottom = Color.FromArgb(76, 44, 100);
        public static readonly Color Taskbar = Color.FromArgb(16, 18, 28);
        public static readonly Color TaskbarText = Color.FromArgb(210, 214, 230);
        public static readonly Color TaskbarHover = Color.FromArgb(44, 48, 72);
        public static readonly Color TaskbarSep = Color.FromArgb(60, 64, 88);
        public static readonly Color TabNormal = Color.FromArgb(40, 43, 62);
        public static readonly Color TabHover = Color.FromArgb(56, 60, 88);
        public static readonly Color TabActive = Color.FromArgb(72, 82, 190);
        public static readonly Color TabMinimized = Color.FromArgb(26, 28, 40);
        public static readonly Color TabTextMuted = Color.FromArgb(120, 124, 150);
        public static readonly Color MenuBg = Color.FromArgb(28, 30, 44);
        public static readonly Color MenuHover = Color.FromArgb(52, 58, 100);
        public static readonly Color MenuBorder = Color.FromArgb(70, 76, 120);

        // Fenetres
        public static readonly Color Shadow = Color.FromArgb(10, 10, 20);
        public static readonly Color Border = Color.FromArgb(36, 38, 54);
        public static readonly Color TitleActive = Color.FromArgb(88, 101, 242);
        public static readonly Color TitleActiveLight = Color.FromArgb(112, 124, 250);
        public static readonly Color TitleInactive = Color.FromArgb(70, 72, 92);
        public static readonly Color TitleInactiveLight = Color.FromArgb(86, 88, 108);
        public static readonly Color TitleBtnHover = Color.FromArgb(110, 122, 250);
        public static readonly Color TopBarBg = Color.FromArgb(18, 20, 32);
        public static readonly Color ChipBg = Color.FromArgb(40, 44, 66);
        public static readonly Color ChipHover = Color.FromArgb(62, 70, 120);
        public static readonly Color TitleText = Color.FromArgb(255, 255, 255);
        public static readonly Color WindowBg = Color.FromArgb(242, 244, 250);
        public static readonly Color Text = Color.FromArgb(30, 32, 46);
        public static readonly Color TextMuted = Color.FromArgb(110, 114, 132);
        public static readonly Color DotRed = Color.FromArgb(255, 95, 87);
        public static readonly Color DotYellow = Color.FromArgb(254, 188, 46);
        public static readonly Color DotGreen = Color.FromArgb(40, 200, 64);

        // Boutons
        public static readonly Color Red = Color.FromArgb(220, 70, 70);
        public static readonly Color RedHover = Color.FromArgb(240, 95, 95);
        public static readonly Color Blue = Color.FromArgb(66, 128, 230);
        public static readonly Color BlueHover = Color.FromArgb(92, 150, 245);
        public static readonly Color Green = Color.FromArgb(46, 160, 90);
        public static readonly Color GreenHover = Color.FromArgb(64, 184, 110);

        // Coloration syntaxique FSL
        public static readonly Color CodeKeyword = Color.FromArgb(88, 101, 242);
        public static readonly Color CodeString = Color.FromArgb(34, 140, 80);
        public static readonly Color CodeNumber = Color.FromArgb(200, 110, 30);
        public static readonly Color CodeNull = Color.FromArgb(210, 60, 140);
        public static readonly Color CodeComment = Color.FromArgb(140, 146, 160);
        public static readonly Color ButtonShadow = Color.FromArgb(190, 194, 208);

        // Explorateur / lecteur texte (theme clair)
        public static readonly Color ToolbarBg = Color.FromArgb(232, 235, 244);
        public static readonly Color PathBg = Color.FromArgb(255, 255, 255);
        public static readonly Color PathBorder = Color.FromArgb(188, 194, 214);
        public static readonly Color Separator = Color.FromArgb(208, 212, 226);
        public static readonly Color SidebarBg = Color.FromArgb(226, 230, 242);
        public static readonly Color HeaderBg = Color.FromArgb(236, 239, 247);
        public static readonly Color RowHover = Color.FromArgb(222, 230, 255);
        public static readonly Color RowSelected = Color.FromArgb(196, 210, 255);
        public static readonly Color StatusBg = Color.FromArgb(230, 233, 242);
        public static readonly Color PaperBg = Color.FromArgb(255, 255, 255);
        public static readonly Color GutterBg = Color.FromArgb(240, 242, 248);
        public static readonly Color LightTrack = Color.FromArgb(226, 229, 238);
        public static readonly Color LightThumb = Color.FromArgb(160, 168, 196);
        public static readonly Color LightThumbActive = Color.FromArgb(110, 120, 170);

        // Console
        public static readonly Color ConsoleBg = Color.FromArgb(20, 22, 30);
        public static readonly Color ConsolePromptBg = Color.FromArgb(32, 35, 48);
        public static readonly Color ConsolePrompt = Color.FromArgb(110, 230, 150);
        public static readonly Color ConsoleInput = Color.FromArgb(240, 242, 250);
        public static readonly Color ConsoleText = Color.FromArgb(196, 204, 222);
        public static readonly Color ConsoleEcho = Color.FromArgb(130, 170, 255);
        public static readonly Color ConsoleError = Color.FromArgb(255, 120, 120);
        public static readonly Color ScrollTrack = Color.FromArgb(38, 41, 54);
        public static readonly Color ScrollThumb = Color.FromArgb(104, 110, 146);
        public static readonly Color ScrollThumbActive = Color.FromArgb(150, 158, 210);
    }
}
