using System;
using System.Collections.Generic;
using System.IO;
using Cosmos.HAL.BlockDevice;
using Cosmos.System.FileSystem.Listing;
using Cosmos.System.FileSystem.VFS;
using FileSystemOS.Graphique;
using FileSystemOS.Pilotes;
using FileSystemOS.Utils;

namespace FileSystemOS.Systeme
{
    /// <summary>
    /// GESTION DIRECTE DU SSD depuis la console (commande "ssd").
    ///
    /// Relie les trois couches ecrites pour File System OS :
    ///   pilote de disque (Ahci / Nvme)  ->  tables de partitions (Gpt)  ->  fichiers (FatDirect)
    /// sans jamais passer par le systeme de fichiers de Cosmos, qui ne sait pas monter le SSD.
    /// On peut lister, lire, ecrire, creer des dossiers, copier dans les deux sens, et installer
    /// tout l'environnement \FSOS de l'OS sur le SSD.
    /// </summary>
    public static class Ssd
    {
        public static FatDirect Volume;
        public static string Courant = "";          // dossier courant sur le SSD

        // ---------------- Disques connus ----------------

        /// <summary>Tous les disques : ceux confies a Cosmos et ceux gardes de cote.</summary>
        private static List<BlockDevice> Disques(List<string> noms)
        {
            var r = new List<BlockDevice>();
            for (int i = 0; i < Ahci.Disques.Count; i++) { r.Add(Ahci.Disques[i]); noms.Add("sata" + i); }
            for (int i = 0; i < Nvme.Disques.Count; i++) { r.Add(Nvme.Disques[i]); noms.Add("nvme" + i); }
            int autre = 0;
            for (int i = 0; i < BlockDevice.Devices.Count; i++)
            {
                BlockDevice d = BlockDevice.Devices[i];
                if (r.Contains(d)) continue;
                if (Live.Disk != null && d == (BlockDevice)Live.Disk) continue;      // disque memoire
                r.Add(d);
                noms.Add("disque" + (autre++));
            }
            return r;
        }

        private static BlockDevice Trouver(string nom, out string erreur)
        {
            erreur = "";
            var noms = new List<string>();
            var d = Disques(noms);
            for (int i = 0; i < d.Count; i++) if (noms[i] == nom) return d[i];
            erreur = "disque inconnu '" + nom + "' (tapez ssd pour la liste)";
            return null;
        }

        private static string Chemin(string arg)
        {
            if (arg.Length == 0) return Courant;
            if (arg[0] == '\\' || arg[0] == '/') return arg;
            return Courant.Length == 0 ? arg : Courant + "\\" + arg;
        }

        // ---------------- Point d'entree ----------------

        public static void Commande(string arg, List<string> o)
        {
            string sous = arg, reste = "";
            int sp = arg.IndexOf(' ');
            if (sp > 0) { sous = arg.Substring(0, sp); reste = arg.Substring(sp + 1).Trim(); }
            sous = sous.ToLower();

            try
            {
                switch (sous)
                {
                    case "": case "aide": Aide(o); Partitions(o); break;
                    case "partitions": Partitions(o); break;
                    case "monter": Monter(reste, o); break;
                    case "demonter": Volume = null; Courant = ""; o.Add("Volume demonte."); break;
                    case "info": Info(o); break;
                    case "ls": case "dir": Lister(reste, o); break;
                    case "cd": Cd(reste, o); break;
                    case "lire": case "cat": LireTexte(reste, o); break;
                    case "ecrire": EcrireTexte(reste, o); break;
                    case "mkdir": Exiger(); Volume.CreerDossier(Chemin(reste)); o.Add("Dossier cree : " + Chemin(reste)); break;
                    case "rm": Exiger(); Volume.Supprimer(Chemin(reste)); o.Add("Supprime : " + Chemin(reste)); break;
                    case "copier": Copier(reste, o); break;
                    case "importer": Importer(reste, o); break;
                    case "installer": Installer(o); break;
                    case "libre": Exiger(); o.Add("Calcul en cours..."); o.Add(Fmt.Size((long)Volume.GrappesLibres() * Volume.OctetsParGrappe) + " libres"); break;
                    case "verif":
                        Exiger();
                        Volume.Verifier = reste != "non";
                        o.Add("Verification apres ecriture : " + (Volume.Verifier ? "oui" : "non"));
                        break;
                    case "disque": Fiche(o); break;
                    case "diag": case "diagnostic": Diagnostic(o); break;
                    case "secteur": Secteur(reste, o); break;
                    case "amorce": Amorce(reste, o); break;
                    case "test": Test(o); break;
                    default: o.Add("Sous-commande inconnue : " + sous); Aide(o); break;
                }
            }
            catch (Exception e)
            {
                o.Add("Erreur : " + e.Message);
            }
        }

