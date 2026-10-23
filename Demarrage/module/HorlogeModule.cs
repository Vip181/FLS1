using FileSystemOS.Processus;
using FileSystemOS.Utils;

namespace FileSystemOS.Demarrage.Modules
{
    public class HorlogeModule : BootStage
    {
        public override string Name => "Horloge";

        public override string Run()
        {
            Services.Scheduler.Spawn("horloge", true, Prio.Basse).Start(new ClockThread());
            int h = ((Cosmos.HAL.RTC.Hour + Services.ClockOffset) % 24 + 24) % 24;
            return "il est " + Fmt.Two(h) + ":" + Fmt.Two(Cosmos.HAL.RTC.Minute);
        }
    }
}
