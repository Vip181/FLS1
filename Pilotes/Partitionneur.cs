using System;
using Cosmos.HAL.BlockDevice;

namespace FileSystemOS.Pilotes
{
    /// <summary>Un espace NON ALLOUE d'un disque, ou une partition FSOS peut etre creee.</summary>
    public class EspaceLibre
    {
        public ulong Debut, Secteurs;
        public bool Gpt;
        public int Emplacement;          // case libre de la table des partitions
        public long Octets => (long)Secteurs * 512;
    }

    /// <summary>
    /// PARTITIONNEUR DE FILE SYSTEM OS : cree la partition FSOS sans Windows ni Linux.
    ///
    /// Il n'utilise QUE l'espace non alloue du disque (place libre entre ou apres les
    /// partitions) : aucune partition existante n'est deplacee, reduite ni modifiee. Reduire
    /// la partition de Windows (C:) demanderait de deplacer ses fichiers NTFS, ce que seul
    /// Windows sait faire sans risque : dans ce cas, reduisez C: dans la Gestion des disques,
    /// puis laissez File System OS creer sa partition dans la place liberee.
    ///
    /// GPT : verification des sommes de controle CRC32 avant toute ecriture ; la nouvelle
    /// entree est ecrite dans la copie de SECOURS (fin du disque) puis dans la copie principale,
    /// chaque en-tete recalcule. MBR : une case primaire libre, type 0x0C (FAT32).
    /// Partition alignee sur 1 Mo, de 64 Mo a 32 Go, formatee en FAT32 avec le nom FSOS.
    /// Logique validee en Python sur une image GPT avant d'etre traduite.
    /// </summary>
    public static class Partitionneur
    {
        private const ulong Alignement = 2048;              // 1 Mo
        private const ulong Minimum = 131072;               // 64 Mo
        private const ulong Maximum = 67108864;             // 32 Go

        private static readonly byte[] TypeDonnees =
            { 0xA2, 0xA0, 0xD0, 0xEB, 0xE5, 0xB9, 0x33, 0x44, 0x87, 0xC0, 0x68, 0xB6, 0xB7, 0x26, 0x99, 0xC7 };

        public static string Journal = "";

        // ================================================================
        //  Recherche de l'espace libre
        // ================================================================

        /// <summary>Le plus grand espace libre utilisable (64 Mo au moins), ou null.</summary>
        public static EspaceLibre Chercher(BlockDevice d)
        {
            try
            {
                byte[] s0 = Lire(d, 0, 1);
                if (s0[510] != 0x55 || s0[511] != 0xAA) { Journal = "disque sans table de partitions"; return null; }
                for (int i = 0; i < 4; i++)
                    if (s0[446 + i * 16 + 4] == 0xEE) return ChercherGpt(d);
                return ChercherMbr(d, s0);
            }
            catch (Exception e) { Journal = "lecture impossible : " + e.Message; return null; }
        }

        private static EspaceLibre ChercherGpt(BlockDevice d)
        {
            byte[] h = Lire(d, 1, 1);
            if (!EnTeteValide(h)) { Journal = "table GPT absente ou abimee : rien ne sera modifie"; return null; }
            ulong premier = U64(h, 40), dernier = U64(h, 48), lbaEntrees = U64(h, 72);
            int nb = (int)U32(h, 80), taille = (int)U32(h, 84);
            byte[] ent = LireEntrees(d, lbaEntrees, nb, taille);
            if (Crc32(ent, 0, nb * taille) != U32(h, 88)) { Journal = "entrees GPT abimees : rien ne sera modifie"; return null; }

            int libre = -1;
            ulong[] debuts = new ulong[nb], fins = new ulong[nb];
            int n = 0;
            for (int i = 0; i < nb; i++)
            {
                int e = i * taille;
                if (Vide(ent, e, 16)) { if (libre < 0) libre = i; continue; }
                debuts[n] = U64(ent, e + 32);
                fins[n] = U64(ent, e + 40);
                n++;
            }
            if (libre < 0) { Journal = "table GPT pleine"; return null; }
            Trier(debuts, fins, n);

            var meilleur = new EspaceLibre { Gpt = true, Emplacement = libre };
            ulong curseur = premier;
            for (int i = 0; i < n; i++)
            {
                if (debuts[i] > curseur) Retenir(meilleur, curseur, debuts[i] - 1);
                if (fins[i] + 1 > curseur) curseur = fins[i] + 1;
            }
            Retenir(meilleur, curseur, dernier);
            if (meilleur.Secteurs == 0) { Journal = "aucun espace libre de 64 Mo"; return null; }
            Journal = "espace libre : " + (meilleur.Octets / 1048576) + " Mo (GPT, case " + (libre + 1) + ")";
            return meilleur;
        }

