using System;
using FileSystemOS.Memoire;
using FileSystemOS.Processus;
using FileSystemOS.Utils;

namespace FileSystemOS.Fsl
{
    /// <summary>Compile et lance les programmes FSL, chacun dans son propre processus.</summary>
    public static class Launcher
    {
        /// <summary>PID du dernier programme lance.</summary>
        public static int Pid = -1;

        public static FslProgram CompileFile(string path) =>
            Compiler.Compile(TextCodec.Join(TextCodec.ReadLines(path)));

        /// <summary>fichier.fsl -> fichier.fsc (bytecode)</summary>
        public static string CompileToFile(string path)
        {
            FslProgram prog = CompileFile(path);
            int dot = path.LastIndexOf('.');
            string outPath = (dot > path.LastIndexOf('\\') ? path.Substring(0, dot) : path) + ".fsc";
            prog.Save(outPath);
            return TextCodec.FileName(outPath) + " (" + prog.Code.Count + " mots de bytecode)";
        }

        public static bool Run(string path, out string message)
        {
            string name = TextCodec.FileName(path);
            var outWin = Services.FslWin;
            Services.Windows.Open(outWin);

            FslProgram prog;
            try
            {
                prog = path.ToLower().EndsWith(".fsc") ? FslProgram.Load(path) : CompileFile(path);
            }
            catch (Exception e)
            {
                message = "Erreur de compilation : " + e.Message;
                outWin.Print(message);
                return false;
            }
            return Lancer(prog, name, "fsl-" + name.ToLower(), null, null, out message);
        }

        /// <summary>
        /// Lance un programme deja compile dans son propre processus. Un PILOTE recoit en plus la
        /// liste des ports materiels declares dans son .config (les seuls qu'il pourra toucher).
        /// </summary>
        public static bool Lancer(FslProgram prog, string name, string nomProcessus, System.Collections.Generic.List<int> ports,
                                  string pilote, out string message)
        {
            var outWin = Services.FslWin;
            // La sentinelle verifie le bytecode AVANT d'executer quoi que ce soit
            string defaut = Systeme.Sentinelle.Verifier(prog, name);
            if (defaut != null)
            {
                Systeme.Sentinelle.Refuser(name, defaut);
                outWin.Print("PROGRAMME CORROMPU : " + name + " - " + defaut);
                message = "programme corrompu : " + defaut;
                return false;
            }

            outWin.Print("== " + name + (pilote != null ? " (pilote " + pilote + ")" : "") + " ==");
            Process proc = Services.Scheduler.Spawn(nomProcessus, false);

            // Memoire paginee du programme : code en lecture+execution, donnees en lecture+ecriture.
            // Tout est rendu automatiquement quand le processus se termine.
            int pagesCode = (prog.Code.Count * 4 + 4095) / 4096;
            if (pagesCode < 1) pagesCode = 1;
            var code = Memoire.Pages.Allouer(pagesCode, name + " (code)", Memoire.Attr.Code, proc, null);
            var donnees = Memoire.Pages.Allouer(1, name + " (donnees)", Memoire.Attr.Donnees, proc, null);
            if (code == null || donnees == null)
            {
                outWin.Print("Erreur : plus assez de pages memoire pour " + name);
                proc.Kill();
                message = "memoire insuffisante";
                return false;
            }
            outWin.Print("Memoire : " + (pagesCode + 1) + " page(s) de 4 Ko attribuee(s) a " + name);

            var vm = new Vm(prog, outWin);
            vm.Ctx.Programme = name;
            vm.Ctx.Ports = ports;
            vm.Ctx.Pilote = pilote;
            var thread = new FslThread(vm, name);
            Pid = proc.Pid;
            proc.Start(thread);
            thread.Allouer(name + " (pile)", Memoire.Attr.Donnees);   // page propre au thread
            message = name + " lance (PID " + proc.Pid + ")";
            return true;
        }
    }

    /// <summary>Thread d'un programme FSL : 300 instructions par tour d'ordonnanceur.</summary>
    public class FslThread : KThread
    {
        private readonly Vm vm;
        private readonly string name;

        public FslThread(Vm machine, string programName) : base("vm")
        {
            vm = machine;
            name = programName;
        }

        public override void Step()
        {
            if (vm.Done) return;
            // Le programme dort (attendre, ou pas d'evenement) : il ne consomme rien
            if (vm.Ctx.ReveilA != 0)
            {
                if (Horloge.Millis < vm.Ctx.ReveilA) return;
                vm.Ctx.ReveilA = 0;
            }
            vm.Run(vm.Ctx.Fenetres.Count > 0 ? 2000 : 300);     // un jeu a besoin d'un peu plus
            if (!vm.Done) return;
            vm.Ctx.FermerTout();

            if (vm.Error != null) Services.FslWin.Print("Erreur d'execution - " + vm.Error);
            int rendues = Owner == null ? 0 : Owner.PagesUtilisees;
            Services.FslWin.Print("-- " + name + " termine (" + vm.Steps + " instructions, " +
                                  rendues + " page(s) rendue(s)) --");
            Owner.Kill();
        }
    }
}