        private static void Exiger()
        {
            if (Volume == null) throw new Exception("aucun volume monte (ssd monter <disque> <partition>)");
            Volume.Oublier();      // un autre montage (session du SSD) a pu ecrire entre-temps
        }

        private static void Aide(List<string> o)
        {
            o.Add("Gestion directe du SSD (pilote FAT32 de File System OS, sans Cosmos) :");
            o.Add("  ssd partitions                 partitions FAT32 de tous les disques");
            o.Add("  ssd monter sata0 1 [ecriture]  ouvre la partition 1 (lecture seule par defaut)");
            o.Add("  ssd ls [dossier] / cd <dossier> / info / libre / disque");
            o.Add("  ssd lire <fichier>             affiche un fichier texte");
            o.Add("  ssd ecrire <fichier> <texte>   cree ou remplace un fichier");
            o.Add("  ssd mkdir <dossier> / rm <element>");
            o.Add("  ssd copier <fichier de l'OS> <fichier du SSD>");
            o.Add("  ssd importer <fichier du SSD> <fichier de l'OS>");
            o.Add("  ssd installer                  copie tout \\FSOS de l'OS sur le SSD");
            o.Add("  ssd diag                       diagnostic pas a pas (a photographier)");
            o.Add("  ssd secteur sata0 <n>          affiche un secteur brut en hexadecimal");
            o.Add("  ssd amorce sata0 [actif|inactif] demarrage Legacy du SSD en mode AHCI");
            o.Add("  ssd test                       test d'ecriture complet sur la partition FSOS");
        }

        // ---------------- Partitions ----------------

        private static void Partitions(List<string> o)
        {
            var noms = new List<string>();
            var d = Disques(noms);
            if (d.Count == 0) { o.Add("Aucun disque. (BIOS : SATA Operation = AHCI)"); return; }
            for (int i = 0; i < d.Count; i++)
            {
                var parts = Gpt.Lire(d[i], out bool gpt);
                o.Add(noms[i] + " : " + Fmt.Size((long)(d[i].BlockCount * d[i].BlockSize)) + ", table " +
                      (gpt ? "GPT" : "MBR") + ", " + parts.Count + " partition(s)");
                for (int k = 0; k < parts.Count; k++)
                {
                    PartitionInfo p = parts[k];
                    string etat = "";
                    var v = FatDirect.Monter(d[i], p.Debut, noms[i], out string err);
                    if (v != null) etat = "  FAT32 \"" + v.Etiquette + "\"" + (v.EstFsos ? " [File System OS : ecriture permise]" : " [lecture seule]");
                    string type = p.Type;
                    if (type == "Donnees Windows") type = v != null ? "Donnees (FAT32)" : "Donnees (NTFS)";
                    o.Add("   " + p.Numero + ". " + Fmt.Pad(type, 22) + Fmt.Pad(Fmt.Size(p.Octets), 10) +
                          (p.Nom.Length > 0 ? "\"" + p.Nom + "\"" : "") + etat);
                }
            }
        }

