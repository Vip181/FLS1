using FileSystemOS.Graphique;
using FileSystemOS.Processus;
using FileSystemOS.Systeme;

namespace FileSystemOS.Demarrage.Modules
{
    /// <summary>Buffers graphiques, compositeur, barres, buffer d'attente.</summary>
    public class InterfaceModule : BootStage
    {
        public override string Name => "Interface graphique";

        public override string Run()
        {
            Desktop.Build();
            Services.TopBar = new TopBar();
            Services.Wait = new WaitBuffer();
            Services.Wait.AutoBalance = Config.GetBool("attente_auto", true);
            int max = Config.GetInt("fenetres_actives_max", 4);
            WaitBuffer.MaxActive = max < 1 ? 1 : max;
            Services.Taskbar = new Taskbar();
            Services.Windows = new WindowManager();

            var s = Services.Scheduler;
            s.Spawn("affichage", true, Prio.Haute).Start(new CompositorThread());

            Process ui = s.Spawn("interface", true, Prio.Normale);
            ui.Start(new TaskbarThread());
            ui.Start(new MenuThread());
            ui.Start(new FondThread());          // rubans du fond d'ecran, apres le demarrage

            Process att = s.Spawn("attente", true, Prio.Basse);
            att.Start(new TopBarThread());
            att.Start(new WaitThread());

            return Services.ScreenW + "x" + Services.ScreenH + ", " + PixelBuffer.Registry.Count + " buffers";
        }
    }
}
