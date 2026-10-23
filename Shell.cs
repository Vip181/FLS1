using System;
using System.Collections.Generic;
using Cosmos.System.FileSystem.Listing;
using Cosmos.System.FileSystem.VFS;
using Cosmos.Core.Memory;
using System.IO;
using FileSystemOS.Fsl;
using FileSystemOS.Graphique;
using FileSystemOS.Memoire;
using FileSystemOS.Processus;
using FileSystemOS.Systeme;
using FileSystemOS.Utils;
using Sys = Cosmos.System;

namespace FileSystemOS
{
    public static unsafe class Shell
    {
        public static void Run(string line, List<string> o)
        {
            string cmd = line, arg = "";
            int sp = line.IndexOf(' ');
            if (sp >= 0)
            {
                cmd = line.Substring(0, sp);
                arg = line.Substring(sp + 1).Trim();
            }
            cmd = cmd.ToLower();

            if (cmd == "aide") Help(o);
            else if (cmd == "coucou") WriteText("coucou", o);
            else if (cmd == "ecrire")
            {
                if (arg.Length == 0) o.Add("Erreur : usage -> ecrire <texte>");
                else WriteText(arg, o);
            }
            else if (cmd == "lire") Read(o);
            else if (cmd == "dump") Dump(o);
            else if (cmd == "mem") Mem(o);
            else if (cmd == "accel") Accel(o);
            else if (cmd == "ps") Ps(o);
            else if (cmd == "buffers") Buffers(o);
            else if (cmd == "disques") Disks(o);
            else if (cmd == "dir") Dir(arg, o);
            else if (cmd == "ouvrir") OpenFile(arg, o);
            else if (cmd == "creer") CreateFile(arg, o);
            else if (cmd == "fslc") CompileFsl(arg, o);
            else if (cmd == "decompiler") Decompiler(arg, o);
            else if (cmd == "fslverifier") VerifierFsl(arg, o);
            else if (cmd == "pilotes") ListePilotes(o);
            else if (cmd == "guide")
            {
                try
                {
                    string c = FileSystemOS.Systeme.GuideFsl.Creer();
                    Services.PdfWin.Load(c);
                    Services.Windows.Open(Services.PdfWin);
                    o.Add("Guide du File Syst Langage : " + c + " (lisible aussi sous Windows)");
                }
                catch (Exception e) { o.Add("Erreur : " + e.Message); }
            }
            else if (cmd == "createur") { Services.Windows.Open(Services.StudioWin); o.Add("Createur de programmes ouvert"); }
            else if (cmd == "pilote") CommandePilote(arg, o);
            else if (cmd == "run") RunFsl(arg, o);
            else if (cmd == "demo") Demo(o);
            else if (cmd == "demoverrou") DemoVerrou(o);
            else if (cmd == "kill") Kill(arg, o);
            else if (cmd == "curseur") Cursor(o);
            else if (cmd == "heure") o.Add("Il est " + Services.ClockText);
            else if (cmd == "live") o.Add("Mode Live : " + Live.Status);
            else if (cmd == "sauver") o.Add("Session : " + Live.Save());
            else if (cmd == "capture" || cmd == "screenshot")
            {
                o.Add(Capture.Prendre(arg == "ouvrir"));
                o.Add("Raccourci : touche Impr. ecran  (Maj + Impr. ecran = ouvrir l'image)");
            }
            else if (cmd == "captures")
            {
                var l = Capture.Liste();
                for (int i = 0; i < l.Count; i++) o.Add(l[i]);
            }
            else if (cmd == "batterie" || cmd == "energie")
            {
                var e = Energie.Etat();
                for (int i = 0; i < e.Count; i++) o.Add(e[i]);
                Services.Windows.Open(Services.EnergieWin);
            }
            else if (cmd == "wifi")
            {
                var l = FileSystemOS.Pilotes.Wifi.Rechercher();
                for (int i = 0; i < l.Count; i++) o.Add(l[i]);
            }
            else if (cmd == "sentinelle")
            {
                var e = Sentinelle.Etat();
                for (int i = 0; i < e.Count; i++) o.Add(e[i]);
            }
            else if (cmd == "calc" || cmd == "calculatrice") { Services.Windows.Open(Services.CalcWin); o.Add("Calculatrice ouverte."); }
            else if (cmd == "serpent" || cmd == "jeu") { Services.Windows.Open(Services.SerpentWin); o.Add("Serpent : fleches pour diriger, Entree pour rejouer."); }
            else if (cmd == "taches")
            {
                Services.Windows.Open(Services.TacheWin);
                o.Add("Gestionnaire des taches ouvert (raccourci : Ctrl + Alt + Suppr)");
            }
            else if (cmd == "deconnexion" || cmd == "deconnecter") { Session.Deconnecter(); }
            else if (cmd == "journal" || cmd == "connexions")
            {
                o.Add("Journal des connexions : " + Journal.Fichier);
                o.Add("Support actuel : " + Journal.Support());
                int n = Fmt.ParseInt(arg);
                var l = Journal.Dernieres(n > 0 ? n : 15);
                for (int i = 0; i < l.Count; i++) o.Add("  " + l[i]);
            }
            else if (cmd == "qui") o.Add("Connecte : " + (Comptes.Actuel == null ? "personne" : Comptes.Actuel.Nom + (Comptes.Actuel.Admin ? " (admin)" : "")));
            else if (cmd == "comptes")
            {
                for (int i = 0; i < Comptes.Liste.Count; i++)
                    o.Add("  " + Comptes.Liste[i].Nom + (Comptes.Liste[i].Admin ? "  (admin)" : ""));
                Services.Windows.Open(Services.AccountsWin);
            }
            else if (cmd == "verrouiller") Session.Lock();
            else if (cmd == "pdf")
            {
                if (arg.Length == 0) o.Add("Erreur : usage -> pdf 0:\\document.pdf");
                else { Services.PdfWin.Load(arg); Services.Windows.Open(Services.PdfWin); o.Add("PDF ouvert : " + arg); }
            }
            else if (cmd == "textwar")
            {
                Services.Windows.Open(Services.TextWarWin);
                o.Add("TextWar ouvert" + (arg.Length > 0 ? " (collez l'adresse : " + arg + ")" : ""));
            }
            else if (cmd == "telechargements")
            {
                string f = Downloads.Folder();
                o.Add("Dossier : " + (f ?? "aucun disque"));
                if (f != null) { Services.ExplorerWin.Navigate(f, true); Services.Windows.Open(Services.ExplorerWin); }
            }
            else if (cmd == "web")
            {
                Services.Windows.Open(Services.BrowserWin);
                if (arg.Length > 0) Services.BrowserWin.Navigate(arg, true);
                o.Add("Navigateur : " + (arg.Length > 0 ? arg : Http.Home));
            }
            else if (cmd == "ipc" || cmd == "messages")
            {
                var e = Ipc.Etat();
                for (int i = 0; i < e.Count; i++) o.Add(e[i]);
                o.Add("Envoyer : envoyer <processus> <texte>");
            }
            else if (cmd == "envoyer")
            {
                int sp2 = arg.IndexOf(' ');
                if (sp2 <= 0) o.Add("Erreur : usage -> envoyer <processus> <texte>");
                else
                {
                    string dest = arg.Substring(0, sp2), txt = arg.Substring(sp2 + 1);
                    o.Add(Ipc.Envoyer(null, dest, txt) ? "Message depose dans la boite de " + dest
                                                       : "Erreur : processus '" + dest + "' introuvable ou boite pleine");
                }
            }
            else if (cmd == "verrous")
            {
                var e = Verrou.Etat2();
                for (int i = 0; i < e.Count; i++) o.Add(e[i]);
            }
            else if (cmd == "quantum")
            {
                int q = Fmt.ParseInt(arg);
                if (q >= 1 && q <= 200) { Scheduler.Quantum = (uint)q; o.Add("Tranche de temps : " + q + " ms"); }
                else o.Add("Tranche actuelle : " + Scheduler.Quantum + " ms   (quantum 1..200)");
            }
            else if (cmd == "pages")
            {
                var e = Memoire.Pages.Etat();
                for (int i = 0; i < e.Count; i++) o.Add(e[i]);
                if (arg == "detail")
                {
                    o.Add("index  adresse     droits            processus             thread      programme");
                    var d = Memoire.Pages.Detail(40);
                    for (int i = 0; i < d.Count; i++) o.Add("  " + d[i]);
                }
                else o.Add("(pages detail = liste page par page)");
            }
            else if (cmd == "ssd") Ssd.Commande(arg, o);
            else if (cmd == "installation")
            {
                FileSystemOS.Graphique.AssistantInstallation.OuvrirDepuisConsole();
                o.Add("Assistant d'installation ouvert.");
            }
            else if (cmd == "installer") Ssd.Commande("installer", o);      // raccourci de "ssd installer"
            else if (cmd == "monterdisque")
            {
                var c1 = FileSystemOS.Pilotes.Ahci.Caches;
                var c2 = FileSystemOS.Pilotes.Nvme.Caches;
                if (arg.Length == 0)
                {
                    o.Add("Disques gardes de cote (non confies a Cosmos) :");
                    for (int i = 0; i < c1.Count; i++) o.Add("  sata" + i + " : " + c1[i].Modele + " " + Fmt.Size(c1[i].Octets));
                    for (int i = 0; i < c2.Count; i++) o.Add("  nvme" + i + " : " + c2[i].Modele + " " + Fmt.Size(c2[i].Octets));
                    if (c1.Count + c2.Count == 0) o.Add("  aucun");
                    o.Add("Pour forcer : monterdisque sata0   (ATTENTION : peut figer l'OS)");
                    o.Add("Ces disques ne contiennent pas de partition FAT (Windows en GPT/NTFS).");
                }
                else
                {
                    o.Add("ATTENTION : Cosmos ne lit que le FAT ; le montage peut figer le systeme.");
                    o.Add("Le chien de garde redemarrera la machine au bout de 8 s si cela arrive.");
                    Cosmos.HAL.BlockDevice.BlockDevice d2 = null;
                    if (arg.StartsWith("sata")) { int k = Fmt.ParseInt(arg.Substring(4)); if (k >= 0 && k < c1.Count) d2 = c1[k]; }
                    else if (arg.StartsWith("nvme")) { int k = Fmt.ParseInt(arg.Substring(4)); if (k >= 0 && k < c2.Count) d2 = c2[k]; }
                    if (d2 == null) o.Add("Erreur : disque inconnu (monterdisque sans argument pour la liste)");
                    else
                    {
                        try
                        {
                            if (!Cosmos.HAL.BlockDevice.BlockDevice.Devices.Contains(d2))
                                Cosmos.HAL.BlockDevice.BlockDevice.Devices.Add(d2);
                            // Selon la version de Cosmos : Initialize(true) ou Initialize()
                            Services.FileSystem.Initialize(true);
                            o.Add("Disque monte. Tapez disques pour voir les lecteurs.");
                        }
                        catch (Exception e) { o.Add("Erreur : " + e.Message); }
                    }
                }
            }
            else if (cmd == "stockage")
            {
                var r = Stockage.Rapport();
                for (int i = 0; i < r.Count; i++) o.Add(r[i]);
                o.Add("-> " + Stockage.Conseil());
                o.Add("Controleurs USB :");
                var u = Stockage.Usb();
                for (int i = 0; i < u.Count; i++) o.Add(u[i]);
            }
            else if (cmd == "machine")
            {
                var r = FileSystemOS.Pilotes.Machine.Report();
                for (int i = 0; i < r.Count; i++) o.Add(r[i]);
            }
            else if (cmd == "materiel") { var r = Materiel.Report(); for (int i = 0; i < r.Count; i++) o.Add(r[i]); }
            else if (cmd == "ecrans" || cmd == "hdmi")
            {
                var e = FileSystemOS.Pilotes.IntelGraphics.Analyser(arg != "rapide");
                for (int i = 0; i < e.Count; i++) o.Add(e[i]);
                o.Add("(ecrans rapide = sans lecture de l'EDID)");
            }
            else if (cmd == "partitions")
            {
                var tous = Cosmos.HAL.BlockDevice.BlockDevice.Devices;
                int n = 0;
                for (int i = 0; i < tous.Count; i++)
                {
                    var l = FileSystemOS.Pilotes.Gpt.Rapport(tous[i], "Disque " + (++n));
                    for (int k = 0; k < l.Count; k++) o.Add(l[k]);
                }
                var c1 = FileSystemOS.Pilotes.Ahci.Caches;
                for (int i = 0; i < c1.Count; i++)
                {
                    var l = FileSystemOS.Pilotes.Gpt.Rapport(c1[i], "sata" + i + " (garde de cote) " + c1[i].Modele);
                    for (int k = 0; k < l.Count; k++) o.Add(l[k]);
                }
                var c2 = FileSystemOS.Pilotes.Nvme.Caches;
                for (int i = 0; i < c2.Count; i++)
                {
                    var l = FileSystemOS.Pilotes.Gpt.Rapport(c2[i], "nvme" + i + " (garde de cote) " + c2[i].Modele);
                    for (int k = 0; k < l.Count; k++) o.Add(l[k]);
                }
            }
            else if (cmd == "formater")
            {
                o.Add("ATTENTION : cette commande EFFACE une partition et y cree un FAT32 vide.");
                o.Add("Usage : formater sata0 2       (disque garde de cote, numero de partition)");
                string[] mots = arg.Split(' ');
                if (mots.Length < 2) o.Add("Indiquez le disque et le numero de partition.");
                else
                {
                    Cosmos.HAL.BlockDevice.BlockDevice d3 = null;
                    var ca = FileSystemOS.Pilotes.Ahci.Caches;
                    var cn = FileSystemOS.Pilotes.Nvme.Caches;
                    if (mots[0].StartsWith("sata")) { int k = Fmt.ParseInt(mots[0].Substring(4)); if (k >= 0 && k < ca.Count) d3 = ca[k]; }
                    else if (mots[0].StartsWith("nvme")) { int k = Fmt.ParseInt(mots[0].Substring(4)); if (k >= 0 && k < cn.Count) d3 = cn[k]; }
                    int num = Fmt.ParseInt(mots[1]);
                    if (d3 == null) o.Add("Erreur : disque inconnu (voir monterdisque).");
                    else
                    {
                        var parts = FileSystemOS.Pilotes.Gpt.Lire(d3, out bool gpt);
                        FileSystemOS.Pilotes.PartitionInfo cible = null;
                        for (int i = 0; i < parts.Count; i++) if (parts[i].Numero == num) cible = parts[i];
                        if (cible == null) o.Add("Erreur : partition " + num + " introuvable (voir partitions).");
                        else
                        {
                            try
                            {
                                o.Add("Formatage de " + cible.Type + " " + Fmt.Size(cible.Octets) + "...");
                                o.Add(Fat32.Formater(d3, cible.Debut, cible.Secteurs, "FSOS"));
                                o.Add("Termine. Faites maintenant : monterdisque " + mots[0]);
                            }
                            catch (Exception e) { o.Add("Erreur : " + e.Message); }
                        }
                    }
                }
            }
            else if (cmd == "resolution")
            {
                if (arg.Length == 0)
                {
                    o.Add("Actuelle : " + Services.ScreenW + "x" + Services.ScreenH + " (" + Materiel.Choix + ")");
                    o.Add("Changer : resolution 1280x720  ou  resolution auto  (puis redemarrer)");
                }
                else o.Add("Resolution " + arg + " : " + Config.SetAndSave("demarrage.cfg", "resolution", arg.ToLower()) +
                           " - redemarrez pour l'appliquer");
            }
            else if (cmd == "fond")
            {
                if (arg.Length == 0)
                {
                    o.Add("Fond actuel : " + Desktop.WallpaperInfo);
                    o.Add("  fond ciel | nuit | aurore | lagon | foret | sable | graphite   (instantanes)");
                    o.Add("  fond vagues    (calcule en arriere-plan)");
                    o.Add("  fond <image>   JPEG, PNG, BMP : preparee une fois en arriere-plan, puis gardee prete");
                }
                else o.Add("Fond d'ecran : " + Desktop.SetWallpaper(arg == "aucun" ? "ciel" : arg));
            }
            else if (cmd == "image")
            {
                if (arg.Length == 0) o.Add("Erreur : usage -> image 0:\\photo.bmp");
                else { Services.ImageWin.Load(arg); Services.Windows.Open(Services.ImageWin); o.Add("Image ouverte : " + arg); }
            }
            else if (cmd == "apropos" || cmd == "version")
            {
                Services.Windows.Open(Services.AboutWin);
                o.Add(Services.OsName + " version " + Services.Version);
                o.Add("Editeur : " + Services.Company);
                o.Add("Createur : " + Services.Author);
                o.Add("Copie interdite de l'OS.");
                o.Add("OS Open Source : veuillez mentionner la version et l'OS en cas de toute modification.");
                o.Add("Langage integre : File Syst Langage (FSL)");
            }
            else if (cmd == "arreter") Alimentation.Arreter();
            else if (cmd == "redemarrer") Alimentation.Redemarrer();
            else if (cmd == "reboot")
            {
                o.Add("Redemarrage immediat (sans ecran d'arret)...");
                Redemarrage.Maintenant();
                o.Add("Echec : derniere methode essayee = " + Redemarrage.Derniere);
            }
            else if (cmd == "chiendegarde" || cmd == "watchdog")
            {
                if (arg == "non" || arg == "off") { FileSystemOS.Noyau.Watchdog.RedemarrageAuto = 0; o.Add("Redemarrage automatique desactive."); }
                else
                {
                    int sec = Fmt.ParseInt(arg);
                    if (sec > 0 && sec <= 600) { FileSystemOS.Noyau.Watchdog.RedemarrageAuto = sec; o.Add("Redemarrage automatique apres " + sec + " s de blocage."); }
                    else o.Add("Chien de garde : bandeau rouge apres 3 s, redemarrage apres " +
                               FileSystemOS.Noyau.Watchdog.RedemarrageAuto + " s.  (chiendegarde <secondes> | non)");
                }
            }
            else if (cmd == "reseau" || cmd == "ip")
            {
                var r = Reseau.Report();
                for (int i = 0; i < r.Count; i++) o.Add(r[i]);
            }
            else if (cmd == "connecter")
            {
                o.Add("Connexion : " + Reseau.Init(Config.GetBool("dhcp", true)));
            }
            else if (cmd == "peripheriques" || cmd == "lspci")
            {
                var all = FileSystemOS.Pilotes.PciCatalogue.Scan();
                o.Add(all.Count + " peripherique(s) PCI :");
                for (int i = 0; i < all.Count; i++)
                {
                    var p = all[i];
                    o.Add("  " + p.Location + " [" + p.Id + "] " + p.VendorName + " - " + p.Kind +
                          (p.Network ? " : " + p.Name + " (" + p.Driver + ")" : ""));
                }
            }
            else if (cmd == "demarrage")
            {
                o.Add("Journal du chargement :");
                for (int i = 0; i < Services.BootLog.Count; i++) o.Add("  " + Services.BootLog[i]);
            }
            else if (cmd == "session")
            {
                if (arg == "enregistrer") o.Add(FileSystemOS.Systeme.SessionSsd.ToutEnregistrer());
                else if (arg == "comparer") o.Add(FileSystemOS.Systeme.SessionSsd.Sauver());
                else
                {
                    o.Add("Session du SSD : " + FileSystemOS.Systeme.SessionSsd.Etat);
                    o.Add("En attente d'enregistrement : " + FileSystemOS.Systeme.SessionSsd.EnAttente + " operation(s)");
                    o.Add("Noyau sur le SSD : " + (FileSystemOS.Systeme.SessionSsd.VersionNoyauSsd ?? "inconnu"));
                    o.Add("  session enregistrer   tout ecrire sur le SSD maintenant");
                    o.Add("  session comparer      comparer toute la session au SSD (lent)");
                }
            }
            else if (cmd == "attente" || cmd == "reprendre") AttenteOuReprise(cmd == "attente", arg, o);
            else if (cmd == "threads") Threads(arg, o);
            else if (cmd == "fonctions")
            {
                o.Add("Fonctions integrees du File Syst Langage :");
                for (int i = 0; i < FileSystemOS.Fsl.Natives.Aides.Length; i++) o.Add("  " + FileSystemOS.Fsl.Natives.Aides[i]);
            }
            else if (cmd == "connexion")
            {
                string[] m = arg.Split(' ');
                if (m.Length >= 2 && m[0] == "auto" && (m[1] == "oui" || m[1] == "non"))
                {
                    o.Add("Connexion automatique au demarrage : " + m[1] + " (" +
                          Config.SetAndSave("demarrage.cfg", "connexion_auto", m[1]) + ")");
                    FileSystemOS.Systeme.Live.SaveIfActive();
                }
                else o.Add("Usage : connexion auto oui|non   (actuellement : " +
                           (Config.GetBool("connexion_auto", true) ? "oui" : "non") + ")");
            }
            else if (cmd == "config")
            {
                var d = Config.Dump();
                o.Add("Fichiers internes (\\FSOS\\Systeme) : " + d.Count + " reglage(s)");
                for (int i = 0; i < d.Count; i++) o.Add("  " + d[i]);
            }
            else if (cmd == "perf")
            {
                o.Add("Tours d'ordonnanceur / s : " + Services.Tps);
                o.Add("Images / s               : " + Services.Fps);
                o.Add("Cogestion : " + FileSystemOS.Noyau.Cogestion.Renders + " rendus, " +
                      FileSystemOS.Noyau.Cogestion.Pending + " en attente, budget " + FileSystemOS.Noyau.Cogestion.Budget + "/tour");
            }
            else o.Add("Erreur : commande inconnue '" + cmd + "' (tapez aide)");
        }

