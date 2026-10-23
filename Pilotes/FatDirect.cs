using System;
using System.Collections.Generic;
using Cosmos.HAL.BlockDevice;
using FileSystemOS.Utils;

namespace FileSystemOS.Pilotes
{
    /// <summary>Une entree de repertoire FAT : fichier ou dossier.</summary>
    public class EntreeFat
    {
        public string Nom = "";          // nom long (ou nom court s'il n'y en a pas)
        public string Court = "";        // nom 8.3 tel qu'il est sur le disque
        public byte Attributs;
        public uint Grappe;              // premiere grappe des donnees
        public uint Taille;              // en octets (0 pour un dossier)
        public ushort Date, Heure;

        // Position dans le repertoire parent (pour modifier ou effacer l'entree)
        public int Position;             // octet de l'entree courte dans le repertoire charge
        public int NbLongues;            // entrees de nom long qui la precedent

        public bool Dossier => (Attributs & 0x10) != 0;
        public bool Volume => (Attributs & 0x08) != 0;
        public bool Cache => (Attributs & 0x02) != 0;
        public bool Systeme => (Attributs & 0x04) != 0;

        public string DateTexte
        {
            get
            {
                if (Date == 0) return "";
                int a = 1980 + (Date >> 9), m = (Date >> 5) & 15, j = Date & 31;
                return Fmt.Two(j) + "/" + Fmt.Two(m) + "/" + a + " " + Fmt.Two(Heure >> 11) + ":" + Fmt.Two((Heure >> 5) & 63);
            }
        }
    }

    /// <summary>
    /// PILOTE DE FICHIERS FAT32 DIRECT, ecrit pour File System OS.
    ///
    /// Il lit et ecrit les fichiers d'une partition FAT32 en parlant DIRECTEMENT au disque
    /// (le pilote AHCI ou NVMe), sans passer par le systeme de fichiers de Cosmos. C'est ce
    /// qui permet d'utiliser le SSD du Dell, que Cosmos refuse de monter a cause de sa table
    /// GPT et de ses partitions Windows.
    ///
    /// Organisation d'une partition FAT32 :
    ///   secteur 0          : secteur de demarrage (BPB) - la geometrie du volume
    ///   secteur 1          : FSInfo - nombre de grappes libres, prochaine grappe libre
    ///   secteurs reserves  : jusqu'au debut des tables FAT
    ///   tables FAT (x2)    : pour chaque grappe, le numero de la suivante (une "chaine")
    ///   zone de donnees    : les grappes, numerotees a partir de 2
    /// Un fichier = une entree de 32 octets dans un repertoire + une chaine de grappes.
    /// Un nom long (plus de 8.3 caracteres) occupe des entrees supplementaires de 13 caracteres.
    ///
    /// Securite : un volume est ouvert en LECTURE SEULE, sauf s'il a ete formate par
    /// File System OS (etiquette "FSOS") ou si l'utilisateur force l'ecriture. La partition
    /// EFI de Windows peut ainsi etre lue sans risque de casser le demarrage de Windows.
    /// Apres chaque ecriture, le fichier est relu et compare octet par octet.
    /// </summary>
    public class FatDirect
    {
        public const uint FinDeChaine = 0x0FFFFFF8;
        public const uint GrappeDefectueuse = 0x0FFFFFF7;
        public const int TailleMaxFichier = 32 * 1024 * 1024;

        public const byte AttrLectureSeule = 0x01, AttrCache = 0x02, AttrSysteme = 0x04;
        public const byte AttrVolume = 0x08, AttrDossier = 0x10, AttrArchive = 0x20, AttrNomLong = 0x0F;

        // ---- Volume ----
        public readonly BlockDevice Disque;
        public readonly ulong Debut;
        public readonly string NomDisque;
        public bool Ecriture;
        public bool Verifier = true;

        public string Oem = "", Etiquette = "";
        private uint secteursParGrappe, reserves, nbFats, secteursParFat, grappeRacine, fsInfo;
        private ulong totalSecteurs, debutFat, debutDonnees;
        private uint nbGrappes;
        private uint indiceLibre = 2;

        public uint OctetsParGrappe => secteursParGrappe * 512;
        public uint NbGrappes => nbGrappes;
        public long Capacite => (long)nbGrappes * OctetsParGrappe;
        public bool EstFsos => Oem.ToUpper() == "FSOS" || Etiquette.ToUpper() == "FSOS";

        /// <summary>Lecture brute du BPB, pour le diagnostic : dit pourquoi un volume est refuse.</summary>
        public static string Analyse(BlockDevice d, ulong debut)
        {
            byte[] s = new byte[512];
            try { d.ReadBlock(debut, 1, ref s); }
            catch (Exception e) { return "LECTURE IMPOSSIBLE : " + e.Message; }
            string sig = (s[510] == 0x55 && s[511] == 0xAA) ? "signature ok" : "PAS de signature 55AA";
            string oem = Ascii(s, 3, 8);
            if (oem == "NTFS") return "NTFS (Windows) - " + sig;
            if (Ascii(s, 3, 5) == "EXFAT") return "exFAT - " + sig;
            return "OEM \"" + oem + "\", octets/secteur " + U16(s, 11) + ", secteurs/grappe " + s[13] +
                   ", reserves " + U16(s, 14) + ", FATs " + s[16] + ", racine " + U16(s, 17) +
                   ", FAT16 " + U16(s, 22) + ", FAT32 " + U32(s, 36) + ", " + sig;
        }

