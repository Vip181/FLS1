using FileSystemOS.Graphique;

namespace FileSystemOS.Demarrage.Modules
{
    /// <summary>Cree les fenetres et ouvre celles du bureau de depart.</summary>
    public class ApplicationsModule : BootStage
    {
        public override string Name => "Applications";

        public override string Run()
        {
            Services.PowerWin = new ControlWindow(30, 40);
            Services.ExplorerWin = new ExplorerWindow(200, 60);
            Services.EditorWin = new EditorWindow(300, 90);
            Services.FslWin = new OutputWindow(470, 400);
            Services.InstallWin = new InstallerWindow(280, 120);
            Services.ImageWin = new ImageWindow(240, 80);
            Services.AboutWin = new AboutWindow(260, 90);
            Services.DisplayWin = new DisplayWindow(300, 70);
            Services.BrowserWin = new BrowserWindow(160, 50);
            Services.PdfWin = new PdfWindow(220, 60);
            Services.StudioWin = new StudioWindow(40, 36);
            Services.TextWarWin = new TextWarWindow(260, 110);
            Services.ReseauWin = new ReseauWindow(180, 70);
            Services.TacheWin = new TacheWindow(140, 60);
            Services.CalcWin = new CalculatriceWindow(420, 120);
            Services.SerpentWin = new SerpentWindow(200, 90);
            Services.EnergieWin = new EnergieWindow(300, 140);
            Services.CaptureWin = new CaptureWindow(340, 160);
            Services.AlerteWin = new AlerteWindow(0, 0);
            Services.CorbeilleWin = new CorbeilleWindow(260, 120);
            Services.AccountsWin = new AccountsWindow(280, 60);
            Services.LoginWin = new LoginWindow((Services.ScreenW - 440) / 2, (Services.ScreenH - 400) / 2);

            // SSD detecte sans File System OS installe : l'assistant d'installation d'abord.
            // Sans SSD (ou deja installe) : directement l'ecran de connexion (mode Live ou installe).
            if (FileSystemOS.Graphique.AssistantInstallation.DoitSAfficher())
            {
                FileSystemOS.Graphique.AssistantInstallation.Ouvrir();
                return "assistant d'installation";
            }

            // Un compte existe et la connexion automatique est active : directement au bureau
            if (FileSystemOS.Systeme.Session.ConnexionAuto())
                return "connexion automatique : " + FileSystemOS.Systeme.Comptes.Actuel.Nom;

            // Sinon (premier demarrage, ou connexion_auto=non) : la page de connexion
            FileSystemOS.Systeme.Session.ShowLogin();
            return "ecran de connexion";
        }
    }
}