        private static void Monter(string reste, List<string> o)
        {
            string[] m = reste.Split(' ');
            if (m.Length < 1 || m[0].Length == 0) { o.Add("Usage : ssd monter sata0 [partition] [ecriture]"); return; }
            BlockDevice d = Trouver(m[0], out string err);
            if (d == null) { o.Add("Erreur : " + err); return; }

            var parts = Gpt.Lire(d, out bool gpt);
            int num = m.Length >= 2 ? Fmt.ParseInt(m[1]) : -1;
            if (num < 0)
            {
                // Pas de numero : la partition nommee "FSOS" d'abord, sinon la premiere FAT32 qui
                // n'est pas la partition de demarrage de Windows (EFI), en dernier recours l'EFI.
                int secours = -1, efi = -1;
                for (int i = 0; i < parts.Count && num < 0; i++)
                {
                    var essai = FatDirect.Monter(d, parts[i].Debut, m[0], out string ignore);
                    if (essai == null) continue;
                    if (essai.EstFsos) num = parts[i].Numero;
                    else if (parts[i].Type.StartsWith("EFI")) { if (efi < 0) efi = parts[i].Numero; }
                    else if (secours < 0) secours = parts[i].Numero;
                }
                if (num < 0) num = secours >= 0 ? secours : efi;
                if (num < 0) { o.Add("Erreur : aucune partition FAT32 sur " + m[0] + " (tapez ssd diag)"); return; }
                o.Add("Partition FAT32 trouvee : numero " + num);
            }
            PartitionInfo cible = null;
            for (int i = 0; i < parts.Count; i++) if (parts[i].Numero == num) cible = parts[i];
            // Disque sans table (formate en entier) : la partition 0 = le disque complet
            ulong debut = cible != null ? cible.Debut : 0;
            if (cible == null && num != 0) { o.Add("Erreur : partition " + num + " introuvable (ssd partitions)"); return; }

            var v = FatDirect.Monter(d, debut, m[0] + " partition " + num, out string e2);
            if (v == null) { o.Add("Erreur : " + e2); return; }

            if ((m.Length >= 3 && m[2] == "ecriture") || (m.Length == 2 && m[1] == "ecriture"))
            {
                v.Ecriture = true;
                o.Add("ATTENTION : ecriture FORCEE sur un volume qui n'a pas ete cree par File System OS.");
                if (cible != null && cible.Type.StartsWith("EFI"))
                    o.Add("ATTENTION : c'est la partition de demarrage de Windows. Ne modifiez pas \\EFI.");
            }
            // Meme partition que la session du SSD : un seul montage, donc un seul cache de FAT
            if (SessionSsd.Volume != null && SessionSsd.Volume.Disque == d && SessionSsd.Volume.Debut == debut)
            {
                bool ecr = v.Ecriture;
                v = SessionSsd.Volume;
                if (ecr) v.Ecriture = true;
            }
            Volume = v;
            Courant = "";
            if (VueFsos.Montee(d, debut))
            {
                o.Add("ATTENTION : cette partition est aussi un lecteur de l'Explorateur.");
                o.Add("Pour ecrire dessus, preferez l'Explorateur ou l'Editeur : ecrire des deux");
                o.Add("cotes a la fois peut melanger les tables FAT.");
            }
            var r = v.Resume();
            for (int i = 0; i < r.Count; i++) o.Add(r[i]);
        }

        private static void Info(List<string> o)
        {
            Exiger();
            var r = Volume.Resume();
            for (int i = 0; i < r.Count; i++) o.Add(r[i]);
            o.Add("  dossier courant : \\" + Courant);
        }

        private static void Fiche(List<string> o)
        {
            for (int i = 0; i < Ahci.Disques.Count; i++) o.Add("sata" + i + " : " + Ahci.Disques[i].Fiche);
            for (int i = 0; i < Nvme.Disques.Count; i++) o.Add("nvme" + i + " : " + Nvme.Disques[i].Modele + " " + Fmt.Size(Nvme.Disques[i].Octets));
            if (Ahci.Disques.Count + Nvme.Disques.Count == 0) o.Add("Aucun disque pilote par File System OS.");
        }

        // ---------------- Diagnostic pas a pas ----------------