        // ---- Statistiques ----
        public uint Lectures, Ecritures, Verifications, Erreurs;

        // ---- Cache d'un secteur de la table FAT ----
        private readonly byte[] cacheFat = new byte[512];
        private long cacheSecteur = -1;
        private bool cacheSale;

        private FatDirect(BlockDevice d, ulong debut, string nom)
        {
            Disque = d; Debut = debut; NomDisque = nom;
        }

        // ================================================================
        //  Outils d'octets
        // ================================================================
        private static uint U16(byte[] b, int p) => (uint)(b[p] | (b[p + 1] << 8));
        private static uint U32(byte[] b, int p) => (uint)(b[p] | (b[p + 1] << 8) | (b[p + 2] << 16) | (b[p + 3] << 24));
        private static void W16(byte[] b, int p, uint v) { b[p] = (byte)v; b[p + 1] = (byte)(v >> 8); }
        private static void W32(byte[] b, int p, uint v) { W16(b, p, v & 0xFFFF); W16(b, p + 2, v >> 16); }

        private static string Ascii(byte[] b, int p, int n)
        {
            char[] c = new char[n];
            int k = 0;
            for (int i = 0; i < n; i++)
            {
                byte x = b[p + i];
                c[k++] = x >= 32 && x < 127 ? (char)x : ' ';
            }
            return new string(c, 0, k).Trim();
        }

        // ================================================================
        //  Montage
        // ================================================================

        /// <summary>Ouvre la partition FAT32 qui commence au secteur 'debut'. Renvoie null si ce n'en est pas une.</summary>
        public static FatDirect Monter(BlockDevice d, ulong debut, string nom, out string erreur)
        {
            erreur = "";
            byte[] s = new byte[512];
            try { d.ReadBlock(debut, 1, ref s); }
            catch (Exception e) { erreur = "lecture impossible : " + e.Message; return null; }

            if (s[510] != 0x55 || s[511] != 0xAA) { erreur = "pas de secteur de demarrage"; return null; }
            if (U16(s, 11) != 512) { erreur = "secteurs de " + U16(s, 11) + " octets non geres"; return null; }

            uint spc = s[13];
            if (spc == 0 || (spc & (spc - 1)) != 0 || spc > 128) { erreur = "taille de grappe invalide"; return null; }
            if (U16(s, 17) != 0 || U16(s, 22) != 0) { erreur = "ce n'est pas du FAT32 (FAT12/16 ou NTFS)"; return null; }

            var v = new FatDirect(d, debut, nom);
            v.secteursParGrappe = spc;
            v.reserves = U16(s, 14);
            v.nbFats = s[16];
            v.secteursParFat = U32(s, 36);
            v.grappeRacine = U32(s, 44);
            v.fsInfo = U16(s, 48);
            v.totalSecteurs = U16(s, 19) != 0 ? U16(s, 19) : U32(s, 32);
            v.Oem = Ascii(s, 3, 8);
            v.Etiquette = Ascii(s, 71, 11);

            if (v.nbFats < 1 || v.nbFats > 2 || v.secteursParFat == 0 || v.reserves == 0)
            { erreur = "geometrie FAT32 incoherente"; return null; }

            v.debutFat = debut + v.reserves;
            v.debutDonnees = v.debutFat + (ulong)v.nbFats * v.secteursParFat;
            ulong donnees = v.totalSecteurs - (v.reserves + (ulong)v.nbFats * v.secteursParFat);
            v.nbGrappes = (uint)(donnees / spc);
            // Le BPB dit FAT32 (pas de racine fixe, pas de FAT16) : on le croit, comme Windows et
            // Linux. Les partitions EFI ont parfois moins de 65525 grappes, sans etre du FAT16.
            if (v.nbGrappes < 16) { erreur = "volume trop petit"; return null; }
            if (v.grappeRacine < 2 || v.grappeRacine >= v.nbGrappes + 2) { erreur = "grappe racine invalide"; return null; }

            // Prochaine grappe libre conseillee par FSInfo (accelere les allocations)
            try
            {
                if (v.fsInfo > 0 && v.fsInfo < v.reserves)
                {
                    byte[] fi = new byte[512];
                    d.ReadBlock(debut + v.fsInfo, 1, ref fi);
                    if (U32(fi, 0) == 0x41615252 && U32(fi, 484) == 0x61417272)
                    {
                        uint indice = U32(fi, 492);
                        if (indice >= 2 && indice < v.nbGrappes + 2) v.indiceLibre = indice;
                    }
                }
            }
            catch (Exception) { }

            // Windows range le nom du volume dans le repertoire racine et laisse souvent
            // "NO NAME" dans le secteur de demarrage : on lit donc aussi la racine.
            v.LireEtiquetteRacine();

            // Ecriture autorisee d'office sur un volume nomme "FSOS" (cree par File System OS
            // ou formate par Windows avec ce nom)
            v.Ecriture = v.EstFsos;
            return v;
        }