        private static void Help(List<string> o)
        {
            o.Add("Commandes disponibles :");
            o.Add("  connexion auto oui|non   au demarrage : directement au bureau, ou page de connexion");
            o.Add("  coucou         ecrit \"coucou\" dans la memoire");
            o.Add("  ecrire <txt>   ecrit un texte dans la memoire");
            o.Add("  lire           relit tout ce qui a ete ecrit");
            o.Add("  dump           affiche la zone memoire en hexa");
            o.Add("  mem            etat de la memoire");
            o.Add("  accel          teste l'accelerateur memoire");
            o.Add("  ps             processus : utilisateur, etat, cpu, pages, threads");
            o.Add("  buffers        buffers graphiques en memoire");
            o.Add("  disques        lecteurs detectes");
            o.Add("  dir <chemin>   liste un dossier (ex: dir 0:\\)");
            o.Add("  ouvrir <fich>  ouvre un fichier dans l'editeur");
            o.Add("  creer <fich>   cree un fichier texte et l'ouvre");
            o.Add("  fslc <f.fsl>   compile un programme FSL en .fsc");
            o.Add("  run <fichier>  execute un programme .fsl ou .fsc");
            o.Add("  decompiler <f.fsc>   retrouve le code FSL d'un programme compile");
            o.Add("  fslverifier <f.fsl>  compile, decompile, recompile : le binaire doit etre identique");
            o.Add("  fonctions      toutes les fonctions integrees du langage");
            o.Add("  guide          refait et ouvre le guide du FSL (Documents\\Guide-FSL.pdf)");
            o.Add("  createur       Createur de programmes (sans ecrire de code)");
            o.Add("  pilotes        pilotes FSL (fichiers .config de \\FSOS\\Systeme\\Pilotes)");
            o.Add("  pilote demarrer|arreter|nouveau NOM, pilote exemples   (administrateur)");
            o.Add("  attente / reprendre PID [TID], threads PID   met en attente ou reprend");
            o.Add("  demo           lance un processus de demonstration");
            o.Add("  demoverrou     montre un interblocage detecte et evite");
            o.Add("  kill <pid>     arrete un processus (ou une fenetre)");
            o.Add("  curseur        le curseur en hexadecimal");
            o.Add("  heure          affiche l'heure");
            o.Add("  apropos        version, editeur et auteur");
            o.Add("  demarrage      journal du chargement de l'OS");
            o.Add("  config         reglages des fichiers internes");
            o.Add("  perf           vitesse du systeme et cogestion");
            o.Add("  reseau         cartes reseau, adresse IP, box, DNS");
            o.Add("  connecter      (re)connexion a la box par cable (DHCP)");
            o.Add("  peripheriques  liste et identifie tous les peripheriques PCI");
            o.Add("  materiel       processeur, memoire, ecran detectes");
            o.Add("  machine        marque, processeur, chipset, profil des pilotes");
            o.Add("  stockage       disques vus et pourquoi (mode BIOS SATA)");
            o.Add("  monterdisque [sata0]  monte un disque garde de cote (risque)");
            o.Add("  pages [detail] memoire paginee : pages par processus et attributs");
            o.Add("  ipc / envoyer <proc> <texte>  messages entre processus");
            o.Add("  verrous        verrous et interblocages evites");
            o.Add("  quantum [ms]   tranche de temps de l'ordonnanceur");
            o.Add("  web [adresse|recherche]  navigateur internet");
            o.Add("  pdf <fichier>  lecteur PDF        textwar  telecharger des textes");
            o.Add("  telechargements  ouvre le dossier des telechargements");
            o.Add("  qui / comptes  utilisateur connecte / comptes");
            o.Add("  verrouiller    verrouille la session (Ctrl + Alt + L)");
            o.Add("  deconnexion    ferme la session et revient a la page de connexion");
            o.Add("  capture [ouvrir] / captures   capture d'ecran (touche Impr. ecran)");
            o.Add("  taches         gestionnaire des taches (Ctrl + Alt + Suppr)");
            o.Add("  calc / serpent calculatrice, jeu du serpent (double tampon)");
            o.Add("  batterie       energie : duree, charge, etat de la batterie");
            o.Add("  wifi           cartes Wi-Fi et pourquoi les box ne sont pas listables");
            o.Add("  sentinelle     programmes corrompus refuses ou arretes");
            o.Add("  journal [n]    journal des connexions (fichier + support de demarrage)");
            o.Add("  live / sauver  mode Live et sauvegarde de la session");
            o.Add("  resolution [LxH|auto]  taille de l'ecran (au redemarrage)");
            o.Add("  ecrans [rapide]  sorties video, pipes du BIOS et ecrans (EDID)");
            o.Add("  partitions     tables MBR / GPT de tous les disques");
            o.Add("  ssd [aide]     gere les fichiers du SSD directement (sans Cosmos)");
            o.Add("  formater <disque> <n>  cree un FAT32 sur une partition (EFFACE tout)");
            o.Add("  fond [ciel|nuit|aurore|...|vagues|image] fond d'ecran (fond seul : la liste)");
            o.Add("  image <f.bmp>  ouvre une image");
            o.Add("  effacer        vide la console");
            o.Add("  arreter / redemarrer");
            o.Add("  reboot         redemarrage immediat (methodes materielles)");
            o.Add("  chiendegarde [s|non]   redemarrage automatique en cas de blocage");
            o.Add("Fleches, PageUp/PageDown ou la barre pour defiler.");
        }