        private static void Diagnostic(List<string> o)
        {
            o.Add("=== DIAGNOSTIC DU STOCKAGE - File System OS " + Services.Version + " ===");

            // 1. Ce que le bus PCI montre
            o.Add("1. Controleurs de stockage sur le bus PCI :");
            if (PciCatalogue.Tous.Count == 0) PciCatalogue.Scan();
            int nb = 0;
            for (int i = 0; i < PciCatalogue.Tous.Count; i++)
            {
                Peripherique p = PciCatalogue.Tous[i];
                if (p.Class != 0x01) continue;
                nb++;
                string mode = p.Sub == 0x06 ? "AHCI (bon mode)" : p.Sub == 0x04 ? "RAID -> BIOS : passer en AHCI !" :
                              p.Sub == 0x08 ? "NVMe" : p.Sub == 0x01 ? "IDE" : "sous-classe " + Fmt.Hex(p.Sub, 2);
                o.Add("   [" + p.Id + "] " + p.VendorName + " - " + mode + "  progIF " + Fmt.Hex(p.ProgIF, 2));
            }
            if (nb == 0) o.Add("   AUCUN : le BIOS cache le controleur (SATA Operation = Disabled ?)");

            // 2. Ce que les pilotes ont trouve
            o.Add("2. Pilotes : AHCI = " + Ahci.Journal);
            o.Add("            NVMe = " + Nvme.Journal);
            for (int i = 0; i < Ahci.Disques.Count; i++) o.Add("   sata" + i + " : " + Ahci.Disques[i].Fiche);

            var noms = new List<string>();
            var disques = Disques(noms);
            if (disques.Count == 0) { o.Add("   -> aucun disque : rien d'autre a tester."); return; }

            for (int k = 0; k < disques.Count; k++)
            {
                BlockDevice d = disques[k];
                o.Add("3. " + noms[k] + " : lecture du secteur 0...");
                byte[] s0 = new byte[512];
                try { d.ReadBlock(0, 1, ref s0); }
                catch (Exception e) { o.Add("   ECHEC DE LECTURE : " + e.Message); continue; }
                o.Add("   lu. Signature " + Fmt.Hex(s0[510], 2) + Fmt.Hex(s0[511], 2) + (s0[510] == 0x55 && s0[511] == 0xAA ? " (ok)" : " (ABSENTE)"));
                for (int i = 0; i < 4; i++)
                {
                    byte t = s0[446 + i * 16 + 4];
                    if (t != 0) o.Add("   entree MBR " + (i + 1) + " : type " + Fmt.Hex(t, 2) + " = " + Gpt.TypeMbr(t));
                }

                o.Add("4. " + noms[k] + " : table de partitions");
                var parts = Gpt.Lire(d, out bool gpt);
                o.Add("   " + (gpt ? "GPT" : "MBR") + ", " + parts.Count + " partition(s)");
                for (int i = 0; i < parts.Count; i++)
                {
                    PartitionInfo p = parts[i];
                    o.Add("   " + p.Numero + ". " + p.Type + " " + Fmt.Size(p.Octets) + " au secteur " + p.Debut +
                          (p.Nom.Length > 0 ? " \"" + p.Nom + "\"" : ""));
                    o.Add("      -> " + FatDirect.Analyse(d, p.Debut));
                    var v = FatDirect.Monter(d, p.Debut, noms[k], out string err);
                    if (v == null) { o.Add("      -> non montable : " + err); continue; }
                    try
                    {
                        var l = v.Lister("");
                        o.Add("      -> MONTABLE : FAT32 \"" + v.Etiquette + "\", " + l.Count + " element(s) a la racine" +
                              (v.EstFsos ? ", ecriture permise" : ", lecture seule"));
                        for (int j = 0; j < l.Count && j < 5; j++) o.Add("         " + (l[j].Dossier ? "[dossier] " : "") + l[j].Nom);
                    }
                    catch (Exception e) { o.Add("      -> montable mais lecture de la racine EN ECHEC : " + e.Message); }
                }
            }
            o.Add("=== fin du diagnostic : prenez cet ecran en photo ===");
        }