        /// <summary>
        /// Cherche l'entree "nom de volume" (attribut 0x08) dans la racine. C'est la que Windows
        /// ecrit le nom choisi au formatage ou en renommant le lecteur.
        /// </summary>
        private void LireEtiquetteRacine()
        {
            try
            {
                byte[] g = LireGrappe(grappeRacine);
                for (int p = 0; p + 32 <= g.Length; p += 32)
                {
                    byte premier = g[p];
                    if (premier == 0x00) return;                        // fin du repertoire
                    if (premier == 0xE5) continue;                      // entree effacee
                    byte attr = g[p + 11];
                    if ((attr & 0x3F) == AttrNomLong) continue;         // morceau de nom long
                    if ((attr & (AttrVolume | AttrDossier)) == AttrVolume)
                    {
                        string nom = Ascii(g, p, 11);
                        if (nom.Length > 0) Etiquette = nom;
                        return;
                    }
                }
            }
            catch (Exception) { }
        }

        private void ExigerEcriture()
        {
            if (!Ecriture)
                throw new Exception("volume en lecture seule (il n'a pas ete formate par File System OS ; " +
                                    "forcez avec 'ssd monter ... ecriture' si vous etes sur de vous)");
        }

        // ================================================================
        //  Table FAT
        // ================================================================
        private void ChargerFat(ulong secteur)
        {
            if (cacheSecteur == (long)secteur) return;
            ViderFat();
            byte[] tmp = cacheFat;
            Disque.ReadBlock(secteur, 1, ref tmp);
            cacheSecteur = (long)secteur;
            Lectures++;
        }

        /// <summary>Ecrit le secteur de FAT modifie dans TOUTES les copies de la table.</summary>
        private void ViderFat()
        {
            if (!cacheSale || cacheSecteur < 0) return;
            byte[] tmp = cacheFat;
            for (uint f = 0; f < nbFats; f++)
            {
                Disque.WriteBlock((ulong)cacheSecteur + (ulong)f * secteursParFat, 1, ref tmp);
                Ecritures++;
            }
            cacheSale = false;
        }

        public uint LireFat(uint grappe)
        {
            if (grappe < 2 || grappe >= nbGrappes + 2) return FinDeChaine;
            uint octet = grappe * 4;
            ChargerFat(debutFat + octet / 512);
            return U32(cacheFat, (int)(octet % 512)) & 0x0FFFFFFF;
        }

        private void EcrireFat(uint grappe, uint valeur)
        {
            if (grappe < 2 || grappe >= nbGrappes + 2) throw new Exception("grappe hors du volume : " + grappe);
            uint octet = grappe * 4;
            ChargerFat(debutFat + octet / 512);
            int p = (int)(octet % 512);
            uint hauts = U32(cacheFat, p) & 0xF0000000;              // 4 bits reserves : on les garde
            W32(cacheFat, p, hauts | (valeur & 0x0FFFFFFF));
            cacheSale = true;
        }

        /// <summary>Liste des grappes d'une chaine, avec protection contre les boucles.</summary>
        public List<uint> Chaine(uint premiere)
        {
            var r = new List<uint>();
            uint c = premiere;
            while (c >= 2 && c < FinDeChaine && c != GrappeDefectueuse)
            {
                if (c >= nbGrappes + 2) throw new Exception("chaine corrompue (grappe " + c + " hors du volume)");
                r.Add(c);
                if (r.Count > nbGrappes) throw new Exception("chaine corrompue (boucle)");
                c = LireFat(c);
            }
            return r;
        }

        /// <summary>Trouve et reserve 'nombre' grappes libres, liees entre elles.</summary>
        private List<uint> Allouer(int nombre)
        {
            var r = new List<uint>();
            if (nombre <= 0) return r;
            uint c = indiceLibre;
            uint vus = 0;
            while (r.Count < nombre)
            {
                if (vus >= nbGrappes)
                {
                    // Plus de place : on rend ce qu'on avait pris
                    for (int i = 0; i < r.Count; i++) EcrireFat(r[i], 0);
                    ViderFat();
                    throw new Exception("volume plein");
                }
                if (c < 2 || c >= nbGrappes + 2) c = 2;
                if (LireFat(c) == 0)
                {
                    if (r.Count > 0) EcrireFat(r[r.Count - 1], c);
                    EcrireFat(c, 0x0FFFFFFF);                           // fin de chaine provisoire
                    r.Add(c);
                }
                c++;
                vus++;
            }
            indiceLibre = c;
            return r;
        }

        private void LibererChaine(uint premiere)
        {
            if (premiere < 2) return;
            var ch = Chaine(premiere);
            for (int i = 0; i < ch.Count; i++) EcrireFat(ch[i], 0);
            if (ch.Count > 0 && ch[0] < indiceLibre) indiceLibre = ch[0];
        }

