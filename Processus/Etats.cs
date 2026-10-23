namespace FileSystemOS.Processus
{
    /// <summary>
    /// Etats d'un processus ou d'un thread, comme dans un systeme multiprogramme :
    ///  Nouveau -> Pret -> Elu (il a le processeur) -> Pret ...
    ///  Elu -> Bloque (il attend un verrou ou un message) -> Pret
    ///  n'importe quand -> Gele (buffer d'attente) ou Termine.
    /// </summary>
    public static class Etat
    {
        public const int Nouveau = 0, Pret = 1, Elu = 2, Bloque = 3, Gele = 4, Termine = 5;

        public static string Nom(int e)
        {
            switch (e)
            {
                case Nouveau: return "nouveau";
                case Pret: return "pret";
                case Elu: return "elu";
                case Bloque: return "bloque";
                case Gele: return "gele";
                default: return "termine";
            }
        }
    }
}
