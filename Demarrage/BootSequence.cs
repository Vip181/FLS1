using System;
using System.Collections.Generic;
using FileSystemOS.Demarrage.Modules;
using FileSystemOS.Processus;

namespace FileSystemOS.Demarrage
{
    /// <summary>Une etape du chargement : initialise un module et lance ses processus.</summary>
    public abstract class BootStage
    {
        public abstract string Name { get; }

        /// <summary>Execute l'etape et renvoie un court compte rendu.</summary>
        public abstract string Run();
    }

    public static class BootSequence
    {
        /// <summary>Ordre de chargement de File System OS.</summary>
        public static List<BootStage> Stages()
        {
            var l = new List<BootStage>();
            l.Add(new NoyauModule());             // ouvre le noyau (processus "noyau")
            l.Add(new MemoireModule());           // accelerateur + zone memoire
            l.Add(new PagesModule());             // memoire paginee (pages attribuees aux processus)
            l.Add(new FichiersModule());          // disques FAT
            l.Add(new LiveModule());              // mode Live (disque en memoire)
            l.Add(new FichiersInternesModule());  // \FSOS\Systeme\*.cfg
            l.Add(new MaterielModule());          // processeur, memoire, ecran
            l.Add(new AccelerationModule());      // caches (police, icones)
            l.Add(new HorlogeModule());           // heure
            l.Add(new MachineModule());           // marque, processeur, chipset, cartes
            l.Add(new ReseauModule());            // carte reseau + DHCP
            l.Add(new CogestionModule());         // cogestion threads / rendus
            l.Add(new InterfaceModule());         // buffers, compositeur, barres
            l.Add(new EntreesModule());           // clavier + souris
            l.Add(new ConsoleModule());           // console + commandes
            l.Add(new MenuModule());              // menu demarrer + applications
            l.Add(new AlimentationModule());      // extinction / redemarrage
            l.Add(new ComptesModule());           // comptes et mots de passe
            l.Add(new ApplicationsModule());      // fenetres de depart
            return l;
        }
    }

    /// <summary>
    /// Thread du processus "demarrage" : anime le logo et execute une etape
    /// toutes les N images, puis laisse la place au bureau et se termine.
    /// </summary>
    public class BootThread : KThread
    {
        private readonly List<BootStage> stages;
        private readonly List<string> log = new List<string>();
        private readonly BootScreen screen = new BootScreen();
        private int index = -1, wait, endFrames;
        private uint frame;

        public BootThread(List<BootStage> list) : base("chargeur") { stages = list; }

        public override void Step()
        {
            if (index < 0) { screen.Begin("Chargement du systeme"); index = 0; }

            frame++;
            string label = index < stages.Count
                ? "Chargement : " + stages[index].Name + "..."
                : "Pret";
            if (index < stages.Count) Services.EtapeDemarrage = stages[index].Name;
            screen.Frame(frame, index, stages.Count, label, log);
            Services.ToursDemarrage++;                 // l'image est sortie : le chargement avance

            if (index >= stages.Count)
            {
                if (++endFrames >= 12) Finish();
                return;
            }

            if (++wait < Services.BootStageFrames) return;
            wait = 0;

            BootStage st = stages[index];
            string result;
            try { result = st.Run(); }
            catch (Exception e) { result = "ERREUR : " + e.Message; }

            string line = st.Name + " : " + result;
            log.Add(line);
            Services.BootLog.Add(line);
            index++;
        }

        private void Finish()
        {
            Services.Booting = false;
            Services.ScreenDirty = true;
            Owner.Kill();
        }
    }
}
