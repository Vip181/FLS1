using System;
using System.Collections.Generic;
using System.IO;
using Cosmos.System.Graphics;
using FileSystemOS.Graphique;
using FileSystemOS.Utils;

namespace FileSystemOS.Systeme
{
    /// <summary>
    /// DETECTION DU MATERIEL : processeur, memoire, carte graphique et resolutions
    /// supportees. Choisit une resolution sure pour CET ordinateur, avec une chaine de
    /// repli (si un mode echoue, on essaie le suivant) pour eviter les bugs de demarrage.
    /// </summary>
    public static class Materiel
    {
        public static string Cpu = "inconnu", Vendor = "inconnu", Video = "inconnu";
        public static uint RamMo;
        public static string Choix = "";                 // explication du choix de resolution
        public static bool Virtuel;                      // machine virtuelle (VirtualBox, VMware, QEMU)
        public static readonly List<Mode> Modes = new List<Mode>();

        // Ordre de preference en mode "auto" : rapide et lisible avant tout
        private static readonly int[] AutoW = { 1280, 1366, 1280, 1024, 800 };
        private static readonly int[] AutoH = { 720, 768, 800, 768, 600 };

        // Derniers recours, sans verification
        private static readonly int[] SafeW = { 1024, 800, 640 };
        private static readonly int[] SafeH = { 768, 600, 480 };

        /// <summary>
        /// Machine virtuelle ? Sur un VRAI PC, l'ecran est regle par le chargeur de demarrage
        /// (VBE du BIOS) et ne doit JAMAIS etre change ensuite : sinon l'image est decalee
        /// (lignes en biais, couleurs fausses) car la carte reste dans son mode d'origine.
        /// </summary>
        private static void DetectVm()
        {
            try { Virtuel = Cosmos.System.VMTools.IsVirtualBox || Cosmos.System.VMTools.IsVMWare || Cosmos.System.VMTools.IsQEMU; }
            catch (Exception) { Virtuel = false; }
        }

        private static void DetectCpu()
        {
            try { RamMo = Cosmos.Core.CPU.GetAmountOfRAM(); } catch (Exception) { }
            try { Vendor = Cosmos.Core.CPU.GetCPUVendorName(); } catch (Exception) { }
            try { Cpu = Cosmos.Core.CPU.GetCPUBrandString().Trim(); } catch (Exception) { }
        }

        private static bool Supported(int w, int h)
        {
            if (Modes.Count == 0) return true;   // liste inconnue : on tentera quand meme
            for (int i = 0; i < Modes.Count; i++)
                if ((int)Modes[i].Columns == w && (int)Modes[i].Rows == h) return true;
            return false;
        }

        /// <summary>Assez de memoire ? (ecran + scene + fond = 3 buffers, plus de la marge)</summary>
        private static bool RamOk(int w, int h)
        {
            if (RamMo == 0) return true;
            uint need = (uint)(w * h * 4 * 3 / (1024 * 1024)) + 48;
            return RamMo >= need * 2;
        }

        /// <summary>Taille d'ecran credible ? (0x0 ou 80x25 = mode texte, pas graphique)</summary>
        private static bool Sain(int w, int h) => w >= 640 && h >= 480 && w <= 4096 && h <= 2160;

        private static bool TryMode(Canvas c, int w, int h)
        {
            try
            {
                c.Mode = new Mode(w, h, ColorDepth.ColorDepth32);
                Services.ScreenW = w;
                Services.ScreenH = h;
                return true;
            }
            catch (Exception) { return false; }
        }