        private static void WriteText(string text, List<string> o)
        {
            uint addr = Services.Memory.Write(text);
            if (addr == 0)
            {
                o.Add("Erreur : zone memoire pleine (4 Ko).");
                return;
            }
            o.Add("\"" + text + "\" ecrit a l'adresse 0x" + Fmt.Hex(addr));
            o.Add("Format : [longueur][octets ASCII]");
            var d = Services.Memory.DumpAt(addr, (uint)text.Length + 1);
            for (int i = 0; i < d.Count; i++) o.Add(d[i]);
        }

        private static void Read(List<string> o)
        {
            var all = Services.Memory.ReadAll();
            o.Add("Textes en memoire : " + all.Count);
            for (int i = 0; i < all.Count; i++) o.Add("  " + all[i]);
        }

        private static void Dump(List<string> o)
        {
            var m = Services.Memory;
            if (m.Used == 0) { o.Add("Zone vide."); return; }
            uint n = m.Used < 128 ? m.Used : 128;
            var d = m.DumpAt(m.BaseAddress, n);
            for (int i = 0; i < d.Count; i++) o.Add(d[i]);
        }

        private static void Mem(List<string> o)
        {
            var m = Services.Memory;
            var a = Services.Accelerator;
            o.Add("RAM detectee      : " + Cosmos.Core.CPU.GetAmountOfRAM() + " Mo");
            o.Add("Zone de textes    : 0x" + Fmt.Hex(m.BaseAddress) + "  " + m.Used + "/" + MemoryManager.ZoneSize + " octets");
            o.Add("Pool accelerateur : 0x" + Fmt.Hex(a.PoolAddress) + "  " + a.FreeBlocks + "/" + MemoryAccelerator.BlockCount + " blocs libres");
            o.Add("Pool              : hits=" + a.Hits + "  misses=" + a.Misses);
        }