        private static EspaceLibre ChercherMbr(BlockDevice d, byte[] s0)
        {
            int libre = -1, n = 0;
            ulong[] debuts = new ulong[4], fins = new ulong[4];
            for (int i = 0; i < 4; i++)
            {
                int e = 446 + i * 16;
                if (s0[e + 4] == 0) { if (libre < 0) libre = i; continue; }
                debuts[n] = U32(s0, e + 8);
                fins[n] = debuts[n] + U32(s0, e + 12) - 1;
                n++;
            }
            if (libre < 0) { Journal = "4 partitions principales deja utilisees"; return null; }
            Trier(debuts, fins, n);
            ulong fin = d.BlockCount - 1;
            if (fin > 0xFFFFFFFF) fin = 0xFFFFFFFF;          // limite du MBR (2 To)

            var meilleur = new EspaceLibre { Gpt = false, Emplacement = libre };
            ulong curseur = Alignement;
            for (int i = 0; i < n; i++)
            {
                if (debuts[i] > curseur) Retenir(meilleur, curseur, debuts[i] - 1);
                if (fins[i] + 1 > curseur) curseur = fins[i] + 1;
            }
            Retenir(meilleur, curseur, fin);
            if (meilleur.Secteurs == 0) { Journal = "aucun espace libre de 64 Mo"; return null; }
            Journal = "espace libre : " + (meilleur.Octets / 1048576) + " Mo (MBR, case " + (libre + 1) + ")";
            return meilleur;
        }

        /// <summary>Garde l'intervalle [a, b] s'il est le plus grand (aligne sur 1 Mo, 64 Mo a 32 Go).</summary>
        private static void Retenir(EspaceLibre m, ulong a, ulong b)
        {
            a = (a + Alignement - 1) / Alignement * Alignement;
            if (b < a) return;
            ulong n = (b - a + 1) / Alignement * Alignement;
            if (n > Maximum) n = Maximum;
            if (n >= Minimum && n > m.Secteurs) { m.Debut = a; m.Secteurs = n; }
        }

        // ================================================================
        //  Creation de la partition
        // ================================================================

        /// <summary>Cree la partition FSOS dans l'espace libre et la formate en FAT32.</summary>
        public static PartitionInfo Creer(BlockDevice d, EspaceLibre e, out string compteRendu)
        {
            if (e.Gpt) CreerGpt(d, e); else CreerMbr(d, e);
            if (d is Ahci a) a.Vider();
            string f = FileSystemOS.Systeme.Fat32.Formater(d, e.Debut, e.Secteurs, "FSOS");
            if (d is Ahci a2) a2.Vider();
            compteRendu = "Partition " + (e.Emplacement + 1) + " creee (" + (e.Octets / 1048576) + " Mo), " + f;
            return new PartitionInfo
            {
                Numero = e.Emplacement + 1, Debut = e.Debut, Secteurs = e.Secteurs,
                Type = "Donnees Windows", Nom = "FSOS", Fat = true
            };
        }

        private static void CreerGpt(BlockDevice d, EspaceLibre e)
        {
            byte[] h = Lire(d, 1, 1);
            if (!EnTeteValide(h)) throw new Exception("table GPT abimee : rien n'a ete modifie");
            int tailleEnTete = (int)U32(h, 12);
            ulong lbaEntrees = U64(h, 72), lbaSecours = U64(h, 32);
            int nb = (int)U32(h, 80), taille = (int)U32(h, 84);
            byte[] ent = LireEntrees(d, lbaEntrees, nb, taille);
            int o = e.Emplacement * taille;
            if (!Vide(ent, o, 16)) throw new Exception("la case choisie n'est plus libre");

            byte[] hs = Lire(d, lbaSecours, 1);
            if (!EnTeteValide(hs)) throw new Exception("copie de secours GPT abimee : rien n'a ete modifie");
            ulong lbaEntreesSecours = U64(hs, 72);

            // Nouvelle entree : type "donnees", identifiant unique, debut, fin, nom FSOS
            for (int i = 0; i < 16; i++) ent[o + i] = TypeDonnees[i];
            byte[] guid = NouveauGuid(e.Debut);
            for (int i = 0; i < 16; i++) ent[o + 16 + i] = guid[i];
            W64(ent, o + 32, e.Debut);
            W64(ent, o + 40, e.Debut + e.Secteurs - 1);
            W64(ent, o + 48, 0);
            for (int i = 56; i < taille; i++) ent[o + i] = 0;
            string nom = "FSOS";
            for (int i = 0; i < nom.Length; i++) ent[o + 56 + i * 2] = (byte)nom[i];
            uint crcEntrees = Crc32(ent, 0, nb * taille);

            // 1. Copie de SECOURS d'abord (fin du disque), 2. puis la copie principale
            Ecrire(d, lbaEntreesSecours, ent);
            W32(hs, 88, crcEntrees); W32(hs, 16, 0); W32(hs, 16, Crc32(hs, 0, tailleEnTete));
            Ecrire(d, lbaSecours, hs);
            Ecrire(d, lbaEntrees, ent);
            W32(h, 88, crcEntrees); W32(h, 16, 0); W32(h, 16, Crc32(h, 0, tailleEnTete));
            Ecrire(d, 1, h);

            // Relecture : les deux en-tetes doivent etre valides
            if (!EnTeteValide(Lire(d, 1, 1)) || !EnTeteValide(Lire(d, lbaSecours, 1)))
                throw new Exception("verification de la table GPT echouee apres ecriture");
        }

