using FileSystemOS.Processus;
using FileSystemOS.Systeme;

namespace FileSystemOS.Demarrage.Modules
{
    public class AlimentationModule : BootStage
    {
        public override string Name => "Extinction et redemarrage";

        public override string Run()
        {
            Services.Scheduler.Spawn("alimentation", true, Prio.Basse).Start(new AlimentationThread());
            return "pret";
        }
    }
}
