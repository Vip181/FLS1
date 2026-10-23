using System;
using System.Collections.Generic;

namespace FileSystemOS.Pilotes
{
    /// <summary>
    /// WI-FI : etat reel de la prise en charge.
    ///
    /// Pourquoi aucune box n'est listee : pour voir les reseaux autour de soi, il faut
    ///   1. un pilote PROPRE A LA PUCE (chaque modele a ses registres : Intel 8260, Realtek
    ///      RTL8822, Qualcomm... aucun ne ressemble a un autre) ;
    ///   2. le MICROLOGICIEL du fabricant, un fichier binaire ferme qu'il faut charger dans la
    ///      carte au demarrage et que l'on n'a pas le droit de redistribuer ;
    ///   3. toute la pile 802.11 : balises, authentification, association ;
    ///   4. le chiffrement WPA2/WPA3 : handshake en quatre temps, AES-CCMP.
    ///
    /// C'est le travail le plus lourd d'un systeme d'exploitation, et il ne peut pas etre
    /// ecrit a l'aveugle. Cette classe dit donc ce qui est detecte, et ce qui manque.
    /// </summary>
    public static class Wifi
    {
        public static List<string> Rechercher()
        {
            var r = new List<string>();
            var cartes = PciCatalogue.Reseau();
            int wifi = 0;
            for (int i = 0; i < cartes.Count; i++) if (cartes[i].Wifi) wifi++;

            if (wifi == 0)
            {
                r.Add("Aucune carte Wi-Fi sur cette machine.");
                return r;
            }

            for (int i = 0; i < cartes.Count; i++)
            {
                if (!cartes[i].Wifi) continue;
                r.Add("Carte : " + cartes[i].Name + "  [" + cartes[i].Id + "]  emplacement " + cartes[i].Location);
            }
            r.Add("Aucune box ne peut etre listee : il manque le pilote de cette puce,");
            r.Add("son micrologiciel ferme (fourni par le fabricant) et le chiffrement WPA2.");
            r.Add("Branchez un cable Ethernet sur la box : aucun mot de passe n'est alors necessaire.");
            return r;
        }
    }
}