        /// <summary>
        /// Drapeau "demarrable" du MBR de protection d'un disque GPT.
        /// En mode Legacy, certains BIOS (dont celui du Dell en AHCI) ne lancent un disque que
        /// si une partition y est marquee active ; un disque GPT n'en a jamais. Marquer l'entree
        /// de protection (type EE) suffit. Seul l'octet 446 du secteur 0 change : la table GPT,
        /// les partitions et le code de demarrage restent intacts. L'UEFI doit ignorer ce drapeau.
        /// </summary>
        private static void Amorce(string reste, List<string> o)
        {
            string[] m = reste.Split(' ');
            if (m.Length < 1 || m[0] == "") { o.Add("Usage : ssd amorce sata0 [actif|inactif]"); return; }
            BlockDevice d = Trouver(m[0], out string err);
            if (d == null) { o.Add("Erreur : " + err); return; }

            byte[] s = new byte[512];
            d.ReadBlock(0, 1, ref s);
            if (s[510] != 0x55 || s[511] != 0xAA) { o.Add("Erreur : pas de secteur de demarrage valide sur " + m[0]); return; }
            if (s[450] != 0xEE) { o.Add("Erreur : " + m[0] + " n'est pas un disque GPT : rien a changer ici."); return; }

            bool actif = s[446] == 0x80;
            if (m.Length < 2)
            {
                o.Add(m[0] + " : MBR de protection " + (actif ? "ACTIF (le BIOS peut le demarrer en Legacy)" : "inactif"));
                o.Add("Code de demarrage Legacy : " + CodeAmorce(s));
                return;
            }

            string voulu = m[1].ToLower();
            if (voulu != "actif" && voulu != "inactif") { o.Add("Usage : ssd amorce sata0 [actif|inactif]"); return; }
            byte valeur = voulu == "actif" ? (byte)0x80 : (byte)0x00;
            if (s[446] == valeur) { o.Add("Deja " + voulu + " : rien a faire."); return; }

            s[446] = valeur;
            d.WriteBlock(0, 1, ref s);
            if (d is Ahci a) a.Vider();
            byte[] verif = new byte[512];
            d.ReadBlock(0, 1, ref verif);
            if (verif[446] != valeur) { o.Add("Erreur : l'ecriture n'a pas ete confirmee par le disque."); return; }

            o.Add("Fait : MBR de protection " + voulu + " (seul l'octet 446 du secteur 0 a change).");
            if (valeur == 0x80)
            {
                o.Add("Redemarrez : F2 > Boot List Option : Legacy, puis Internal HDD.");
                o.Add("Si Windows ne demarrait plus en UEFI : demarrez sur la cle et tapez");
                o.Add("  ssd amorce " + m[0] + " inactif");
            }
        }

        private static string CodeAmorce(byte[] s)
        {
            bool vide = true;
            for (int i = 0; i < 440 && vide; i++) if (s[i] != 0) vide = false;
            if (vide) return "aucun (le BIOS n'a rien a lancer)";
            string texte = "";
            for (int i = 0; i < 440; i++) texte += s[i] >= 32 && s[i] < 127 ? (char)s[i] : ' ';
            if (texte.IndexOf("GRUB") >= 0) return "present (GRUB, installe par installer-ssd.ps1)";
            if (texte.IndexOf("Invalid partition table") >= 0) return "celui de Windows (lancez installer-ssd.ps1 sous Windows)";
            return "present (programme inconnu)";
        }

        public const string FichierTest = "FSOS-TEST.TXT";