        /// <summary>
        /// Ouvre l'ecran graphique. 'wanted' vient de demarrage.cfg : "auto" ou "1280x720".
        /// </summary>
        public static Canvas OpenScreen(string wanted)
        {
            DetectCpu();
            DetectVm();

            Canvas c;
            try { c = FullScreenCanvas.GetFullScreenCanvas(); }
            catch (Exception e)
            {
                EcranSecours.Raison = "ouverture de l'ecran : " + e.Message;
                return null;                  // -> mode texte de secours
            }
            if (c == null) { EcranSecours.Raison = "aucun pilote graphique disponible"; return null; }
            try { Video = c.Name(); } catch (Exception) { }

            // VRAI ORDINATEUR : on garde exactement le mode choisi au demarrage (multiboot / VBE).
            // Seul "resolution=force:LxH" dans demarrage.cfg permet de forcer un changement.
            bool force = wanted != null && wanted.StartsWith("force:");
            if (!Virtuel && !force)
            {
                int w0 = 0, h0 = 0;
                try { w0 = (int)c.Mode.Columns; h0 = (int)c.Mode.Rows; } catch (Exception) { }
                if (Sain(w0, h0))
                {
                    Services.ScreenW = w0;
                    Services.ScreenH = h0;
                    Choix = "mode d'origine (vrai ordinateur, non modifie)";
                    return c;
                }
                // Taille absurde (0x0...) : le chargeur n'a pas prepare d'ecran. On essaie nous-memes.
                for (int i = 0; i < SafeW.Length; i++)
                    if (TryMode(c, SafeW[i], SafeH[i])) { Choix = "repli (mode d'origine invalide)"; return c; }
                EcranSecours.Raison = "aucun mode graphique accepte (ecran annonce " + w0 + "x" + h0 + ")";
                return null;
            }
            if (force) wanted = wanted.Substring(6);
            try
            {
                var all = c.AvailableModes;
                for (int i = 0; i < all.Count; i++)
                    if (all[i].ColorDepth == ColorDepth.ColorDepth32 && all[i].Columns >= 640) Modes.Add(all[i]);
            }
            catch (Exception) { }

            // 1. Resolution demandee dans demarrage.cfg
            if (wanted != null && wanted != "auto")
            {
                int x = wanted.IndexOf('x');
                if (x > 0)
                {
                    int w = Fmt.ParseInt(wanted.Substring(0, x)), h = Fmt.ParseInt(wanted.Substring(x + 1));
                    if (w >= 640 && h >= 480 && Supported(w, h) && TryMode(c, w, h))
                    {
                        Choix = "demandee (demarrage.cfg)";
                        return c;
                    }
                }
            }

            // 2. Choix automatique selon l'ordinateur
            for (int i = 0; i < AutoW.Length; i++)
            {
                if (!Supported(AutoW[i], AutoH[i]) || !RamOk(AutoW[i], AutoH[i])) continue;
                if (TryMode(c, AutoW[i], AutoH[i])) { Choix = "automatique"; return c; }
            }

            // 3. Repli de securite
            for (int i = 0; i < SafeW.Length; i++)
                if (TryMode(c, SafeW[i], SafeH[i])) { Choix = "repli de securite"; return c; }

            // 4. On garde le mode par defaut du pilote, s'il est credible
            int dw = 0, dh = 0;
            try { dw = (int)c.Mode.Columns; dh = (int)c.Mode.Rows; } catch (Exception) { }
            if (!Sain(dw, dh)) { EcranSecours.Raison = "aucun mode graphique accepte"; return null; }
            Services.ScreenW = dw;
            Services.ScreenH = dh;
            Choix = "mode par defaut du pilote";
            return c;
        }

        public static List<string> Report()
        {
            var r = new List<string>();
            r.Add("Processeur : " + Cpu + " (" + Vendor + ")");
            r.Add("Memoire    : " + RamMo + " Mo");
            r.Add("Machine    : " + (Virtuel ? "virtuelle" : "physique (vrai ordinateur)"));
            r.Add("Graphique  : " + Video);
            r.Add("Resolution : " + Services.ScreenW + "x" + Services.ScreenH + " - " + Choix);
            string m = "";
            for (int i = 0; i < Modes.Count && i < 10; i++)
                m += (i > 0 ? ", " : "") + Modes[i].Columns + "x" + Modes[i].Rows;
            r.Add("Modes      : " + (m.Length > 0 ? m : "liste non fournie par le pilote"));
            return r;
        }

        /// <summary>Ecrit la fiche materiel dans \FSOS\Systeme\materiel.txt</summary>
        public static string Save()
        {
            string root = InstallerWindow.FindRoot();
            if (root == null) return "non enregistre (OS pas encore installe)";
            try
            {
                FileSystemOS.Utils.FsTools.WriteBytes(root + "\\Systeme\\materiel.txt", TextCodec.Encode(Report()));
                return "fiche ecrite dans " + root + "\\Systeme\\materiel.txt";
            }
            catch (Exception e) { return "erreur : " + e.Message; }
        }
    }
}