        private static void CreerMbr(BlockDevice d, EspaceLibre e)
        {
            byte[] s0 = Lire(d, 0, 1);
            int o = 446 + e.Emplacement * 16;
            if (s0[o + 4] != 0) throw new Exception("la case choisie n'est plus libre");
            s0[o] = 0x00;
            s0[o + 1] = 0xFE; s0[o + 2] = 0xFF; s0[o + 3] = 0xFF;     // CHS "au-dela" : adressage LBA
            s0[o + 4] = 0x0C;                                          // FAT32 (LBA)
            s0[o + 5] = 0xFE; s0[o + 6] = 0xFF; s0[o + 7] = 0xFF;
            W32(s0, o + 8, (uint)e.Debut);
            W32(s0, o + 12, (uint)e.Secteurs);
            Ecrire(d, 0, s0);
            byte[] v = Lire(d, 0, 1);
            if (v[o + 4] != 0x0C || U32(v, o + 8) != (uint)e.Debut) throw new Exception("verification du MBR echouee");
        }

        // ================================================================
        //  Outils
        // ================================================================

        private static bool EnTeteValide(byte[] h)
        {
            if (h[0] != (byte)'E' || h[1] != (byte)'F' || h[2] != (byte)'I' || h[3] != (byte)' ' ||
                h[4] != (byte)'P' || h[5] != (byte)'A' || h[6] != (byte)'R' || h[7] != (byte)'T') return false;
            int taille = (int)U32(h, 12);
            if (taille < 92 || taille > 512) return false;
            uint attendu = U32(h, 16);
            byte[] copie = new byte[taille];
            for (int i = 0; i < taille; i++) copie[i] = h[i];
            W32(copie, 16, 0);
            return Crc32(copie, 0, taille) == attendu;
        }

        private static byte[] LireEntrees(BlockDevice d, ulong lba, int nb, int taille)
        {
            int octets = nb * taille;
            int secteurs = (octets + 511) / 512;
            return Lire(d, lba, secteurs);
        }

        private static byte[] Lire(BlockDevice d, ulong lba, int n)
        {
            byte[] b = new byte[n * 512];
            d.ReadBlock(lba, (ulong)n, ref b);
            return b;
        }

        private static void Ecrire(BlockDevice d, ulong lba, byte[] b)
        {
            d.WriteBlock(lba, (ulong)(b.Length / 512), ref b);
        }

        private static bool Vide(byte[] b, int o, int n)
        {
            for (int i = 0; i < n; i++) if (b[o + i] != 0) return false;
            return true;
        }

        private static void Trier(ulong[] debuts, ulong[] fins, int n)
        {
            for (int i = 1; i < n; i++)
                for (int j = i; j > 0 && debuts[j] < debuts[j - 1]; j--)
                {
                    ulong t = debuts[j]; debuts[j] = debuts[j - 1]; debuts[j - 1] = t;
                    t = fins[j]; fins[j] = fins[j - 1]; fins[j - 1] = t;
                }
        }

        private static byte[] NouveauGuid(ulong graine)
        {
            uint x = (uint)graine ^ 0x9E3779B9u;
            try { x ^= (uint)FileSystemOS.Processus.Horloge.Millis * 2654435761u; } catch (Exception) { }
            try { x ^= (uint)Cosmos.HAL.RTC.Second << 16 ^ (uint)Cosmos.HAL.RTC.Minute << 8; } catch (Exception) { }
            if (x == 0) x = 1;
            byte[] g = new byte[16];
            for (int i = 0; i < 16; i++)
            {
                x ^= x << 13; x ^= x >> 17; x ^= x << 5;      // xorshift
                g[i] = (byte)x;
            }
            g[7] = (byte)((g[7] & 0x0F) | 0x40);              // GUID version 4
            g[8] = (byte)((g[8] & 0x3F) | 0x80);
            return g;
        }

        private static uint[] table;
        private static uint Crc32(byte[] b, int debut, int n)
        {
            if (table == null)
            {
                table = new uint[256];
                for (uint i = 0; i < 256; i++)
                {
                    uint c = i;
                    for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                    table[i] = c;
                }
            }
            uint crc = 0xFFFFFFFFu;
            for (int i = debut; i < debut + n; i++) crc = table[(crc ^ b[i]) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }

        private static uint U32(byte[] b, int p) => (uint)(b[p] | b[p + 1] << 8 | b[p + 2] << 16 | b[p + 3] << 24);
        private static ulong U64(byte[] b, int p) => U32(b, p) | (ulong)U32(b, p + 4) << 32;
        private static void W32(byte[] b, int p, uint v)
        {
            b[p] = (byte)v; b[p + 1] = (byte)(v >> 8); b[p + 2] = (byte)(v >> 16); b[p + 3] = (byte)(v >> 24);
        }
        private static void W64(byte[] b, int p, ulong v) { W32(b, p, (uint)v); W32(b, p + 4, (uint)(v >> 32)); }
    }
}
