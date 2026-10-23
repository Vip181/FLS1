using Cosmos.System.FileSystem;
using Cosmos.System.Graphics;
using Cosmos.System.Graphics.Fonts;
using FileSystemOS.Graphique;
using FileSystemOS.Memoire;
using FileSystemOS.Processus;

namespace FileSystemOS
{
    /// <summary>Acces global aux composants du systeme.</summary>
    public static class Services
    {
        // Identite du systeme
        public const string OsName = "File System OS";
        public const string Company = "File System OS Company";
        public const string Version = "1.28.2";

        /// <summary>Marqueur lu par installer-ssd.ps1 dans le fichier noyau pour connaitre sa version.</summary>
        public static readonly string MarqueurNoyau = "FSOS-NOYAU-VERSION=" + Version + ";";
        public const string Author = "Vincent Senut";

        // Taille de l'ecran : choisie au demarrage selon l'ordinateur (Systeme/Materiel.cs)
        public static int ScreenW = 1024;
        public static int ScreenH = 768;

        public static Canvas Canvas;
        public static Font Font;

        public static MemoryAccelerator Accelerator;
        public static MemoryManager Memory;
        public static Scheduler Scheduler;

        public static WindowManager Windows;
        public static Taskbar Taskbar;
        public static Compositor Compositor;

        // Fenetres gardees pour pouvoir les rouvrir depuis le menu
        public static ConsoleWindow ConsoleWin;
        public static ControlWindow PowerWin;
        public static ExplorerWindow ExplorerWin;
        public static EditorWindow EditorWin;
        public static OutputWindow FslWin;
        public static InstallerWindow InstallWin;
        public static ImageWindow ImageWin;
        public static AboutWindow AboutWin;
        public static DisplayWindow DisplayWin;
        public static LoginWindow LoginWin;
        public static AccountsWindow AccountsWin;
        public static BrowserWindow BrowserWin;
        public static PdfWindow PdfWin;
        public static StudioWindow StudioWin;
        public static TextWarWindow TextWarWin;
        public static ReseauWindow ReseauWin;
        public static TacheWindow TacheWin;
        public static CalculatriceWindow CalcWin;
        public static SerpentWindow SerpentWin;
        public static EnergieWindow EnergieWin;
        public static CaptureWindow CaptureWin;
        public static AlerteWindow AlerteWin;
        public static CorbeilleWindow CorbeilleWin;

        public static bool LoggedIn;                // session ouverte (sinon : ecran de connexion)

        // Systeme de fichiers (FAT) de Cosmos
        public static CosmosVFS FileSystem;
        public static bool FsReady;

        public static TopBar TopBar;
        public static WaitBuffer Wait;

        public static bool ScreenDirty = true;  // la scene (fenetres, barres) doit etre recomposee
        public static bool CursorDirty;         // seule la souris a bouge
        public static uint Frame;
        public static bool GcRequest;               // ramasse-miettes demande pour la fin du tour

        // Demarrage / arret
        public static bool Booting = true;          // ecran de chargement affiche
        public static uint ToursDemarrage;          // images du chargeur reussies (le gardien verifie qu'elles avancent)
        public static string EtapeDemarrage = "";   // etape du chargement en cours
        public static bool ShuttingDown;            // ecran d'arret affiche
        public static int BootStageFrames = 6;      // demarrage.cfg
        public static readonly System.Collections.Generic.List<string> BootLog =
            new System.Collections.Generic.List<string>();

        public static int ClockOffset;              // horloge.cfg
        public static uint Tps, Fps;                // mesures du gardien du noyau
        public static string ClockText = "--:--:--";
    }
}