        private static void Accel(List<string> o)
        {
            var a = Services.Accelerator;

            uint before = a.Hits;
            uint[] blocks = new uint[32];
            for (int i = 0; i < 32; i++) blocks[i] = (uint)a.Alloc(48);
            for (int i = 0; i < 32; i++) a.Free((byte*)blocks[i]);
            o.Add("32 allocations de 48 octets : " + (a.Hits - before) + " servies par le pool");

            byte* src = Heap.Alloc(4096);
            byte* dst = Heap.Alloc(4096);
            for (uint i = 0; i < 4096; i++) src[i] = (byte)(i * 7);
            MemoryAccelerator.Copy(dst, src, 4096);
            bool ok = true;
            for (uint i = 0; i < 4096; i++) if (dst[i] != src[i]) { ok = false; break; }
            o.Add("Copie rapide 4096 octets (32 bits) : " + (ok ? "OK" : "ECHEC"));

            MemoryAccelerator.Fill(dst, 0xAB, 4096);
            ok = true;
            for (uint i = 0; i < 4096; i++) if (dst[i] != 0xAB) { ok = false; break; }
            o.Add("Remplissage rapide 4096 octets     : " + (ok ? "OK" : "ECHEC"));

            Heap.Free(src);
            Heap.Free(dst);
        }

