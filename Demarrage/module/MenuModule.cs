namespace FileSystemOS.Demarrage.Modules
{
    public class MenuModule : BootStage
    {
        public override string Name => "Menu demarrer";

        public override string Run()
        {
            int apps = Services.Taskbar.RebuildMenu();
            return apps + " application(s) FSL trouvee(s)";
        }
    }
}
