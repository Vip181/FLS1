using FileSystemOS.Processus;
using FileSystemOS.Systeme;

namespace FileSystemOS.Demarrage.Modules
{
    public class ReseauModule : BootStage
    {
        public override string Name => "Reseau";

        public override string Run()
        {
            string r = Reseau.Init(Config.GetBool("dhcp", true));
            Services.Scheduler.Spawn("reseau", true, Prio.Basse).Start(new ReseauThread());
            return r;
        }
    }
}