        private static void Buffers(List<string> o)
        {
            var reg = PixelBuffer.Registry;
            uint total = 0;
            for (int i = 0; i < reg.Count; i++)
            {
                PixelBuffer b = reg[i];
                total += b.Bytes;
                o.Add("0x" + Fmt.Hex(b.Address) + "  " + Fmt.Pad(b.W + "x" + b.H, 9) + " " +
                      Fmt.Pad((b.Bytes / 1024).ToString() + " Ko", 8) + " " + b.Name);
            }
            o.Add("Total : " + reg.Count + " buffers, " + (total / 1024) + " Ko");
        }

        private static void Disks(List<string> o)
        {
            if (!Services.FsReady) { o.Add("Erreur : aucun systeme de fichiers."); return; }
            try
            {
                var vols = VFSManager.GetVolumes();
                if (vols.Count == 0) o.Add("Aucun lecteur detecte.");
                for (int i = 0; i < vols.Count; i++)
                {
                    string d = vols[i].mFullPath;
                    o.Add(Fmt.Pad(d, 6) + " total " + Fmt.Size(Services.FileSystem.GetTotalSize(d)) +
                          "   libre " + Fmt.Size(Services.FileSystem.GetAvailableFreeSpace(d)));
                }
            }
            catch (Exception e) { o.Add("Erreur : " + e.Message); }
        }

