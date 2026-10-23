using FileSystemOS.Memoire;

namespace FileSystemOS.Demarrage.Modules
{
    public class MemoireModule : BootStage
    {
        public override string Name => "Memoire et accelerateur";

        public override string Run()
        {
            Services.Accelerator = new MemoryAccelerator();
            Services.Memory = new MemoryManager(Services.Accelerator);
            Services.Memory.Write(Services.OsName + " " + Services.Version + " demarre");
            Services.Memory.Write(Services.Company + " - " + Services.Author);
            return Cosmos.Core.CPU.GetAmountOfRAM() + " Mo de RAM, pool de " +
                   MemoryAccelerator.BlockCount + " blocs";
        }
    }
}