        /// <summary>
        /// TEST D'ECRITURE DE BOUT EN BOUT sur la partition FSOS : trouve la partition, y ecrit un
        /// fichier, le relit avec un montage tout neuf (rien en memoire), compare octet par octet,
        /// et le laisse en place : sous Windows, on doit le retrouver sur le lecteur FSOS.
        /// </summary>
        private static void Test(List<string> o)
        {
            o.Add("=== TEST D'ECRITURE - " + Services.OsName + " " + Services.Version + " ===");
            o.Add("0. Disques : " + Ahci.Disques.Count + " SATA, " + Nvme.Disques.Count + " NVMe. Mode Live : " + Live.Status);

            BlockDevice d = null;
            PartitionInfo cible = null;
            string nomDisque = "";
            int total = Ahci.Disques.Count + Nvme.Disques.Count;
            for (int k = 0; k < total && cible == null; k++)
            {
                BlockDevice disque;
                string nom;
                if (k < Ahci.Disques.Count) { disque = Ahci.Disques[k]; nom = "sata" + k; }
                else { disque = Nvme.Disques[k - Ahci.Disques.Count]; nom = "nvme" + (k - Ahci.Disques.Count); }
                List<PartitionInfo> parts;
                try { parts = Gpt.Lire(disque, out bool gpt); }
                catch (Exception) { continue; }
                for (int i = 0; i < parts.Count; i++)
                {
                    if (parts[i].Type.StartsWith("EFI")) continue;
                    var v = FatDirect.Monter(disque, parts[i].Debut, nom, out string e);
                    if (v != null && v.EstFsos) { d = disque; cible = parts[i]; nomDisque = nom; break; }
                }
            }
            if (cible == null)
            {
                o.Add("1. ECHEC : aucune partition FAT32 nommee FSOS (BIOS en AHCI ? partition renommee ?)");
                return;
            }
            o.Add("1. Partition FSOS : " + nomDisque + " partition " + cible.Numero + ", " + Fmt.Size(cible.Octets) +
                  ", a partir du secteur " + cible.Debut);

            string texte = "Ecrit par " + Services.OsName + " " + Services.Version +
                           " : si vous lisez ceci sous Windows, l'ecriture fonctionne.";
            byte[] data = new byte[texte.Length];
            for (int i = 0; i < texte.Length; i++) data[i] = (byte)texte[i];

            var w = FatDirect.Monter(d, cible.Debut, nomDisque, out string err);
            if (w == null) { o.Add("2. ECHEC : montage impossible (" + err + ")"); return; }
            w.Ecriture = true;
            try { w.Ecrire(FichierTest, data); }
            catch (Exception ex) { o.Add("2. ECHEC de l'ecriture : " + ex.Message); return; }
            o.Add("2. Ecriture de " + FichierTest + " (" + data.Length + " octets) : faite, cache du SSD vide");

            var r = FatDirect.Monter(d, cible.Debut, nomDisque, out err);
            byte[] relu;
            try { relu = r.Lire(FichierTest); }
            catch (Exception ex) { o.Add("3. ECHEC de la relecture : " + ex.Message); return; }
            bool identique = relu != null && relu.Length == data.Length;
            for (int i = 0; identique && i < data.Length; i++) if (relu[i] != data[i]) identique = false;
            o.Add(identique ? "3. Relecture avec un montage neuf : identique, octet par octet"
                            : "3. ECHEC : le fichier relu est different de celui ecrit");

            if (d is Ahci a)
                o.Add("4. SSD : " + a.Commandes + " commandes, " + a.Erreurs + " erreur(s), " + a.Reparations +
                      " reparation(s), cache vide " + a.Vidages + " fois");

            if (identique)
            {
                o.Add("RESULTAT : l'ecriture fonctionne.");
                o.Add("Sous Windows, apres un REDEMARRAGE complet (demarrage rapide desactive),");
                o.Add("ouvrez le lecteur FSOS : le fichier " + FichierTest + " doit y etre.");
            }
            else o.Add("RESULTAT : l'ecriture ne fonctionne pas. Prenez cet ecran en photo.");
        }

        private static void Secteur(string reste, List<string> o)
        {
            string[] m = reste.Split(' ');
            if (m.Length < 2) { o.Add("Usage : ssd secteur sata0 <numero>"); return; }
            BlockDevice d = Trouver(m[0], out string err);
            if (d == null) { o.Add("Erreur : " + err); return; }
            ulong n = (ulong)Fmt.ParseInt(m[1]);
            byte[] s = new byte[512];
            d.ReadBlock(n, 1, ref s);
            for (int l = 0; l < 512; l += 32)
            {
                string h = Fmt.Pad(l.ToString(), 4) + " ";
                string a = "";
                for (int i = 0; i < 32; i++)
                {
                    byte b = s[l + i];
                    h += Fmt.Hex(b, 2);
                    a += b >= 32 && b < 127 ? (char)b : '.';
                }
                o.Add(h + "  " + a);
            }
        }

        // ---------------- Fichiers ----------------

        private static void Lister(string reste, List<string> o)
        {
            Exiger();
            string ch = Chemin(reste);
            var l = Volume.Lister(ch);
            o.Add("Contenu de \\" + ch + " (" + l.Count + " element(s))");
            long total = 0;
            for (int i = 0; i < l.Count; i++)
            {
                EntreeFat e = l[i];
                total += e.Taille;
                o.Add("  " + (e.Dossier ? "[dossier] " : "          ") + Fmt.Pad(e.Nom, 32) +
                      (e.Dossier ? Fmt.Pad("", 10) : Fmt.Pad(Fmt.Size(e.Taille), 10)) + e.DateTexte +
                      (e.Cache ? "  cache" : "") + (e.Systeme ? "  systeme" : ""));
            }
            o.Add("  total " + Fmt.Size(total));
        }

