using FileSystemOS.Noyau;
using FileSystemOS.Processus;

namespace FileSystemOS.Demarrage.Modules
{
    public class CogestionModule : BootStage
    {
        public override string Name => "Cogestion des threads";

        public override string Run()
        {
            Services.Scheduler.Spawn("cogestion", true, Prio.Haute).Start(new CogestionThread());
            return Cogestion.Budget + " rendu(s) de fenetre par tour";
        }
    }
}
