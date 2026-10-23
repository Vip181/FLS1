using FileSystemOS.Memoire;
using FileSystemOS.Systeme;

namespace FileSystemOS.Demarrage.Modules
{
    /// <summary>Memoire paginee : reserve decoupee en pages de 4 Ko attribuees aux processus.</summary>
    public class PagesModule : BootStage
    {
        public override string Name => "Memoire paginee";

        public override string Run()
        {
            int demande = Config.GetInt("pages_memoire", 512);        // 512 pages = 2 Mo
            Pages.Init(demande);

            // Pages du noyau : verrouillees, jamais rendues automatiquement
            Pages.Allouer("noyau (tables)", Attr.Systeme | Attr.Verrouillee, null, null);
            return Pages.Total + " pages de 4 Ko (" + Utils.Fmt.Size(Pages.Octets) + "), " +
                   Pages.Libres + " libres";
        }
    }
}
