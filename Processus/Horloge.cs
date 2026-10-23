using System;

namespace FileSystemOS.Processus
{
    /// <summary>
    /// Horloge du systeme en millisecondes, entretenue par le minuteur materiel (PIT).
    /// Elle sert a mesurer le temps de chaque tranche d'execution (quantum).
    /// </summary>
    public static class Horloge
    {
        private static uint millis;
        public static bool Active;

        public static uint Millis => millis;

        public static void Start()
        {
            if (Active) return;
            try
            {
                Cosmos.HAL.Global.PIT.RegisterTimer(
                    new Cosmos.HAL.PIT.PITTimer(Battement, 1000000UL, true));   // 1 ms
                Active = true;
            }
            catch (Exception) { Active = false; }
        }

        private static void Battement() { millis++; }
    }
}
