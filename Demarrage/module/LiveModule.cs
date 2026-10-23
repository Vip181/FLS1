using FileSystemOS.Systeme;

namespace FileSystemOS.Demarrage.Modules
{
    /// <summary>Mode Live : utiliser l'OS sans l'installer (prepare des le noyau).</summary>
    public class LiveModule : BootStage
    {
        public override string Name => "Mode Live";
        public override string Run() => Live.Status;
    }
}
