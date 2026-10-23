using System;
using System.Collections.Generic;
using FileSystemOS.Utils;

namespace FileSystemOS.Fsl
{
    /// <summary>Jeu d'instructions de la machine virtuelle FSL.</summary>
    public static class Op
    {
        public const int Halt = 0, PushInt = 1, PushStr = 2, PushNull = 3, Load = 4, Store = 5;
        public const int Add = 6, Sub = 7, Mul = 8, Div = 9, Mod = 10;
        public const int Eq = 11, Ne = 12, Lt = 13, Gt = 14, Le = 15, Ge = 16;
        public const int And = 17, Or = 18, Not = 19, Neg = 20;
        public const int Jmp = 21, Jz = 22, Print = 23;
        public const int Appel = 24;     // Appel <fonction integree> <nombre d'arguments>
        public const int Pop = 25;       // oublie la valeur au sommet de la pile
        public const int LoadL = 26;     // LoadL <case> : variable locale d'une fonction
        public const int StoreL = 27;    // StoreL <case>
        public const int AppelF = 28;    // AppelF <fonction du programme> <nombre d'arguments>
        public const int Retour = 29;    // fin de fonction : renvoie le sommet de la pile

        public static readonly string[] Noms =
        {
            "Halt", "PushInt", "PushStr", "PushNull", "Load", "Store", "Add", "Sub", "Mul", "Div", "Mod",
            "Eq", "Ne", "Lt", "Gt", "Le", "Ge", "And", "Or", "Not", "Neg", "Jmp", "Jz", "Print",
            "Appel", "Pop", "LoadL", "StoreL", "AppelF", "Retour"
        };

        /// <summary>Nombre de mots de l'instruction (code + operandes).</summary>
        public static int Taille(int op)
        {
            switch (op)
            {
                case PushInt: case PushStr: case Load: case Store: case Jmp: case Jz: case LoadL: case StoreL: return 2;
                case Appel: case AppelF: return 3;
                default: return 1;
            }
        }
    }

    /// <summary>Une fonction ecrite dans le programme : fonction entier carre(entier x) { ... }</summary>
    public class FonctionFsl
    {
        public string Nom = "";
        public int Retour;                                  // 0 rien, sinon un type
        public int NbParams;
        public int Entree, Fin;                             // premier mot du corps, mot apres le corps
        public readonly List<string> Locales = new List<string>();   // parametres d'abord
        public readonly List<int> Types = new List<int>();
    }

    /// <summary>
    /// Programme compile : bytecode + textes + variables globales + fonctions.
    /// Fichier .fsc : "FSC2" (avec fonctions) ; les anciens "FSC1" se lisent toujours.
    /// </summary>
    public class FslProgram
    {
        public const int TypeRien = 0, TypeInt = 1, TypeTexte = 2, TypeBool = 3;

        public readonly List<int> Code = new List<int>();
        public readonly List<int> Lines = new List<int>();
        public readonly List<string> Strings = new List<string>();
        public readonly List<string> VarNames = new List<string>();
        public readonly List<int> VarTypes = new List<int>();
        public readonly List<FonctionFsl> Fonctions = new List<FonctionFsl>();

        public static string NomType(int t) =>
            t == TypeInt ? "entier" : (t == TypeTexte ? "texte" : (t == TypeBool ? "booleen" : "rien"));

        public int Emit(int v, int line)
        {
            Code.Add(v);
            Lines.Add(line);
            return Code.Count - 1;
        }

        public int TrouverFonction(string nom)
        {
            for (int i = 0; i < Fonctions.Count; i++) if (Fonctions[i].Nom == nom) return i;
            return -1;
        }