        // ================================================================
        //  Grappes
        // ================================================================
        private ulong SecteurDe(uint grappe) => debutDonnees + (ulong)(grappe - 2) * secteursParGrappe;

        private byte[] LireGrappe(uint grappe)
        {
            byte[] b = new byte[OctetsParGrappe];
            Disque.ReadBlock(SecteurDe(grappe), secteursParGrappe, ref b);
            Lectures++;
            return b;
        }

        private void EcrireGrappe(uint grappe, byte[] b)
        {
            Disque.WriteBlock(SecteurDe(grappe), secteursParGrappe, ref b);
            Ecritures++;
        }

        // ================================================================
        //  Repertoires
        // ================================================================

        /// <summary>Un repertoire charge en memoire : ses grappes et leur contenu bout a bout.</summary>
        private class Repertoire
        {
            public uint Premiere;
            public List<uint> Grappes;
            public byte[] Octets;
        }

        private Repertoire ChargerRepertoire(uint premiere)
        {
            var r = new Repertoire();
            r.Premiere = premiere;
            r.Grappes = Chaine(premiere);
            r.Octets = new byte[r.Grappes.Count * OctetsParGrappe];
            for (int i = 0; i < r.Grappes.Count; i++)
            {
                byte[] g = LireGrappe(r.Grappes[i]);
                Array.Copy(g, 0, r.Octets, i * (int)OctetsParGrappe, g.Length);
            }
            return r;
        }

        /// <summary>Reecrit les grappes du repertoire qui contiennent les octets [debut, fin[.</summary>
        private void SauverRepertoire(Repertoire r, int debut, int fin)
        {
            int g0 = debut / (int)OctetsParGrappe, g1 = (fin - 1) / (int)OctetsParGrappe;
            for (int g = g0; g <= g1 && g < r.Grappes.Count; g++)
            {
                byte[] b = new byte[OctetsParGrappe];
                Array.Copy(r.Octets, g * (int)OctetsParGrappe, b, 0, (int)OctetsParGrappe);
                EcrireGrappe(r.Grappes[g], b);
            }
        }

        /// <summary>Lit les entrees d'un repertoire, en reconstituant les noms longs.</summary>
        private List<EntreeFat> Entrees(Repertoire r)
        {
            var liste = new List<EntreeFat>();
            char[] nomLong = new char[260];
            int longueurLong = 0;
            int nbLongues = 0;
            byte sommeAttendue = 0;

            for (int p = 0; p + 32 <= r.Octets.Length; p += 32)
            {
                byte premier = r.Octets[p];
                if (premier == 0x00) break;                              // fin du repertoire
                if (premier == 0xE5) { nbLongues = 0; longueurLong = 0; continue; }   // entree effacee

                byte attr = r.Octets[p + 11];
                if ((attr & 0x3F) == AttrNomLong)
                {
                    // Morceau de nom long : 13 caracteres UTF-16 repartis en trois zones
                    int seq = premier & 0x1F;
                    if ((premier & 0x40) != 0) { longueurLong = 0; nbLongues = 0; for (int i = 0; i < 260; i++) nomLong[i] = '\0'; }
                    sommeAttendue = r.Octets[p + 13];
                    int base0 = (seq - 1) * 13;
                    for (int i = 0; i < 13; i++)
                    {
                        int o = i < 5 ? 1 + i * 2 : (i < 11 ? 14 + (i - 5) * 2 : 28 + (i - 11) * 2);
                        int ch = r.Octets[p + o] | (r.Octets[p + o + 1] << 8);
                        if (ch == 0x0000 || ch == 0xFFFF) continue;
                        int pos = base0 + i;
                        if (pos < 260)
                        {
                            nomLong[pos] = ch < 256 ? (char)ch : '?';
                            if (pos + 1 > longueurLong) longueurLong = pos + 1;
                        }
                    }
                    nbLongues++;
                    continue;
                }

                var e = new EntreeFat();
                e.Attributs = attr;
                e.Court = NomCourt(r.Octets, p);
                e.Grappe = (U16(r.Octets, p + 20) << 16) | U16(r.Octets, p + 26);
                e.Taille = U32(r.Octets, p + 28);
                e.Heure = (ushort)U16(r.Octets, p + 22);
                e.Date = (ushort)U16(r.Octets, p + 24);
                e.Position = p;

                // Le nom long n'est valable que si sa somme de controle correspond au nom court
                if (nbLongues > 0 && longueurLong > 0 && Somme(r.Octets, p) == sommeAttendue)
                {
                    e.Nom = new string(nomLong, 0, longueurLong);
                    e.NbLongues = nbLongues;
                }
                else
                {
                    e.Nom = NomCourtAffiche(r.Octets, p);
                    e.NbLongues = 0;
                }
                nbLongues = 0;
                longueurLong = 0;

                if (e.Volume && !e.Dossier) continue;                    // etiquette du volume
                if (e.Court == "." || e.Court == "..") continue;
                liste.Add(e);
            }
            return liste;
        }