        private static void Cd(string reste, List<string> o)
        {
            Exiger();
            if (reste == "\\" || reste == "/" || reste.Length == 0) { Courant = ""; o.Add("\\"); return; }
            if (reste == "..")
            {
                int k = Courant.LastIndexOf('\\');
                Courant = k < 0 ? "" : Courant.Substring(0, k);
                o.Add("\\" + Courant);
                return;
            }
            string ch = Chemin(reste);
            var e = Volume.Info(ch);
            if (e == null || !e.Dossier) { o.Add("Erreur : dossier introuvable"); return; }
            int debut = 0;
            while (debut < ch.Length && (ch[debut] == '\\' || ch[debut] == '/')) debut++;
            Courant = ch.Substring(debut);
            o.Add("\\" + Courant);
        }

        private static void LireTexte(string reste, List<string> o)
        {
            Exiger();
            byte[] b = Volume.Lire(Chemin(reste));
            var lignes = TextCodec.Decode(b, b.Length);
            int max = lignes.Count < 80 ? lignes.Count : 80;
            for (int i = 0; i < max; i++) o.Add(lignes[i]);
            if (lignes.Count > max) o.Add("... (" + (lignes.Count - max) + " lignes de plus)");
        }

        private static void EcrireTexte(string reste, List<string> o)
        {
            Exiger();
            int sp = reste.IndexOf(' ');
            if (sp <= 0) { o.Add("Usage : ssd ecrire <fichier> <texte>"); return; }
            string ch = Chemin(reste.Substring(0, sp));
            var une = new List<string>();
            une.Add(reste.Substring(sp + 1));
            byte[] data = TextCodec.Encode(une);
            Volume.Ecrire(ch, data);
            o.Add("Ecrit et verifie : \\" + ch + " (" + data.Length + " octets)");
        }

        private static void Copier(string reste, List<string> o)
        {
            Exiger();
            int sp = reste.IndexOf(' ');
            if (sp <= 0) { o.Add("Usage : ssd copier <fichier de l'OS> <fichier du SSD>"); return; }
            string source = reste.Substring(0, sp), dest = Chemin(reste.Substring(sp + 1).Trim());
            if (!File.Exists(source)) { o.Add("Erreur : fichier de l'OS introuvable : " + source); return; }
            byte[] data = File.ReadAllBytes(source);
            Volume.Ecrire(dest, data);
            o.Add("Copie sur le SSD : " + source + " -> \\" + dest + " (" + Fmt.Size(data.Length) + ")");
        }

        private static void Importer(string reste, List<string> o)
        {
            Exiger();
            int sp = reste.IndexOf(' ');
            if (sp <= 0) { o.Add("Usage : ssd importer <fichier du SSD> <fichier de l'OS>"); return; }
            string source = Chemin(reste.Substring(0, sp)), dest = reste.Substring(sp + 1).Trim();
            byte[] data = Volume.Lire(source);
            FsTools.WriteBytes(dest, data);
            Live.SaveIfActive();
            o.Add("Copie dans l'OS : \\" + source + " -> " + dest + " (" + Fmt.Size(data.Length) + ")");
        }

        // ---------------- Installation de \FSOS sur le SSD ----------------

        private static int fichiers, dossiers;
        private static long octets;