        // ---- Fichier .fsc ----
        public void Save(string path)
        {
            var o = new Tampon(4096);
            o.AjouterTexte("FSC2");
            WriteInt(o, Strings.Count);
            for (int i = 0; i < Strings.Count; i++) WriteStr(o, Strings[i]);
            WriteInt(o, VarNames.Count);
            for (int i = 0; i < VarNames.Count; i++) { WriteStr(o, VarNames[i]); WriteInt(o, VarTypes[i]); }
            WriteInt(o, Fonctions.Count);
            for (int i = 0; i < Fonctions.Count; i++)
            {
                FonctionFsl f = Fonctions[i];
                WriteStr(o, f.Nom); WriteInt(o, f.Retour); WriteInt(o, f.NbParams);
                WriteInt(o, f.Entree); WriteInt(o, f.Fin);
                WriteInt(o, f.Locales.Count);
                for (int k = 0; k < f.Locales.Count; k++) { WriteStr(o, f.Locales[k]); WriteInt(o, f.Types[k]); }
            }
            WriteInt(o, Code.Count);
            for (int i = 0; i < Code.Count; i++) { WriteInt(o, Code[i]); WriteInt(o, Lines[i]); }
            FsTools.WriteBytes(path, o.VersTableau());
        }

        public static FslProgram Load(string path)
        {
            byte[] d = FsTools.ReadBytes(path);
            if (d.Length < 4 || d[0] != 'F' || d[1] != 'S' || d[2] != 'C' || (d[3] != '1' && d[3] != '2'))
                throw new Exception("ce fichier n'est pas un programme FSL compile");
            bool v2 = d[3] == '2';
            var p = new FslProgram();
            int pos = 4;
            int ns = ReadInt(d, ref pos);
            for (int i = 0; i < ns; i++) p.Strings.Add(ReadStr(d, ref pos));
            int nv = ReadInt(d, ref pos);
            for (int i = 0; i < nv; i++) { p.VarNames.Add(ReadStr(d, ref pos)); p.VarTypes.Add(ReadInt(d, ref pos)); }
            if (v2)
            {
                int nf = ReadInt(d, ref pos);
                for (int i = 0; i < nf; i++)
                {
                    var f = new FonctionFsl();
                    f.Nom = ReadStr(d, ref pos); f.Retour = ReadInt(d, ref pos); f.NbParams = ReadInt(d, ref pos);
                    f.Entree = ReadInt(d, ref pos); f.Fin = ReadInt(d, ref pos);
                    int nl = ReadInt(d, ref pos);
                    for (int k = 0; k < nl; k++) { f.Locales.Add(ReadStr(d, ref pos)); f.Types.Add(ReadInt(d, ref pos)); }
                    p.Fonctions.Add(f);
                }
            }
            int nc = ReadInt(d, ref pos);
            for (int i = 0; i < nc; i++) { p.Code.Add(ReadInt(d, ref pos)); p.Lines.Add(ReadInt(d, ref pos)); }
            return p;
        }

        private static void WriteInt(Tampon o, int v)
        {
            o.Ajouter((byte)v); o.Ajouter((byte)(v >> 8)); o.Ajouter((byte)(v >> 16)); o.Ajouter((byte)(v >> 24));
        }

        private static void WriteStr(Tampon o, string s)
        {
            WriteInt(o, s.Length);
            for (int i = 0; i < s.Length; i++) o.Ajouter(s[i] < 256 ? (byte)s[i] : (byte)'?');
        }

        private static int ReadInt(byte[] d, ref int pos)
        {
            if (pos + 4 > d.Length) throw new Exception("fichier .fsc tronque");
            int v = d[pos] | (d[pos + 1] << 8) | (d[pos + 2] << 16) | (d[pos + 3] << 24);
            pos += 4;
            return v;
        }

        private static string ReadStr(byte[] d, ref int pos)
        {
            int n = ReadInt(d, ref pos);
            if (n < 0 || pos + n > d.Length) throw new Exception("fichier .fsc tronque");
            char[] c = new char[n];
            for (int i = 0; i < n; i++) c[i] = (char)d[pos + i];
            pos += n;
            return new string(c);
        }
    }
}
