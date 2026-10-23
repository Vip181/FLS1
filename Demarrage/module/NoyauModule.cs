using FileSystemOS.Noyau;
using FileSystemOS.Processus;

namespace FileSystemOS.Demarrage.Modules
{
    /// <summary>Etape 1 : ouvre le noyau (processus "noyau" + thread "gardien").</summary>
    public class NoyauModule : BootStage
    {
        public override string Name => "Ouverture du noyau";

        public override string Run()
        {
            Process p = Services.Scheduler.Spawn("noyau", true, Prio.Haute);
            p.Start(new GardienThread());
            return "processus noyau PID " + p.Pid;
        }
    }
}
