using FileSystemOS.Graphique;

namespace FileSystemOS.Demarrage.Modules
{
    public class ConsoleModule : BootStage
    {
        public override string Name => "Console et commandes";

        public override string Run()
        {
            Services.ConsoleWin = new ConsoleWindow(390, 250);
            return "interpreteur de commandes pret";
        }
    }
}
