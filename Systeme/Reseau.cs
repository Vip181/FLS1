using System;
using System.Collections.Generic;
using Cosmos.HAL;
using Cosmos.System.Network.Config;
using Cosmos.System.Network.IPv4.UDP.DHCP;
using FileSystemOS.Pilotes;
using FileSystemOS.Processus;

namespace FileSystemOS.Systeme
{
    /// <summary>
    /// Reseau : identifie toutes les cartes (catalogue PCI), demarre les pilotes
    /// (Cosmos : PCnet, RTL8139 ; File System OS : Intel E1000) et obtient
    /// l'adresse IP de la box par DHCP. Par cable, aucun mot de passe n'est necessaire.
    /// </summary>
    public static class Reseau
    {
        public static string Status = "non initialise";
        public static bool Connected;
        public static string Ip = "-", Gateway = "-", Dns = "-";
        public static int Drivers;

        public static string Init(bool dhcp)
        {
            Connected = false;
            try
            {
                PciCatalogue.Scan();
                Machine.Detect();

                // Les pilotes ne sont demarres que pour les puces CONNUES (voir PiloteReseau)
                PiloteReseau.Mode = Config.Get("pilotes_reseau", "auto").ToLower();
                E1000.Experimental = Config.GetBool("pilote_i219", false);   // I217/I218/I219 : sur demande
                PiloteReseau.Demarrer();
                Drivers = NetworkDevice.Devices.Count;

                if (Drivers == 0)
                {
                    var cards = PciCatalogue.Reseau();
                    if (cards.Count == 0) { Status = "aucune carte reseau detectee"; return Status; }
                    bool wifiOnly = true;
                    for (int i = 0; i < cards.Count; i++) if (!cards[i].Wifi) wifiOnly = false;
                    Status = Machine.Marque + " : " + cards.Count + " carte(s) detectee(s), aucune pilotable" +
                             (wifiOnly ? " (Wi-Fi uniquement)" : "") + " - " + PiloteReseau.Journal;
                    return Status;
                }

                string card = NetworkDevice.Devices[0].Name;
                if (!dhcp) { Status = card + " (DHCP desactive)"; return Status; }

                using (var client = new DHCPClient()) { client.SendDiscoverPacket(); }
                Refresh();
                Status = Connected ? card + " - IP " + Ip : card + " : la box n'a pas repondu (cable branche ?)";
            }
            catch (Exception e) { Status = "erreur : " + e.Message; }
            return Status;
        }

        /// <summary>
        /// Relit l'adresse IP, la passerelle (box) et le DNS.
        /// IMPORTANT : sans carte ou sans configuration, Cosmos n'a AUCUNE configuration reseau ;
        /// y acceder directement provoquait un plantage complet de l'OS (non rattrapable).
        /// On verifie donc chaque objet avant de l'utiliser.
        /// </summary>
        public static void Refresh()
        {
            Connected = false;
            Ip = "-"; Gateway = "-"; Dns = "-";
            if (NetworkDevice.Devices == null || NetworkDevice.Devices.Count == 0) return;
            try
            {
                var cfg = NetworkConfiguration.CurrentNetworkConfig;
                if (cfg != null && cfg.IPConfig != null)
                {
                    var ip = cfg.IPConfig.IPAddress;
                    if (ip != null)
                    {
                        Ip = ip.ToString();
                        Connected = Ip != "0.0.0.0";
                    }
                    var gw = cfg.IPConfig.DefaultGateway;
                    if (gw != null) Gateway = gw.ToString();
                }
            }
            catch (Exception) { Connected = false; }
            try
            {
                var dns = DNSConfig.DNSNameservers;
                if (dns != null && dns.Count > 0 && dns[0] != null) Dns = dns[0].ToString();
            }
            catch (Exception) { }
        }

        public static List<string> Report()
        {
            var r = new List<string>();
            var m = Machine.Report();
            for (int i = 0; i < m.Count; i++) r.Add(m[i]);
            r.Add("Pilotes   : " + PiloteReseau.Journal);
            r.Add("Etat      : " + Status);
            r.Add("IP        : " + Ip + "   Box (passerelle) : " + Gateway + "   DNS : " + Dns);
            var cards = PciCatalogue.Reseau();
            r.Add("Cartes reseau detectees : " + cards.Count);
            for (int i = 0; i < cards.Count; i++)
            {
                var c = cards[i];
                r.Add("  " + c.Name + " [" + c.Id + "] " + (c.Wifi ? "Wi-Fi" : "Ethernet") + " - " + c.Driver);
            }
            for (int i = 0; i < E1000.Cards.Count; i++)
                r.Add("  E1000 " + E1000.Cards[i].Name + " MAC " + E1000.Cards[i].MacText() +
                      (E1000.Cards[i].LinkUp ? " lien actif" : " pas de lien") +
                      " (" + E1000.Cards[i].Sent + " envoyes / " + E1000.Cards[i].Received + " recus)");
            return r;
        }
    }

    /// <summary>Processus "reseau" : releve les paquets E1000 et garde l'etat a jour.</summary>
    public class ReseauThread : KThread
    {
        private int counter;
        public ReseauThread() : base("etat") { }

        public override void Step()
        {
            for (int i = 0; i < E1000.Cards.Count; i++) E1000.Cards[i].Poll();   // secours si pas d'interruption
            if (++counter < 200) return;
            counter = 0;
            if (Reseau.Drivers == 0 || !Reseau.Connected) return;    // rien a surveiller
            Reseau.Refresh();
            if (!Reseau.Connected) Reseau.Status = "connexion perdue";
        }
    }
}