        private static void Installer(List<string> o)
        {
            Exiger();
            if (!Volume.Ecriture)
            {
                o.Add("Erreur : volume en lecture seule. Ouvrez-le en ecriture : ssd monter sata0 <n> ecriture");
                return;
            }

            fichiers = 0; dossiers = 0; octets = 0;
            Volume.CreerDossier("FSOS");
            string racine = InstallerWindow.FindRoot();
            if (racine != null)
            {
                // Un environnement existe deja dans l'OS (mode Live ou disque) : on le recopie
                o.Add("Copie de " + racine + " vers \\FSOS du SSD...");
                CopierDossier(racine, "FSOS", o);
            }
            else
            {
                // Demarrage sur cle sans environnement en memoire : on cree directement sur le SSD
                // l'environnement standard, exactement comme l'installateur graphique.
                o.Add("Creation de l'environnement \\FSOS sur le SSD...");
                CreerDossierSsd("FSOS\\Systeme");
                CreerDossierSsd("FSOS\\Applications");
                CreerDossierSsd("FSOS\\Documents");
                CreerDossierSsd("FSOS\\Images");
                CreerDossierSsd("FSOS\\Utilisateurs");
                EcrireTexteSsd("FSOS\\Systeme\\version.txt", Samples.Version);
                EcrireTexteSsd("FSOS\\Documents\\Bienvenue.txt", Samples.Welcome);
                EcrireTexteSsd("FSOS\\Applications\\Bonjour.fsl", Samples.Hello);
                EcrireTexteSsd("FSOS\\Applications\\Compteur.fsl", Samples.Counter);
                EcrireTexteSsd("FSOS\\Applications\\Nul.fsl", Samples.Nul);
                EcrireTexteSsd("FSOS\\Applications\\Fenetre.fsl", Samples.Fenetre);
                EcrireTexteSsd("FSOS\\Applications\\Balle.fsl", Samples.Balle);
                EcrireTexteSsd("FSOS\\Applications\\Calculs.fsl", Samples.Calculs);
                CreerDossierSsd("FSOS\\Systeme\\Pilotes");
                EcrireTexteSsd("FSOS\\Systeme\\Pilotes\\HautParleur.config", PilotesFsl.ConfigHautParleur);
                EcrireTexteSsd("FSOS\\Systeme\\Pilotes\\HautParleur.fsl", PilotesFsl.ProgHautParleur);
                EcrireTexteSsd("FSOS\\Systeme\\Pilotes\\HorlogeCmos.config", PilotesFsl.ConfigHorloge);
                EcrireTexteSsd("FSOS\\Systeme\\Pilotes\\HorlogeCmos.fsl", PilotesFsl.ProgHorloge);
                for (int i = 0; i < Config.Files.Length; i++)
                    EcrireTexteSsd("FSOS\\Systeme\\" + Config.Files[i], Config.Default(Config.Files[i]));
            }
            o.Add("Termine : " + dossiers + " dossier(s), " + fichiers + " fichier(s), " + Fmt.Size(octets) +
                  ", tout verifie octet par octet.");
            o.Add("Depuis Windows, ouvrez le lecteur FSOS : le dossier FSOS doit y apparaitre.");
            Journal.Ajouter("installation sur SSD", Comptes.Actuel?.Nom);
        }

        /// <summary>Installe l'environnement \\FSOS sur un volume choisi (assistant d'installation).</summary>
        public static void InstallerDans(FatDirect v, List<string> o)
        {
            Volume = v;
            Courant = "";
            Installer(o);
        }

        private static void CreerDossierSsd(string chemin)
        {
            Volume.CreerDossier(chemin);                 // ne fait rien si le dossier existe deja
            dossiers++;
        }

        private static void EcrireTexteSsd(string chemin, string texte)
        {
            byte[] b = new byte[texte.Length];
            for (int i = 0; i < texte.Length; i++) b[i] = texte[i] < 128 ? (byte)texte[i] : (byte)'?';
            Volume.Ecrire(chemin, b);
            fichiers++;
            octets += b.Length;
        }

        private static void CopierDossier(string source, string dest, List<string> o)
        {
            var liste = VFSManager.GetDirectoryListing(source);
            for (int i = 0; i < liste.Count; i++)
            {
                string nom = liste[i].mName;
                string src = source + "\\" + nom, dst = dest + "\\" + nom;
                if (liste[i].mEntryType == DirectoryEntryTypeEnum.Directory)
                {
                    Volume.CreerDossier(dst);
                    dossiers++;
                    CopierDossier(src, dst, o);
                }
                else if (liste[i].mEntryType == DirectoryEntryTypeEnum.File)
                {
                    byte[] data = File.ReadAllBytes(src);
                    Volume.Ecrire(dst, data);
                    fichiers++;
                    octets += data.Length;
                    if (fichiers % 10 == 0) o.Add("  " + fichiers + " fichiers copies...");
                }
            }
        }
    }
}
