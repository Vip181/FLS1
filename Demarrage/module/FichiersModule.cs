using System;
using Cosmos.System.FileSystem;
using Cosmos.System.FileSystem.VFS;

namespace FileSystemOS.Demarrage.Modules
{
    public class FichiersModule : BootStage
    {
        public override string Name => "Systeme de fichiers";

        /// <summary>Monte les disques FAT (appele des le debut par le noyau).</summary>
        public static void Mount()
        {
            if (Services.FileSystem != null) return;
            try
            {
                // Les disques sans partition FAT ont deja ete ecartes par les pilotes :
                // Cosmos ne connait que le FAT et resterait bloque sur un disque Windows.
                FileSystemOS.Systeme.Amorce.Etape("    " + Cosmos.HAL.BlockDevice.BlockDevice.Devices.Count +
                                                  " disque(s) confie(s) a Cosmos");
                Services.FileSystem = new CosmosVFS();
                VFSManager.RegisterVFS(Services.FileSystem);
                Services.FsReady = true;
            }
            catch (Exception e)
            {
                Services.FsReady = false;
                FileSystemOS.Systeme.Amorce.Etape("    ERREUR de montage : " + e.Message);
            }
        }

        public override string Run()
        {
            Mount();
            if (!Services.FsReady) return "aucun disque (l'OS fonctionne quand meme)";
            try { return VFSManager.GetVolumes().Count + " lecteur(s) FAT"; }
            catch (Exception) { return "monte"; }
        }
    }
}
