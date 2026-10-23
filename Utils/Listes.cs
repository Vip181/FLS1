using System.Collections.Generic;

namespace FileSystemOS.Utils
{
    /// <summary>
    /// Recherche d'un texte dans une liste de textes, en comparant leur CONTENU.
    ///
    /// List&lt;string&gt;.IndexOf et Contains passent par EqualityComparer, qui sous Cosmos
    /// compare visiblement les ADRESSES : deux textes identiques lus a des moments differents
    /// n'etaient pas reconnus. Consequence sur le Dell : la session du SSD reecrivait puis
    /// effacait tous ses fichiers a chaque enregistrement. Ici, on compare avec ==.
    /// </summary>
    public static class Listes
    {
        public static int Indice(List<string> l, string s)
        {
            if (l == null) return -1;
            for (int i = 0; i < l.Count; i++) if (l[i] == s) return i;
            return -1;
        }

        public static bool Contient(List<string> l, string s) => Indice(l, s) >= 0;
    }
}