        private static string NomCourt(byte[] b, int p)
        {
            string nom = Ascii(b, p, 8), ext = Ascii(b, p + 8, 3);
            if (b[p] == 0x05) nom = (char)0xE5 + nom.Substring(1);
            return ext.Length > 0 ? nom + "." + ext : nom;
        }

        /// <summary>Nom court affiche, en tenant compte des drapeaux "minuscules" poses par Windows.</summary>
        private static string NomCourtAffiche(byte[] b, int p)
        {
            string nom = Ascii(b, p, 8), ext = Ascii(b, p + 8, 3);
            byte drapeaux = b[p + 12];
            if ((drapeaux & 0x08) != 0) nom = nom.ToLower();
            if ((drapeaux & 0x10) != 0) ext = ext.ToLower();
            return ext.Length > 0 ? nom + "." + ext : nom;
        }

        /// <summary>Somme de controle des 11 octets du nom court (lie les noms longs a leur entree).</summary>
        private static byte Somme(byte[] b, int p)
        {
            byte s = 0;
            for (int i = 0; i < 11; i++) s = (byte)((((s & 1) << 7) | (s >> 1)) + b[p + i]);
            return s;
        }

        private static bool MemeNom(string a, string b) => a.ToLower() == b.ToLower();

        private EntreeFat Chercher(List<EntreeFat> l, string nom)
        {
            for (int i = 0; i < l.Count; i++)
                if (MemeNom(l[i].Nom, nom) || MemeNom(l[i].Court, nom)) return l[i];
            return null;
        }

        // ================================================================
        //  Chemins
        // ================================================================
        private static List<string> Morceaux(string chemin)
        {
            var r = new List<string>();
            string courant = "";
            for (int i = 0; i < chemin.Length; i++)
            {
                char c = chemin[i];
                if (c == '\\' || c == '/')
                {
                    if (courant.Length > 0) r.Add(courant);
                    courant = "";
                }
                else courant += c;
            }
            if (courant.Length > 0) r.Add(courant);
            return r;
        }

        /// <summary>Premiere grappe du dossier designe par le chemin (la racine si chemin vide).</summary>
        private uint Dossier(List<string> morceaux, int nombre)
        {
            uint g = grappeRacine;
            for (int i = 0; i < nombre; i++)
            {
                var e = Chercher(Entrees(ChargerRepertoire(g)), morceaux[i]);
                if (e == null) throw new Exception("dossier introuvable : " + morceaux[i]);
                if (!e.Dossier) throw new Exception("'" + morceaux[i] + "' n'est pas un dossier");
                g = e.Grappe == 0 ? grappeRacine : e.Grappe;
            }
            return g;
        }

        /// <summary>Oublie le secteur de FAT garde en memoire : a appeler quand un autre montage a pu ecrire.</summary>
        public void Oublier()
        {
            ViderFat();
            cacheSecteur = -1;
        }

        public List<EntreeFat> Lister(string chemin)
        {
            var m = Morceaux(chemin);
            return Entrees(ChargerRepertoire(Dossier(m, m.Count)));
        }

        public EntreeFat Info(string chemin)
        {
            var m = Morceaux(chemin);
            if (m.Count == 0) return null;
            return Chercher(Entrees(ChargerRepertoire(Dossier(m, m.Count - 1))), m[m.Count - 1]);
        }

        public bool Existe(string chemin)
        {
            try { return Info(chemin) != null; } catch (Exception) { return false; }
        }

        // ================================================================
        //  Lecture d'un fichier
        // ================================================================
        public byte[] Lire(string chemin)
        {
            var e = Info(chemin);
            if (e == null) throw new Exception("fichier introuvable : " + chemin);
            if (e.Dossier) throw new Exception("'" + chemin + "' est un dossier");
            if (e.Taille > TailleMaxFichier) throw new Exception("fichier trop gros pour la memoire (" + Fmt.Size(e.Taille) + ")");
            return LireDonnees(e.Grappe, e.Taille);
        }

        private byte[] LireDonnees(uint premiere, uint taille)
        {
            byte[] r = new byte[taille];
            if (taille == 0) return r;
            var ch = Chaine(premiere);
            uint fait = 0;
            for (int i = 0; i < ch.Count && fait < taille; i++)
            {
                byte[] g = LireGrappe(ch[i]);
                uint n = taille - fait;
                if (n > OctetsParGrappe) n = OctetsParGrappe;
                Array.Copy(g, 0, r, (int)fait, (int)n);
                fait += n;
            }
            if (fait < taille) throw new Exception("fichier tronque : chaine trop courte");
            return r;
        }

