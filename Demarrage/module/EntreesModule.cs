using FileSystemOS.Processus;

namespace FileSystemOS.Demarrage.Modules
{
    public class EntreesModule : BootStage
    {
        public override string Name => "Clavier et souris";

        public override string Run()
        {
            Process p = Services.Scheduler.Spawn("systeme", true, Prio.Haute);
            p.Start(new InputThread());
            p.Start(new GcThread());
            p.Start(new FileSystemOS.Graphique.EnregistreurThread());     // capture differee, video
            p.Start(new SynchroThread());                                 // session du SSD, petit a petit
            return "souris PS/2 et clavier actifs";
        }
    }
}