        private static void Dir(string arg, List<string> o)
        {
            if (!Services.FsReady) { o.Add("Erreur : aucun systeme de fichiers."); return; }
            string p = arg.Length == 0 ? "0:\\" : arg;
            try
            {
                var list = VFSManager.GetDirectoryListing(p);
                o.Add("Contenu de " + p + " (" + list.Count + ")");
                for (int i = 0; i < list.Count; i++)
                {
                    bool dir = list[i].mEntryType == DirectoryEntryTypeEnum.Directory;
                    o.Add((dir ? "[DOS] " : "      ") + Fmt.Pad(list[i].mName, 32) +
                          (dir ? "" : Fmt.Size(list[i].mSize)));
                }
            }
            catch (Exception e) { o.Add("Erreur : " + e.Message); }
        }

        private static void OpenFile(string arg, List<string> o)
        {
            if (arg.Length == 0) { o.Add("Erreur : usage -> ouvrir 0:\\fichier.txt"); return; }
            Services.EditorWin.Load(arg);
            Services.Windows.Open(Services.EditorWin);
            o.Add("Ouvert dans l'editeur : " + arg);
        }

        private static void CreateFile(string arg, List<string> o)
        {
            if (arg.Length == 0) { o.Add("Erreur : usage -> creer 0:\\notes.txt"); return; }
            try
            {
                if (!File.Exists(arg)) FileSystemOS.Utils.FsTools.WriteText(arg, "");
                Services.EditorWin.Load(arg);
                Services.Windows.Open(Services.EditorWin);
                o.Add("Fichier pret : " + arg);
            }
            catch (Exception e) { o.Add("Erreur : " + e.Message); }
        }

        private static void CompileFsl(string arg, List<string> o)
        {
            if (arg.Length == 0) { o.Add("Erreur : usage -> fslc 0:\\prog.fsl"); return; }
            try { o.Add("Compile : " + Launcher.CompileToFile(arg)); }
            catch (Exception e) { o.Add("Erreur de compilation : " + e.Message); }
        }

        private static void RunFsl(string arg, List<string> o)
        {
            if (arg.Length == 0) { o.Add("Erreur : usage -> run 0:\\prog.fsl"); return; }
            Launcher.Run(arg, out string msg);
            o.Add(msg);
        }

