using System;
using System.Collections.Generic;

namespace FileSystemOS.Processus
{
    public class Message
    {
        public Process De;
        public string Texte;
        public uint Millis;
    }

    /// <summary>
    /// COMMUNICATION ENTRE PROCESSUS : une boite aux lettres par processus.
    /// Envoyer ne bloque jamais ; Recevoir rend null si la boite est vide, et le thread
    /// peut se declarer "bloque" en attendant (Attendre), ce qui libere le processeur.
    /// </summary>
    public static class Ipc
    {
        public const int MaxMessages = 32;
        public static uint Envoyes, Recus, Perdus;

        private static readonly List<Process> boites = new List<Process>();
        private static readonly List<Queue<Message>> files = new List<Queue<Message>>();

        private static Queue<Message> File(Process p, bool creer)
        {
            int i = boites.IndexOf(p);
            if (i >= 0) return files[i];
            if (!creer) return null;
            boites.Add(p);
            files.Add(new Queue<Message>());
            return files[files.Count - 1];
        }

        public static bool Envoyer(Process de, Process vers, string texte)
        {
            if (vers == null || !vers.Alive) { Perdus++; return false; }
            var f = File(vers, true);
            if (f.Count >= MaxMessages) { Perdus++; return false; }
            var m = new Message();
            m.De = de; m.Texte = texte; m.Millis = Horloge.Millis;
            f.Enqueue(m);
            Envoyes++;
            return true;
        }

        /// <summary>Envoi par nom de processus (le premier trouve).</summary>
        public static bool Envoyer(Process de, string nom, string texte)
        {
            var procs = Services.Scheduler.Processes;
            for (int i = 0; i < procs.Count; i++)
                if (procs[i].Name == nom) return Envoyer(de, procs[i], texte);
            Perdus++;
            return false;
        }

        public static Message Recevoir(Process p)
        {
            var f = File(p, false);
            if (f == null || f.Count == 0) return null;
            Recus++;
            return f.Dequeue();
        }

        public static int EnAttente(Process p)
        {
            var f = File(p, false);
            return f == null ? 0 : f.Count;
        }

        public static void Oublier(Process p)
        {
            int i = boites.IndexOf(p);
            if (i < 0) return;
            Perdus += (uint)files[i].Count;
            boites.RemoveAt(i);
            files.RemoveAt(i);
        }

        public static List<string> Etat()
        {
            var r = new List<string>();
            r.Add("Messages : " + Envoyes + " envoyes, " + Recus + " recus, " + Perdus + " perdus");
            for (int i = 0; i < boites.Count; i++)
                if (files[i].Count > 0)
                    r.Add("  boite de " + boites[i].Name + " (PID " + boites[i].Pid + ") : " + files[i].Count + " message(s)");
            if (r.Count == 1) r.Add("  toutes les boites sont vides");
            return r;
        }
    }
}
