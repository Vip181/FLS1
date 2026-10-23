using FileSystemOS.Pilotes;

namespace FileSystemOS.Demarrage.Modules
{
    /// <summary>Reconnait l'ordinateur (marque, processeur, chipset, cartes) avant les pilotes.</summary>
    public class MachineModule : BootStage
    {
        public override string Name => "Reconnaissance de l'ordinateur";

        public override string Run()
        {
            Machine.Detect();
            return Machine.Marque + " / " + Machine.Cpu + " / " + PiloteReseau.Profil();
        }
    }
}