        // ================================================================
        //  Noms courts 8.3 et entrees de noms longs
        // ================================================================
        private static bool CaractereCourt(char c) =>
            (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-' || c == '$' ||
            c == '~' || c == '!' || c == '#' || c == '%' || c == '&' || c == '@' || c == '(' || c == ')';

        /// <summary>Le nom tient-il tel quel en 8.3 majuscules ? (alors pas besoin de nom long)</summary>
        private static bool Est83(string nom)
        {
            int point = nom.LastIndexOf('.');
            string b = point < 0 ? nom : nom.Substring(0, point);
            string x = point < 0 ? "" : nom.Substring(point + 1);
            if (b.Length == 0 || b.Length > 8 || x.Length > 3) return false;
            for (int i = 0; i < b.Length; i++) if (!CaractereCourt(b[i])) return false;
            for (int i = 0; i < x.Length; i++) if (!CaractereCourt(x[i])) return false;
            return true;
        }

        private static string Nettoyer(string s, int max)
        {
            string r = "";
            string maj = s.ToUpper();
            for (int i = 0; i < maj.Length && r.Length < max; i++)
            {
                char c = maj[i];
                if (c == ' ' || c == '.') continue;
                r += CaractereCourt(c) ? c : '_';
            }
            return r;
        }

        /// <summary>Fabrique un nom court unique "NOMLON~1.TXT" a la maniere de Windows.</summary>
        private static byte[] FabriquerCourt(string nom, List<EntreeFat> existants)
        {
            byte[] r = new byte[11];
            if (Est83(nom))
            {
                int pt = nom.LastIndexOf('.');
                string b0 = pt < 0 ? nom : nom.Substring(0, pt);
                string x0 = pt < 0 ? "" : nom.Substring(pt + 1);
                for (int i = 0; i < 8; i++) r[i] = (byte)(i < b0.Length ? b0[i] : ' ');
                for (int i = 0; i < 3; i++) r[8 + i] = (byte)(i < x0.Length ? x0[i] : ' ');
                return r;
            }

            int point = nom.LastIndexOf('.');
            string baseNom = Nettoyer(point <= 0 ? nom : nom.Substring(0, point), 8);
            string ext = point <= 0 ? "" : Nettoyer(nom.Substring(point + 1), 3);
            if (baseNom.Length == 0) baseNom = "FICHIER";

            for (int n = 1; n < 1000; n++)
            {
                string suffixe = "~" + n;
                string b = baseNom.Length + suffixe.Length > 8 ? baseNom.Substring(0, 8 - suffixe.Length) : baseNom;
                string candidat = b + suffixe + (ext.Length > 0 ? "." + ext : "");
                bool pris = false;
                for (int i = 0; i < existants.Count; i++) if (MemeNom(existants[i].Court, candidat)) pris = true;
                if (pris) continue;
                string bb = b + suffixe;
                for (int i = 0; i < 8; i++) r[i] = (byte)(i < bb.Length ? bb[i] : ' ');
                for (int i = 0; i < 3; i++) r[8 + i] = (byte)(i < ext.Length ? ext[i] : ' ');
                return r;
            }
            throw new Exception("trop de fichiers au nom proche de '" + nom + "'");
        }

        private static void DateHeure(out ushort date, out ushort heure)
        {
            try
            {
                int a = 2000 + Cosmos.HAL.RTC.Year, m = Cosmos.HAL.RTC.Month, j = Cosmos.HAL.RTC.DayOfTheMonth;
                int h = Cosmos.HAL.RTC.Hour, mi = Cosmos.HAL.RTC.Minute, s = Cosmos.HAL.RTC.Second;
                date = (ushort)(((a - 1980) << 9) | (m << 5) | j);
                heure = (ushort)((h << 11) | (mi << 5) | (s / 2));
            }
            catch (Exception) { date = 0; heure = 0; }
        }

        /// <summary>Trouve 'nombre' emplacements libres consecutifs, en agrandissant le dossier si besoin.</summary>
        private int PlaceLibre(Repertoire r, int nombre)
        {
            int suite = 0;
            for (int p = 0; p + 32 <= r.Octets.Length; p += 32)
            {
                byte b = r.Octets[p];
                if (b == 0x00 || b == 0xE5)
                {
                    suite++;
                    if (suite == nombre) return p - (nombre - 1) * 32;
                }
                else suite = 0;
            }

            // Plus de place : on ajoute une grappe vide au repertoire
            var nouvelle = Allouer(1);
            EcrireFat(r.Grappes[r.Grappes.Count - 1], nouvelle[0]);
            EcrireGrappe(nouvelle[0], new byte[OctetsParGrappe]);
            r.Grappes.Add(nouvelle[0]);
            byte[] plus = new byte[r.Octets.Length + OctetsParGrappe];
            Array.Copy(r.Octets, plus, r.Octets.Length);
            r.Octets = plus;
            ViderFat();
            return PlaceLibre(r, nombre);
        }

        /// <summary>Ecrit l'entree (noms longs + nom court) d'un nouvel element dans le repertoire.</summary>
        private void CreerEntree(Repertoire r, string nom, byte attr, uint grappe, uint taille)
        {
            var existants = Entrees(r);
            byte[] court = FabriquerCourt(nom, existants);
            bool avecLong = !Est83(nom);
            int nbLong = avecLong ? (nom.Length + 12) / 13 : 0;
            if (nbLong > 20) throw new Exception("nom trop long (255 caracteres maximum)");

            int p = PlaceLibre(r, nbLong + 1);

            // Somme de controle du nom court, recopiee dans chaque morceau de nom long
            byte[] tmp = new byte[11];
            Array.Copy(court, tmp, 11);
            byte somme = Somme(tmp, 0);

            // Morceaux de nom long, du dernier au premier (c'est l'ordre sur le disque)
            for (int k = 0; k < nbLong; k++)
            {
                int seq = nbLong - k;
                int q = p + k * 32;
                for (int i = 0; i < 32; i++) r.Octets[q + i] = 0;
                r.Octets[q] = (byte)(seq | (k == 0 ? 0x40 : 0));
                r.Octets[q + 11] = AttrNomLong;
                r.Octets[q + 13] = somme;
                for (int i = 0; i < 13; i++)
                {
                    int idx = (seq - 1) * 13 + i;
                    int ch = idx < nom.Length ? nom[idx] : (idx == nom.Length ? 0x0000 : 0xFFFF);
                    int o = i < 5 ? 1 + i * 2 : (i < 11 ? 14 + (i - 5) * 2 : 28 + (i - 11) * 2);
                    r.Octets[q + o] = (byte)ch;
                    r.Octets[q + o + 1] = (byte)(ch >> 8);
                }
            }

            // Entree courte
            int e = p + nbLong * 32;
            for (int i = 0; i < 32; i++) r.Octets[e + i] = 0;
            for (int i = 0; i < 11; i++) r.Octets[e + i] = court[i];
            r.Octets[e + 11] = attr;
            DateHeure(out ushort date, out ushort heure);
            W16(r.Octets, e + 14, heure);   // creation
            W16(r.Octets, e + 16, date);
            W16(r.Octets, e + 18, date);    // dernier acces
            W16(r.Octets, e + 20, grappe >> 16);
            W16(r.Octets, e + 22, heure);   // modification
            W16(r.Octets, e + 24, date);
            W16(r.Octets, e + 26, grappe & 0xFFFF);
            W32(r.Octets, e + 28, taille);

            SauverRepertoire(r, p, e + 32);
        }

        private void MettreAJourEntree(Repertoire r, EntreeFat e, uint grappe, uint taille)
        {
            int p = e.Position;
            DateHeure(out ushort date, out ushort heure);
            W16(r.Octets, p + 20, grappe >> 16);
            W16(r.Octets, p + 26, grappe & 0xFFFF);
            W32(r.Octets, p + 28, taille);
            W16(r.Octets, p + 22, heure);
            W16(r.Octets, p + 24, date);
            W16(r.Octets, p + 18, date);
            SauverRepertoire(r, p, p + 32);
        }

        private void EffacerEntree(Repertoire r, EntreeFat e)
        {
            int debut = e.Position - e.NbLongues * 32;
            for (int p = debut; p <= e.Position; p += 32) r.Octets[p] = 0xE5;
            SauverRepertoire(r, debut, e.Position + 32);
        }

        // ================================================================
        //  Ecriture
        // ================================================================

        /// <summary>Ecrit les donnees dans une chaine neuve ; renvoie sa premiere grappe (0 si vide).</summary>
        private uint EcrireDonnees(byte[] data)
        {
            if (data.Length == 0) return 0;
            int n = (int)((data.Length + OctetsParGrappe - 1) / OctetsParGrappe);
            var ch = Allouer(n);
            ViderFat();
            for (int i = 0; i < ch.Count; i++)
            {
                byte[] g = new byte[OctetsParGrappe];
                int debut = i * (int)OctetsParGrappe;
                int nb = data.Length - debut;
                if (nb > (int)OctetsParGrappe) nb = (int)OctetsParGrappe;
                Array.Copy(data, debut, g, 0, nb);
                EcrireGrappe(ch[i], g);
            }
            return ch[0];
        }

        /// <summary>Cree ou remplace un fichier. Le fichier est relu et compare apres l'ecriture.</summary>
        public void Ecrire(string chemin, byte[] data)
        {
            ExigerEcriture();
            if (data.Length > TailleMaxFichier) throw new Exception("fichier trop gros (" + Fmt.Size(data.Length) + ")");
            var m = Morceaux(chemin);
            if (m.Count == 0) throw new Exception("chemin vide");
            string nom = m[m.Count - 1];
            VerifierNom(nom);

            uint parent = Dossier(m, m.Count - 1);
            var r = ChargerRepertoire(parent);
            var existant = Chercher(Entrees(r), nom);
            if (existant != null && existant.Dossier) throw new Exception("'" + nom + "' est un dossier");

            uint premiere = EcrireDonnees(data);
            if (existant != null)
            {
                uint ancienne = existant.Grappe;
                MettreAJourEntree(r, existant, premiere, (uint)data.Length);
                LibererChaine(ancienne);                     // l'ancien contenu n'est libere qu'apres
            }
            else CreerEntree(r, nom, AttrArchive, premiere, (uint)data.Length);

            Terminer();

            if (Verifier)
            {
                byte[] relu = Lire(chemin);
                Verifications++;
                bool ok = relu.Length == data.Length;
                for (int i = 0; ok && i < data.Length; i++) if (relu[i] != data[i]) ok = false;
                if (!ok) { Erreurs++; throw new Exception("VERIFICATION ECHOUEE : le fichier relu differe de l'original"); }
            }
        }

        public void CreerDossier(string chemin)
        {
            ExigerEcriture();
            var m = Morceaux(chemin);
            if (m.Count == 0) throw new Exception("chemin vide");
            string nom = m[m.Count - 1];
            VerifierNom(nom);

            uint parent = Dossier(m, m.Count - 1);
            var r = ChargerRepertoire(parent);
            var existant = Chercher(Entrees(r), nom);
            if (existant != null)
            {
                if (existant.Dossier) return;                       // deja la : rien a faire
                throw new Exception("un fichier porte deja ce nom");
            }

            // Grappe du nouveau dossier, avec les entrees "." et ".."
            var g = Allouer(1);
            ViderFat();
            byte[] b = new byte[OctetsParGrappe];
            DateHeure(out ushort date, out ushort heure);
            for (int k = 0; k < 2; k++)
            {
                int p = k * 32;
                for (int i = 0; i < 11; i++) b[p + i] = (byte)' ';
                b[p] = (byte)'.';
                if (k == 1) b[p + 1] = (byte)'.';
                b[p + 11] = AttrDossier;
                uint cible = k == 0 ? g[0] : (parent == grappeRacine ? 0 : parent);   // ".." vers la racine = 0
                W16(b, p + 20, cible >> 16);
                W16(b, p + 26, cible & 0xFFFF);
                W16(b, p + 22, heure);
                W16(b, p + 24, date);
            }
            EcrireGrappe(g[0], b);

            CreerEntree(r, nom, AttrDossier, g[0], 0);
            Terminer();
        }

        public void Supprimer(string chemin)
        {
            ExigerEcriture();
            var m = Morceaux(chemin);
            if (m.Count == 0) throw new Exception("impossible de supprimer la racine");
            uint parent = Dossier(m, m.Count - 1);
            var r = ChargerRepertoire(parent);
            var e = Chercher(Entrees(r), m[m.Count - 1]);
            if (e == null) throw new Exception("introuvable : " + chemin);
            if (e.Dossier && Entrees(ChargerRepertoire(e.Grappe)).Count > 0)
                throw new Exception("le dossier n'est pas vide");
            EffacerEntree(r, e);
            LibererChaine(e.Grappe);
            Terminer();
        }

        private static void VerifierNom(string nom)
        {
            if (nom.Length == 0 || nom.Length > 255) throw new Exception("nom invalide");
            for (int i = 0; i < nom.Length; i++)
            {
                char c = nom[i];
                if (c < 32 || c == '"' || c == '*' || c == '/' || c == ':' || c == '<' || c == '>' ||
                    c == '?' || c == '\\' || c == '|')
                    throw new Exception("caractere interdit dans le nom : '" + c + "'");
            }
            if (nom == "." || nom == "..") throw new Exception("nom reserve");
        }

        /// <summary>Fin d'operation : table FAT sur le disque, FSInfo a jour, cache du disque vide.</summary>
        private void Terminer()
        {
            ViderFat();
            try
            {
                if (fsInfo > 0 && fsInfo < reserves)
                {
                    byte[] fi = new byte[512];
                    Disque.ReadBlock(Debut + fsInfo, 1, ref fi);
                    if (U32(fi, 0) == 0x41615252 && U32(fi, 484) == 0x61417272)
                    {
                        W32(fi, 488, 0xFFFFFFFF);          // nombre de grappes libres : "a recalculer"
                        W32(fi, 492, indiceLibre);         // la ou chercher la prochaine fois
                        Disque.WriteBlock(Debut + fsInfo, 1, ref fi);
                    }
                }
            }
            catch (Exception) { }

            // Le pilote AHCI vide le cache interne du SSD : les donnees sont vraiment ecrites
            if (Disque is Ahci a) a.Vider();
        }

        // ================================================================
        //  Espace et controle
        // ================================================================

        /// <summary>Compte les grappes libres en parcourant toute la table (peut etre long).</summary>
        public uint GrappesLibres()
        {
            uint n = 0;
            for (uint c = 2; c < nbGrappes + 2; c++) if (LireFat(c) == 0) n++;
            return n;
        }

        public List<string> Resume()
        {
            var r = new List<string>();
            r.Add("Volume FAT32 \"" + Etiquette + "\" (" + Oem + ") sur " + NomDisque + ", secteur " + Debut);
            r.Add("  " + nbGrappes + " grappes de " + (OctetsParGrappe / 1024) + " Ko = " + Fmt.Size(Capacite) +
                  ", " + nbFats + " table(s) FAT de " + secteursParFat + " secteurs");
            r.Add("  " + (Ecriture ? "LECTURE ET ECRITURE" : "lecture seule") +
                  (Verifier ? ", verification apres ecriture" : "") +
                  "   (" + Lectures + " lectures, " + Ecritures + " ecritures, " + Erreurs + " erreur(s))");
            return r;
        }
    }
}
