using System;
using Cosmos.HAL.BlockDevice;

namespace FileSystemOS.Systeme
{
    /// <summary>
    /// FORMATAGE FAT32 ecrit par File System OS.
    ///
    /// On n'utilise pas le formatage de Cosmos : il ne marche pas sur tous les disques.
    /// Ici, tout est ecrit a la main, secteur par secteur :
    ///   - le secteur de demarrage (BPB) avec la geometrie du systeme de fichiers ;
    ///   - le secteur d'information (FSInfo) ;
    ///   - une copie de secours des deux a l'emplacement 6 ;
    ///   - les deux tables FAT, remises a zero, avec les trois premieres entrees reservees ;
    ///   - le repertoire racine, vide, avec le nom du volume.
    /// Resultat : une partition que Cosmos ET Windows savent lire.
    /// </summary>
    public static class Fat32
    {
        public const uint TailleSecteur = 512;

        private static void U16(byte[] b, int p, uint v) { b[p] = (byte)v; b[p + 1] = (byte)(v >> 8); }
        private static void U32(byte[] b, int p, uint v) { U16(b, p, v & 0xFFFF); U16(b, p + 2, v >> 16); }
        private static void Txt(byte[] b, int p, string s, int n)
        {
            for (int i = 0; i < n; i++) b[p + i] = (byte)(i < s.Length ? s[i] : ' ');
        }

        /// <summary>Secteurs par grappe conseilles selon la taille, comme le fait Windows.</summary>
        public static uint GrappePour(ulong secteurs)
        {
            ulong mo = secteurs / 2048;
            if (mo <= 260) return 1;        // 512 o
            if (mo <= 8192) return 8;       // 4 Ko
            if (mo <= 16384) return 16;     // 8 Ko
            if (mo <= 32768) return 32;     // 16 Ko
            return 64;                      // 32 Ko
        }

        /// <summary>
        /// Formate en FAT32 la zone qui commence au secteur 'debut' et fait 'secteurs' secteurs.
        /// Renvoie un compte rendu ; leve une exception en cas d'echec d'ecriture.
        /// </summary>
        public static string Formater(BlockDevice d, ulong debut, ulong secteurs, string nom)
        {
            if (secteurs < 66600) throw new Exception("partition trop petite pour du FAT32 (33 Mo minimum)");

            uint spc = GrappePour(secteurs);
            const uint reserves = 32;
            uint fatSecteurs = 1;

            // On ajuste la taille des tables FAT jusqu'a ce qu'elle suffise aux grappes restantes
            for (int tour = 0; tour < 32; tour++)
            {
                ulong donnees = secteurs - reserves - 2UL * fatSecteurs;
                ulong grappes = donnees / spc;
                uint besoin = (uint)(((grappes + 2) * 4 + TailleSecteur - 1) / TailleSecteur);
                if (besoin <= fatSecteurs) break;
                fatSecteurs = besoin;
            }
            ulong grappesFinales = (secteurs - reserves - 2UL * fatSecteurs) / spc;
            if (grappesFinales < 65525) throw new Exception("pas assez de grappes pour du FAT32 (essayez une plus grosse partition)");

            byte[] s = new byte[TailleSecteur];

            // ---- Secteur de demarrage (BPB FAT32) ----
            s[0] = 0xEB; s[1] = 0x58; s[2] = 0x90;              // saut
            Txt(s, 3, "FSOS    ", 8);
            U16(s, 11, TailleSecteur);
            s[13] = (byte)spc;
            U16(s, 14, reserves);
            s[16] = 2;                                          // deux tables FAT
            U16(s, 17, 0);                                      // racine : 0 en FAT32
            U16(s, 19, 0);
            s[21] = 0xF8;                                       // disque fixe
            U16(s, 22, 0);                                      // FAT16 seulement
            U16(s, 24, 63); U16(s, 26, 255);                    // geometrie historique
            U32(s, 28, (uint)debut);
            U32(s, 32, (uint)secteurs);
            U32(s, 36, fatSecteurs);                            // secteurs par FAT (32 bits)
            U16(s, 40, 0);                                      // miroir actif
            U16(s, 42, 0);                                      // version
            U32(s, 44, 2);                                      // premiere grappe de la racine
            U16(s, 48, 1);                                      // FSInfo au secteur 1
            U16(s, 50, 6);                                      // copie de secours au secteur 6
            s[64] = 0x80;
            s[66] = 0x29;                                       // signature etendue
            U32(s, 67, 0x46534F53);                             // numero de serie "FSOS"
            Txt(s, 71, nom, 11);
            Txt(s, 82, "FAT32   ", 8);
            s[510] = 0x55; s[511] = 0xAA;
            d.WriteBlock(debut, 1, ref s);
            d.WriteBlock(debut + 6, 1, ref s);                  // copie de secours

            // ---- FSInfo ----
            byte[] info = new byte[TailleSecteur];
            U32(info, 0, 0x41615252);
            U32(info, 484, 0x61417272);
            U32(info, 488, (uint)(grappesFinales - 1));         // grappes libres
            U32(info, 492, 2);                                  // derniere grappe utilisee
            info[510] = 0x55; info[511] = 0xAA;
            d.WriteBlock(debut + 1, 1, ref info);
            d.WriteBlock(debut + 7, 1, ref info);

            // ---- Les deux tables FAT ----
            byte[] vide = new byte[TailleSecteur];
            byte[] premiere = new byte[TailleSecteur];
            U32(premiere, 0, 0x0FFFFFF8);                       // entree 0 : type de disque
            U32(premiere, 4, 0x0FFFFFFF);                       // entree 1 : reservee
            U32(premiere, 8, 0x0FFFFFF8);                       // entree 2 : racine, fin de chaine
            for (uint f = 0; f < 2; f++)
            {
                ulong basef = debut + reserves + f * fatSecteurs;
                d.WriteBlock(basef, 1, ref premiere);
                for (uint i = 1; i < fatSecteurs; i++) d.WriteBlock(basef + i, 1, ref vide);
            }

            // ---- Repertoire racine : une grappe vide, avec le nom du volume ----
            ulong racine = debut + reserves + 2UL * fatSecteurs;
            byte[] entree = new byte[TailleSecteur];
            Txt(entree, 0, nom, 11);
            entree[11] = 0x08;                                  // attribut "nom de volume"
            d.WriteBlock(racine, 1, ref entree);
            for (uint i = 1; i < spc; i++) d.WriteBlock(racine + i, 1, ref vide);

            return "FAT32 : " + grappesFinales + " grappes de " + (spc * TailleSecteur / 1024) + " Ko, " +
                   "2 tables de " + fatSecteurs + " secteurs, volume \"" + nom + "\"";
        }
    }
}
