using System;

namespace FileSystemOS.Utils
{
    /// <summary>Suite d'octets qui s'agrandit toute seule (plus legere qu'une List&lt;byte&gt;).</summary>
    public class Tampon
    {
        private byte[] donnees;
        public int Count;

        public Tampon(int capacite) { donnees = new byte[capacite < 16 ? 16 : capacite]; }

        public void Ajouter(byte b)
        {
            if (Count == donnees.Length) Agrandir(Count + 1);
            donnees[Count++] = b;
        }

        public void Ajouter(byte[] b, int debut, int n)
        {
            if (Count + n > donnees.Length) Agrandir(Count + n);
            for (int i = 0; i < n; i++) donnees[Count + i] = b[debut + i];
            Count += n;
        }

        public void Ajouter16(int v) { Ajouter((byte)v); Ajouter((byte)(v >> 8)); }

        public void AjouterTexte(string s) { for (int i = 0; i < s.Length; i++) Ajouter((byte)s[i]); }

        private void Agrandir(int minimum)
        {
            int n = donnees.Length * 2;
            if (n < minimum) n = minimum;
            byte[] nouveau = new byte[n];
            for (int i = 0; i < Count; i++) nouveau[i] = donnees[i];
            donnees = nouveau;
        }

        public byte[] VersTableau()
        {
            byte[] r = new byte[Count];
            for (int i = 0; i < Count; i++) r[i] = donnees[i];
            return r;
        }
    }
}
