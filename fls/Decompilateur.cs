using System;
using System.Collections.Generic;

namespace FileSystemOS.Fsl
{
    /// <summary>
    /// DECOMPILATEUR : transforme un programme compile (.fsc) en code FSL lisible.
    ///
    /// Il reconnait les formes que produit le compilateur : expressions (rebaties depuis la pile),
    /// declarations, si / sinon, tantque (une boucle "pour" revient sous forme de tantque, ce qui
    /// est equivalent), sortir, fonctions et retourner. Les noms des variables et des fonctions
    /// sont dans le fichier .fsc, ils reviennent donc tels quels ; les commentaires et la mise en
    /// page d'origine, eux, ne sont pas dans le binaire.
    ///
    /// Garantie verifiable : recompiler le code obtenu redonne EXACTEMENT le meme bytecode
    /// (commande "fsl verifier" de la Console).
    /// </summary>
    public class Decompilateur
    {
        private FslProgram p;
        private int[] c;
        private readonly List<string> lignes = new List<string>();
        private bool[] globDeclaree, locDeclaree;
        private FonctionFsl fn;

        public static string Decompiler(FslProgram prog)
        {
            var d = new Decompilateur();
            d.p = prog;
            d.c = prog.Code.ToArray();
            d.globDeclaree = new bool[prog.VarNames.Count];
            int fin = d.c.Length;
            if (fin > 0 && d.c[fin - 1] == Op.Halt) fin--;
            d.lignes.Add("// Decompile par File System OS " + Services.Version);
            d.Bloc(0, fin, 0, -1);
            return Joindre(d.lignes);
        }

        /// <summary>Compile, decompile, recompile : null si le bytecode est identique, sinon la difference.</summary>
        public static string VerifierAllerRetour(string source, out string decompile)
        {
            FslProgram a = Compiler.Compile(source);
            decompile = Decompiler(a);
            FslProgram b;
            try { b = Compiler.Compile(decompile); }
            catch (Exception e) { return "le code decompile ne se recompile pas : " + e.Message; }
            if (a.Code.Count != b.Code.Count) return "longueurs differentes (" + a.Code.Count + " / " + b.Code.Count + " mots)";
            for (int i = 0; i < a.Code.Count; i++)
                if (a.Code[i] != b.Code[i]) return "difference au mot " + i + " (ligne source " + a.Lines[i] + ")";
            if (a.Fonctions.Count != b.Fonctions.Count || a.VarNames.Count != b.VarNames.Count) return "tables differentes";
            for (int i = 0; i < a.Strings.Count && i < b.Strings.Count; i++)
                if (a.Strings[i] != b.Strings[i]) return "texte " + i + " different";
            return null;
        }

        // ---- Sortie ----
        private void Ligne(int indent, string s)
        {
            string esp = "";
            for (int i = 0; i < indent; i++) esp += "    ";
            lignes.Add(esp + s);
        }

        private static string Joindre(List<string> l)
        {
            int n = 0;
            for (int i = 0; i < l.Count; i++) n += l[i].Length + 1;
            char[] r = new char[n];
            int k = 0;
            for (int i = 0; i < l.Count; i++)
            {
                string s = l[i];
                for (int j = 0; j < s.Length; j++) r[k++] = s[j];
                r[k++] = '\n';
            }
            return new string(r);
        }

        private static string Texte(string s)
        {
            string r = "\"";
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (ch == '\\') r += "\\\\";
                else if (ch == '"') r += "\\\"";
                else if (ch == '\n') r += "\\n";
                else r += ch;
            }
            return r + "\"";
        }

        private static string Nombre(int v)
        {
            if (v >= 0) return v.ToString();
            // Un nombre negatif ecrit tel quel serait recompile en "-(n)" : on l'ecrit en hexadecimal
            const string chiffres = "0123456789ABCDEF";
            uint u = (uint)v;
            string h = "";
            for (int i = 0; i < 8; i++) { h = chiffres[(int)(u & 15)] + h; u >>= 4; }
            return "0x" + h;
        }

        private static string Symbole(int op)
        {
            switch (op)
            {
                case Op.Add: return "+";
                case Op.Sub: return "-";
                case Op.Mul: return "*";
                case Op.Div: return "/";
                case Op.Mod: return "%";
                case Op.Eq: return "==";
                case Op.Ne: return "!=";
                case Op.Lt: return "<";
                case Op.Gt: return ">";
                case Op.Le: return "<=";
                case Op.Ge: return ">=";
                case Op.And: return "&&";
                case Op.Or: return "||";
            }
            return null;
        }

