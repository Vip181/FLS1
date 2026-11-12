using FileSystemOS.Graphique;

namespace FileSystemOS.Demarrage.Modules
{
    /// <summary>Prepare les caches qui accelerent l'OS une fois charge.</summary>
    public class AccelerationModule : BootStage
    {
        public override string Name => "Acceleration (caches)";

        public override string Run()
        {
            Police.Lire();                       // FSOS ou Comics (reglage "police" de interface.cfg)
            FontCache.Build();                   // texte : pixels precalcules
            HexBitmap warm = Glyphs.Close;       // icones hexa converties maintenant, pas au 1er clic
            warm = Glyphs.Trash;
            return "police " + Police.Nom + " en cache (" + FontCache.Count + " pixels), icones pretes";
        }
    }
}
