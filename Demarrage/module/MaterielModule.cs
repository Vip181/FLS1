using FileSystemOS.Systeme;

namespace FileSystemOS.Demarrage.Modules
{
    /// <summary>Fiche materiel de cet ordinateur (\\FSOS\\Systeme\\materiel.txt).</summary>
    public class MaterielModule : BootStage
    {
        public override string Name => "Detection du materiel";

        public override string Run()
        {
            Materiel.Save();
            return Services.ScreenW + "x" + Services.ScreenH + " (" + Materiel.Choix + "), " +
                   Materiel.RamMo + " Mo, " + Materiel.Video;
        }
    }
}