        /// <summary>Priorite d'un operateur binaire (plus grand = plus lie) ; 0 si ce n'en est pas un.</summary>
        private static int Prio(int op)
        {
            switch (op)
            {
                case Op.Or: return 1;
                case Op.And: return 2;
                case Op.Eq: case Op.Ne: return 3;
                case Op.Lt: case Op.Gt: case Op.Le: case Op.Ge: return 4;
                case Op.Add: case Op.Sub: return 5;
                case Op.Mul: case Op.Div: case Op.Mod: return 6;
            }
            return 0;
        }

        private const int PrioUnaire = 7, PrioSimple = 8;

        // La pile du decompilateur : chaque expression garde sa priorite, pour ne mettre des
        // parentheses que la ou elles changent le sens (a + b * c, mais (a + b) * c).
        private readonly List<string> pile = new List<string>();
        private readonly List<int> prios = new List<int>();

        private void Empiler(string s, int pr) { pile.Add(s); prios.Add(pr); }

        private string Depiler(out int pr)
        {
            if (pile.Count == 0) throw new Exception("bytecode inattendu (pile vide)");
            int k = pile.Count - 1;
            string s = pile[k];
            pr = prios[k];
            pile.RemoveAt(k);
            prios.RemoveAt(k);
            return s;
        }

        private string Depiler() => Depiler(out int _);

        private string Arguments(int n)
        {
            string[] a = new string[n];
            for (int k = n - 1; k >= 0; k--) a[k] = Depiler();
            string r = "";
            for (int k = 0; k < n; k++) r += (k > 0 ? ", " : "") + a[k];
            return r;
        }

        private static string Booleen(string v) => v == "1" ? "vrai" : (v == "0" ? "faux" : v);

        private FonctionFsl FonctionEn(int entree)
        {
            for (int i = 0; i < p.Fonctions.Count; i++) if (p.Fonctions[i].Entree == entree) return p.Fonctions[i];
            return null;
        }