        private static void Ps(List<string> o)
        {
            var s = Services.Scheduler;
            o.Add("Ordonnanceur : " + s.TotalTicks + " tours, " + s.Commutations + " commutations de contexte, " +
                  "tranche " + Scheduler.Quantum + " ms");
            o.Add("PID  processus        utilisateur  etat     prio     cpu(ms)  pages  threads");
            var procs = s.Processes;
            for (int i = 0; i < procs.Count; i++)
            {
                Process p = procs[i];
                o.Add(Fmt.Pad(p.Pid.ToString(), 5) + Fmt.Pad(p.Name, 17) + Fmt.Pad(p.Utilisateur, 13) +
                      Fmt.Pad(Etat.Nom(p.EtatCourant), 9) + Fmt.Pad(Prio.Name(p.Priority), 9) +
                      Fmt.Pad(p.TempsCpu.ToString(), 9) + Fmt.Pad(p.PagesUtilisees.ToString(), 7) +
                      p.Threads.Count + (p.Critical ? "  [systeme]" : ""));
                for (int j = 0; j < p.Threads.Count; j++)
                {
                    KThread t = p.Threads[j];
                    o.Add("      TID " + Fmt.Pad(t.Tid.ToString(), 4) + Fmt.Pad(t.Name, 12) +
                          Fmt.Pad(Etat.Nom(t.EtatCourant), 9) + "cpu " + Fmt.Pad(t.TempsCpu + " ms", 10) +
                          "pire tranche " + Fmt.Pad(t.PireTranche + " ms", 8) +
                          (t.Depassements > 0 ? t.Depassements + " depassement(s)" : ""));
                }
            }
        }

        private static void Demo(List<string> o)
        {
            Process p = Services.Scheduler.Spawn("demo", false);
            p.Start(new CounterThread());
            p.Allouer("demo (donnees)", FileSystemOS.Memoire.Attr.Donnees);
            o.Add("Processus 'demo' lance (PID " + p.Pid + ", utilisateur " + p.Utilisateur + ").");
            o.Add("Essayez : ps, pages, envoyer demo bonjour, ipc, verrous, kill " + p.Pid);
        }

        /// <summary>Demonstration d'un INTERBLOCAGE : deux processus, deux verrous croises.</summary>
        private static void DemoVerrou(List<string> o)
        {
            Process a = Services.Scheduler.Spawn("demo-a", false);
            Process b = Services.Scheduler.Spawn("demo-b", false);
            var v1 = new Verrou("fichier");
            var v2 = new Verrou("imprimante");

            v1.Prendre(a, out string r1);
            o.Add("demo-a prend le verrou 'fichier' : " + (r1.Length == 0 ? "accorde" : r1));
            v2.Prendre(b, out string r2);
            o.Add("demo-b prend le verrou 'imprimante' : " + (r2.Length == 0 ? "accorde" : r2));

            v2.Prendre(a, out string r3);
            o.Add("demo-a demande 'imprimante' : " + (r3.Length == 0 ? "accorde" : r3));
            v1.Prendre(b, out string r4);
            o.Add("demo-b demande 'fichier'    : " + (r4.Length == 0 ? "accorde" : r4));
            o.Add("-> le systeme a vu le cycle d'attente et a refuse, au lieu de figer les deux.");

            v1.Rendre(a); v2.Rendre(b);
            a.Kill(); b.Kill();
        }

        private static void Kill(string arg, List<string> o)
        {
            int pid = Fmt.ParseInt(arg);
            if (pid < 0) { o.Add("Erreur : usage -> kill <pid>"); return; }
            Services.Scheduler.Kill(pid, out string msg);
            o.Add(msg);
        }

        private static void Cursor(List<string> o)
        {
            o.Add("Curseur " + MouseCursor.W + "x" + MouseCursor.H + " : 1 chiffre hexa = 1 pixel");
            o.Add("0 = transparent  1 = 0xFF000000 (noir)  F = 0xFFFFFFFF (blanc)");
            for (int y = 0; y < MouseCursor.H; y++)
            {
                char[] row = new char[MouseCursor.W];
                for (int x = 0; x < MouseCursor.W; x++)
                {
                    uint px = MouseCursor.Bitmap[y * MouseCursor.W + x];
                    row[x] = px == 0 ? '0' : (px == MouseCursor.Black ? '1' : 'F');
                }
                o.Add(Fmt.Two(y) + " : " + new string(row) +
                      "   contour 0x" + Fmt.Hex(MouseCursor.Outline[y], 3) +
                      "  remplissage 0x" + Fmt.Hex(MouseCursor.Fill[y], 3));
            }
        }

        // ---- Mise en attente des processus et des threads ----

        private static Process ProcessusPid(string s, List<string> o)
        {
            int pid = Fmt.ParseInt(s);
            var l = Services.Scheduler.Processes;
            for (int i = 0; i < l.Count; i++) if (l[i].Pid == pid) return l[i];
            o.Add("Erreur : aucun processus " + s + " (tapez ps)");
            return null;
        }

        /// <summary>attente PID [TID] / reprendre PID [TID]</summary>
        private static void AttenteOuReprise(bool attente, string arg, List<string> o)
        {
            string[] m = arg.Split(' ');
            if (arg.Length == 0) { o.Add("Usage : " + (attente ? "attente" : "reprendre") + " PID [TID]   (ps, threads PID)"); return; }
            Process p = ProcessusPid(m[0], o);
            if (p == null) return;
            if (attente && p.Critical) { o.Add("Refuse : '" + p.Name + "' est un processus du systeme"); return; }
            if (m.Length >= 2 && m[1].Length > 0)
            {
                int tid = Fmt.ParseInt(m[1]);
                for (int i = 0; i < p.Threads.Count; i++)
                    if (p.Threads[i].Tid == tid)
                    {
                        p.Threads[i].EnAttente = attente;
                        if (!attente) { p.Threads[i].RaisonAttente = ""; p.Threads[i].Errors = 0; }
                        o.Add("Thread " + tid + " (" + p.Threads[i].Name + ") de '" + p.Name + "' " + (attente ? "mis en attente" : "repris"));
                        return;
                    }
                o.Add("Erreur : aucun thread " + m[1] + " dans '" + p.Name + "' (threads " + p.Pid + ")");
                return;
            }
            p.Suspended = attente;
            if (!attente)
                for (int i = 0; i < p.Threads.Count; i++) { p.Threads[i].EnAttente = false; p.Threads[i].RaisonAttente = ""; p.Threads[i].Errors = 0; }
            o.Add("'" + p.Name + "' (PID " + p.Pid + ") " + (attente ? "mis en attente : il ne consomme plus rien, ses donnees restent" : "repris"));
        }

