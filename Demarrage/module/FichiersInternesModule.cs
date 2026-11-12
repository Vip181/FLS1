using FileSystemOS.Noyau;
using FileSystemOS.Systeme;

namespace FileSystemOS.Demarrage.Modules
{
    /// <summary>Lit les fichiers internes \FSOS\Systeme\*.cfg et applique les reglages.</summary>
    public class FichiersInternesModule : BootStage
    {
        public override string Name => "Fichiers systeme internes";

        public override string Run()
        {
            string r = Config.LoadAll();   // relecture (deja lus par le noyau pour la resolution)

            int f = Config.GetInt("duree_etape", 6);
            Services.BootStageFrames = f < 1 ? 1 : (f > 60 ? 60 : f);
            Services.ClockOffset = Config.GetInt("decalage_heures", 0);
            int b = Config.GetInt("rendus_par_tour", 2);
            Cogestion.Budget = b < 1 ? 1 : (b > 8 ? 8 : b);
            // Clavier francais (AZERTY) sauf si "clavier=qwerty"
            FileSystemOS.Processus.Clavier.Azerty = Config.Get("clavier", "azerty").ToLower() != "qwerty";
            FileSystemOS.Pilotes.UsbStockage.EcritureAutorisee = Config.GetBool("usb_ecriture", false);
            return r + (FileSystemOS.Processus.Clavier.Azerty ? " ; clavier AZERTY" : " ; clavier QWERTY");
        }
    }
}
