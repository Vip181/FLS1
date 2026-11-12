using FileSystemOS.Processus;
using FileSystemOS.Systeme;

namespace FileSystemOS.Demarrage.Modules
{
    public class ReseauModule : BootStage
    {
        public override string Name => "Reseau";

        public override string Run()
        {
            // Au demarrage : seulement les pilotes surs (jamais l'I219 experimental). La demande
            // d'adresse a la box peut attendre quelques secondes : le gardien est prevenu.
            FileSystemOS.Noyau.Watchdog.Lent = true;
            string r;
            try { r = Reseau.Init(Config.GetBool("dhcp", true)); }
            finally { FileSystemOS.Noyau.Watchdog.Lent = false; }
            Services.Scheduler.Spawn("reseau", true, Prio.Basse).Start(new ReseauThread());
            // Cles USB : le thread attend la fin du demarrage et la session ; il ne demarre le
            // controleur que si la detection automatique est activee (usb_detection, oui par defaut)
            Services.Scheduler.Spawn("usb", true, Prio.Basse).Start(new FileSystemOS.Pilotes.UsbThread());
            // Wi-Fi : le thread ne fait rien tant qu'aucun pilote n'est mis en service (wifi demo, menu Wi-Fi)
            Services.Scheduler.Spawn("wifi", true, Prio.Basse).Start(new FileSystemOS.SansFil.WifiThread());
            return r;
        }
    }
}