        private static void Threads(string arg, List<string> o)
        {
            if (arg.Length == 0) { o.Add("Usage : threads PID"); return; }
            Process p = ProcessusPid(arg, o);
            if (p == null) return;
            o.Add("Threads de '" + p.Name + "' (PID " + p.Pid + ")" + (p.Suspended ? " - PROCESSUS EN ATTENTE" : ""));
            for (int i = 0; i < p.Threads.Count; i++)
            {
                KThread t = p.Threads[i];
                string etat = !t.Alive ? "termine" : (t.EnAttente ? "EN ATTENTE" + (t.RaisonAttente.Length > 0 ? " (" + t.RaisonAttente + ")" : "") : Etat.Nom(t.EtatCourant));
                o.Add("  TID " + t.Tid + "  " + Fmt.Pad(t.Name, 12) + " " + etat + "   erreurs " + t.Errors + "   " + t.TempsCpu + " ms");
            }
        }

        // ---- Decompilation : .fsc -> .fsl ----

        private static void Decompiler(string arg, List<string> o)
        {
            if (arg.Length == 0) { o.Add("Usage : decompiler 1:\\FSOS\\Applications\\prog.fsc   (cree prog-decompile.fsl)"); return; }
            try
            {
                FslProgram p = FslProgram.Load(arg);
                string code = Decompilateur.Decompiler(p);
                int dot = arg.LastIndexOf('.');
                string sortie = (dot > arg.LastIndexOf('\\') ? arg.Substring(0, dot) : arg) + "-decompile.fsl";
                byte[] b = new byte[code.Length];
                for (int i = 0; i < code.Length; i++) b[i] = code[i] < 256 ? (byte)code[i] : (byte)'?';
                FsTools.WriteBytes(sortie, b);
                o.Add("Decompile : " + sortie + " (" + p.Fonctions.Count + " fonction(s), " + p.VarNames.Count + " variable(s) globale(s))");
            }
            catch (Exception e) { o.Add("Erreur : " + e.Message); }
        }

        /// <summary>Compile, decompile et recompile : le bytecode doit etre identique.</summary>
        private static void VerifierFsl(string arg, List<string> o)
        {
            if (arg.Length == 0) { o.Add("Usage : fslverifier 1:\\FSOS\\Applications\\prog.fsl"); return; }
            try
            {
                string src = TextCodec.Join(TextCodec.ReadLines(arg));
                string diff = Decompilateur.VerifierAllerRetour(src, out string code);
                if (diff == null) o.Add("OK : compile -> decompile -> recompile donne exactement le meme bytecode");
                else o.Add("DIFFERENCE : " + diff);
            }
            catch (Exception e) { o.Add("Erreur : " + e.Message); }
        }

        // ---- Pilotes FSL ----

        private static void ListePilotes(List<string> o)
        {
            o.Add(FileSystemOS.Systeme.PilotesFsl.Charger() + " dans " + FileSystemOS.Systeme.PilotesFsl.Dossier());
            var l = FileSystemOS.Systeme.PilotesFsl.Liste;
            for (int i = 0; i < l.Count; i++)
            {
                o.Add("  " + Fmt.Pad(l[i].Nom, 14) + " " + FileSystemOS.Systeme.PilotesFsl.EtatDe(l[i]) +
                      (l[i].Demarrage ? "  [au demarrage]" : ""));
                o.Add("      ports " + FileSystemOS.Systeme.PilotesFsl.Ports(l[i]) + (l[i].Description.Length > 0 ? " - " + l[i].Description : ""));
            }
            if (l.Count == 0) o.Add("  (aucun : tapez 'pilote exemples' ou 'pilote nouveau NOM')");
        }

        private static void CommandePilote(string arg, List<string> o)
        {
            string[] m = arg.Split(' ');
            string action = m[0], nom = m.Length > 1 ? m[1] : "";
            var c = FileSystemOS.Systeme.Comptes.Actuel;
            if (c == null || !c.Admin) { o.Add("Refuse : seul un administrateur gere les pilotes"); return; }
            if (action == "exemples") { o.Add(FileSystemOS.Systeme.PilotesFsl.Exemples()); return; }
            if (nom.Length == 0) { o.Add("Usage : pilote demarrer|arreter|nouveau NOM   ou   pilote exemples"); return; }
            if (action == "nouveau") { o.Add(FileSystemOS.Systeme.PilotesFsl.Nouveau(nom)); return; }
            FileSystemOS.Systeme.PilotesFsl.Charger();
            var p = FileSystemOS.Systeme.PilotesFsl.Trouver(nom);
            if (p == null) { o.Add("Aucun pilote '" + nom + "' (tapez pilotes)"); return; }
            if (action == "demarrer") o.Add(FileSystemOS.Systeme.PilotesFsl.Demarrer(p));
            else if (action == "arreter") o.Add(FileSystemOS.Systeme.PilotesFsl.Arreter(p));
            else o.Add("Action inconnue : " + action + " (demarrer, arreter, nouveau, exemples)");
        }
    }
}