        // ---- Coeur : decompile les instructions de [debut, fin) ----
        private void Bloc(int debut, int fin, int indent, int finBoucle)
        {
            int base0 = pile.Count;                     // les blocs imbriques partagent la pile
            int pc = debut, debutInstr = debut;
            while (pc < fin)
            {
                int op = c[pc];
                string sym = Symbole(op);
                if (sym != null)
                {
                    int P = Prio(op);
                    string b = Depiler(out int pb), a = Depiler(out int pa);
                    if (pa < P) a = "(" + a + ")";
                    if (pb <= P) b = "(" + b + ")";       // a - (b - c) : la droite garde ses parentheses
                    Empiler(a + " " + sym + " " + b, P);
                    pc++;
                    continue;
                }
                switch (op)
                {
                    case Op.PushInt: Empiler(Nombre(c[pc + 1]), PrioSimple); pc += 2; break;
                    case Op.PushStr: Empiler(Texte(p.Strings[c[pc + 1]]), PrioSimple); pc += 2; break;
                    case Op.PushNull: Empiler("0/nul", PrioSimple); pc++; break;
                    case Op.Load: Empiler(p.VarNames[c[pc + 1]], PrioSimple); pc += 2; break;
                    case Op.LoadL: Empiler(fn.Locales[c[pc + 1]], PrioSimple); pc += 2; break;
                    case Op.Not:
                    case Op.Neg:
                        {
                            string a = Depiler(out int pa);
                            Empiler((op == Op.Not ? "!" : "-") + (pa >= PrioUnaire ? a : "(" + a + ")"), PrioUnaire);
                            pc++;
                            break;
                        }

                    case Op.Appel:
                        {
                            int f = c[pc + 1], n = c[pc + 2];
                            Empiler(Natives.Noms[f] + "(" + Arguments(n) + ")", PrioSimple);
                            pc += 3;
                            break;
                        }
                    case Op.AppelF:
                        {
                            int f = c[pc + 1], n = c[pc + 2];
                            Empiler(p.Fonctions[f].Nom + "(" + Arguments(n) + ")", PrioSimple);
                            pc += 3;
                            break;
                        }

                    case Op.Store:
                    case Op.StoreL:
                        {
                            int s = c[pc + 1];
                            string v = Depiler();
                            bool[] decl = op == Op.Store ? globDeclaree : locDeclaree;
                            string nom = op == Op.Store ? p.VarNames[s] : fn.Locales[s];
                            int type = op == Op.Store ? p.VarTypes[s] : fn.Types[s];
                            if (type == FslProgram.TypeBool) v = Booleen(v);
                            if (!decl[s])
                            {
                                decl[s] = true;
                                Ligne(indent, FslProgram.NomType(type) + " " + nom + (v == "0/nul" ? ";" : " = " + v + ";"));
                            }
                            else Ligne(indent, nom + " = " + v + ";");
                            pc += 2;
                            debutInstr = pc;
                            break;
                        }

                    case Op.Print: Ligne(indent, "afficher(" + Depiler() + ");"); pc++; debutInstr = pc; break;
                    case Op.Pop: Ligne(indent, Depiler() + ";"); pc++; debutInstr = pc; break;
                    case Op.Retour:
                        {
                            string v = Depiler();
                            if (fn != null && fn.Retour == FslProgram.TypeBool) v = Booleen(v);
                            if (fn != null && fn.Retour == FslProgram.TypeRien && v == "0/nul") Ligne(indent, "retourner;");
                            else Ligne(indent, "retourner " + v + ";");
                            pc++;
                            debutInstr = pc;
                            break;
                        }

                    case Op.Jz:
                        {
                            int t = c[pc + 1];
                            string cond = Depiler();
                            bool sautAvantT = t - 2 >= pc + 2 && c[t - 2] == Op.Jmp;
                            if (sautAvantT && c[t - 1] == debutInstr)
                            {
                                // tantque : la fin du corps revient au debut de la condition
                                Ligne(indent, "tantque (" + cond + ") {");
                                Bloc(pc + 2, t - 2, indent + 1, t);
                                Ligne(indent, "}");
                                pc = t;
                            }
                            else if (sautAvantT && c[t - 1] > t && c[t - 1] <= fin)
                            {
                                // si / sinon : la fin du "si" saute par-dessus le "sinon"
                                int e = c[t - 1];
                                Ligne(indent, "si (" + cond + ") {");
                                Bloc(pc + 2, t - 2, indent + 1, finBoucle);
                                Ligne(indent, "} sinon {");
                                Bloc(t, e, indent + 1, finBoucle);
                                Ligne(indent, "}");
                                pc = e;
                            }
                            else
                            {
                                Ligne(indent, "si (" + cond + ") {");
                                Bloc(pc + 2, t, indent + 1, finBoucle);
                                Ligne(indent, "}");
                                pc = t;
                            }
                            debutInstr = pc;
                            break;
                        }

                    case Op.Jmp:
                        {
                            int x = c[pc + 1];
                            FonctionFsl f = fn == null ? FonctionEn(pc + 2) : null;
                            if (f != null && x == f.Fin)
                            {
                                Fonction(f, indent);
                                pc = x;
                            }
                            else if (x == finBoucle)
                            {
                                Ligne(indent, "sortir;");
                                pc += 2;
                            }
                            else throw new Exception("saut inattendu a la position " + pc);
                            debutInstr = pc;
                            break;
                        }

                    case Op.Halt: Ligne(indent, "// fin du programme"); pc++; debutInstr = pc; break;
                    default: throw new Exception("instruction inconnue " + op + " a la position " + pc);
                }
            }
            if (pile.Count > base0) throw new Exception("bytecode inattendu (valeurs restees sur la pile)");
        }

        private void Fonction(FonctionFsl f, int indent)
        {
            string ps = "";
            for (int k = 0; k < f.NbParams; k++)
                ps += (k > 0 ? ", " : "") + FslProgram.NomType(f.Types[k]) + " " + f.Locales[k];
            if (lignes.Count > 0 && lignes[lignes.Count - 1].Length > 0) lignes.Add("");
            Ligne(indent, "fonction " + FslProgram.NomType(f.Retour) + " " + f.Nom + "(" + ps + ") {");
            FonctionFsl avant = fn;
            bool[] avantDecl = locDeclaree;
            fn = f;
            locDeclaree = new bool[f.Locales.Count];
            for (int k = 0; k < f.NbParams; k++) locDeclaree[k] = true;
            Bloc(f.Entree, f.Fin - 2, indent + 1, -1);      // sans le "retourner 0/nul" ajoute par le compilateur
            fn = avant;
            locDeclaree = avantDecl;
            Ligne(indent, "}");
            lignes.Add("");
        }
    }
}
