using FileSystemOS.Graphique;

namespace FileSystemOS.Fsl
{
    /// <summary>Valeur FSL : nulle (0/nul), entier ou texte.</summary>
    public struct Value
    {
        public const int KNull = 0, KInt = 1, KText = 2;

        public int Kind;
        public int I;
        public string S;

        public static Value Int(int v) { Value x = new Value(); x.Kind = KInt; x.I = v; return x; }
        public static Value Text(string s) { Value x = new Value(); x.Kind = KText; x.S = s; return x; }
    }

    /// <summary>
    /// Machine virtuelle a pile. Run(budget) execute au plus 'budget' instructions
    /// puis rend la main : un programme ne peut donc jamais bloquer l'OS.
    /// </summary>
    public class Vm
    {
        private readonly FslProgram p;
        private readonly int[] code;
        private readonly int[] lines;
        private readonly Value[] stack = new Value[1024];
        private readonly Value[] vars;
        private readonly OutputWindow output;
        private int sp, pc;

        // Appels de fonctions : variables locales de tous les appels en cours, et retours
        private const int MaxAppels = 256;
        private readonly Value[] locales = new Value[8192];
        private int fp, sommet, fonction = -1, prof;
        private readonly int[] rPc = new int[MaxAppels], rFp = new int[MaxAppels], rSommet = new int[MaxAppels], rFn = new int[MaxAppels];

        public bool Done;
        public string Error;
        public uint Steps;

        /// <summary>Fenetres, evenements et reveil du programme (fonctions integrees).</summary>
        public readonly FslContexte Ctx = new FslContexte();

        public Vm(FslProgram prog, OutputWindow outWin)
        {
            p = prog;
            code = prog.Code.ToArray();
            lines = prog.Lines.ToArray();
            vars = new Value[prog.VarNames.Count]; // toutes les variables commencent a 0/nul
            output = outWin;
        }

        private void Push(Value v)
        {
            if (sp >= stack.Length) { Fail("pile pleine"); return; }
            stack[sp++] = v;
        }

        private Value Pop()
        {
            if (sp <= 0) { Fail("pile vide"); return new Value(); }
            return stack[--sp];
        }

        private void Fail(string msg)
        {
            if (Done) return;
            int ip = pc - 1 < 0 ? 0 : pc - 1;
            int line = ip < lines.Length ? lines[ip] : 0;
            Error = "Ligne " + line + " : " + msg;
            Done = true;
        }

        public static string ToText(Value v)
        {
            if (v.Kind == Value.KNull) return "0/nul";
            if (v.Kind == Value.KInt) return v.I.ToString();
            return v.S;
        }

        /// <summary>Une valeur peut-elle aller dans une variable de ce type ? (0/nul va partout)</summary>
        private static bool Compatible(Value v, int type)
        {
            if (v.Kind == Value.KNull) return true;
            if (type == FslProgram.TypeTexte) return v.Kind == Value.KText;
            return v.Kind == Value.KInt;                          // entier et booleen
        }

        private static Value Normaliser(Value v, int type) =>
            type == FslProgram.TypeBool && v.Kind == Value.KInt ? Value.Int(v.I != 0 ? 1 : 0) : v;

        private static bool Truthy(Value v)
        {
            if (v.Kind == Value.KInt) return v.I != 0;
            if (v.Kind == Value.KText) return v.S.Length > 0;
            return false;
        }

        private static bool Equal(Value a, Value b)
        {
            if (a.Kind != b.Kind) return false;
            if (a.Kind == Value.KNull) return true;
            if (a.Kind == Value.KInt) return a.I == b.I;
            return a.S == b.S;
        }

        private bool Ints(Value a, Value b)
        {
            if (a.Kind == Value.KNull || b.Kind == Value.KNull)
            {
                Fail("calcul avec une variable nulle (0/nul)");
                return false;
            }
            if (a.Kind != Value.KInt || b.Kind != Value.KInt)
            {
                Fail("nombres attendus");
                return false;
            }
            return true;
        }

        public void Run(int budget)
        {
            while (budget-- > 0 && !Done)
            {
                if (pc >= code.Length) { Done = true; break; }
                int op = code[pc++];
                Steps++;
                Value a, b;

                switch (op)
                {
                    case Op.Halt: Done = true; break;
                    case Op.PushInt: Push(Value.Int(code[pc++])); break;
                    case Op.PushStr: Push(Value.Text(p.Strings[code[pc++]])); break;
                    case Op.PushNull: Push(new Value()); break;
                    case Op.Load: Push(vars[code[pc++]]); break;

                    case Op.Store:
                        {
                            int slot = code[pc++];
                            Value v = Pop();
                            if (!Compatible(v, p.VarTypes[slot]))
                            {
                                Fail("type incompatible pour '" + p.VarNames[slot] + "' (" + FslProgram.NomType(p.VarTypes[slot]) + " attendu)");
                                break;
                            }
                            vars[slot] = Normaliser(v, p.VarTypes[slot]);
                            break;
                        }

                    case Op.LoadL: Push(locales[fp + code[pc++]]); break;

                    case Op.StoreL:
                        {
                            int slot = code[pc++];
                            Value v = Pop();
                            if (fonction < 0) { Fail("variable locale hors d'une fonction"); break; }
                            FonctionFsl f = p.Fonctions[fonction];
                            if (!Compatible(v, f.Types[slot]))
                            {
                                Fail("type incompatible pour '" + f.Locales[slot] + "' (" + FslProgram.NomType(f.Types[slot]) + " attendu)");
                                break;
                            }
                            locales[fp + slot] = Normaliser(v, f.Types[slot]);
                            break;
                        }

                    case Op.AppelF:
                        {
                            int fi = code[pc++], n = code[pc++];
                            FonctionFsl nf = p.Fonctions[fi];
                            if (prof >= MaxAppels) { Fail("plus de " + MaxAppels + " appels imbriques (une fonction s'appelle-t-elle sans fin ?)"); break; }
                            int b0 = sommet;
                            if (b0 + nf.Locales.Count > locales.Length) { Fail("plus de place pour les variables locales"); break; }
                            for (int k = 0; k < nf.Locales.Count; k++) locales[b0 + k] = new Value();
                            for (int k = n - 1; k >= 0; k--)
                            {
                                Value v = Pop();
                                if (!Compatible(v, nf.Types[k]))
                                {
                                    Fail(nf.Nom + " : le parametre '" + nf.Locales[k] + "' doit etre " + FslProgram.NomType(nf.Types[k]));
                                    break;
                                }
                                locales[b0 + k] = Normaliser(v, nf.Types[k]);
                            }
                            if (Done) break;
                            rPc[prof] = pc; rFp[prof] = fp; rSommet[prof] = sommet; rFn[prof] = fonction;
                            prof++;
                            fp = b0;
                            sommet = b0 + nf.Locales.Count;
                            fonction = fi;
                            pc = nf.Entree;
                            break;
                        }

                    case Op.Retour:
                        {
                            Value v = Pop();
                            if (prof == 0 || fonction < 0) { Fail("'retourner' en dehors d'une fonction"); break; }
                            FonctionFsl cf = p.Fonctions[fonction];
                            if (cf.Retour != FslProgram.TypeRien && !Compatible(v, cf.Retour))
                            {
                                Fail(cf.Nom + " doit renvoyer " + FslProgram.NomType(cf.Retour));
                                break;
                            }
                            prof--;
                            pc = rPc[prof]; fp = rFp[prof]; sommet = rSommet[prof]; fonction = rFn[prof];
                            Push(cf.Retour == FslProgram.TypeRien ? new Value() : Normaliser(v, cf.Retour));
                            break;
                        }

                    case Op.Add:
                        b = Pop(); a = Pop();
                        if (a.Kind == Value.KText || b.Kind == Value.KText) Push(Value.Text(ToText(a) + ToText(b)));
                        else if (Ints(a, b)) Push(Value.Int(a.I + b.I));
                        break;
                    case Op.Sub: b = Pop(); a = Pop(); if (Ints(a, b)) Push(Value.Int(a.I - b.I)); break;
                    case Op.Mul: b = Pop(); a = Pop(); if (Ints(a, b)) Push(Value.Int(a.I * b.I)); break;
                    case Op.Div:
                        b = Pop(); a = Pop();
                        if (Ints(a, b)) { if (b.I == 0) Fail("division par zero"); else Push(Value.Int(a.I / b.I)); }
                        break;
                    case Op.Mod:
                        b = Pop(); a = Pop();
                        if (Ints(a, b)) { if (b.I == 0) Fail("division par zero"); else Push(Value.Int(a.I % b.I)); }
                        break;

                    case Op.Eq: b = Pop(); a = Pop(); Push(Value.Int(Equal(a, b) ? 1 : 0)); break;
                    case Op.Ne: b = Pop(); a = Pop(); Push(Value.Int(Equal(a, b) ? 0 : 1)); break;
                    case Op.Lt: b = Pop(); a = Pop(); if (Ints(a, b)) Push(Value.Int(a.I < b.I ? 1 : 0)); break;
                    case Op.Gt: b = Pop(); a = Pop(); if (Ints(a, b)) Push(Value.Int(a.I > b.I ? 1 : 0)); break;
                    case Op.Le: b = Pop(); a = Pop(); if (Ints(a, b)) Push(Value.Int(a.I <= b.I ? 1 : 0)); break;
                    case Op.Ge: b = Pop(); a = Pop(); if (Ints(a, b)) Push(Value.Int(a.I >= b.I ? 1 : 0)); break;

                    case Op.And: b = Pop(); a = Pop(); Push(Value.Int(Truthy(a) && Truthy(b) ? 1 : 0)); break;
                    case Op.Or: b = Pop(); a = Pop(); Push(Value.Int(Truthy(a) || Truthy(b) ? 1 : 0)); break;
                    case Op.Not: a = Pop(); Push(Value.Int(Truthy(a) ? 0 : 1)); break;
                    case Op.Neg:
                        a = Pop();
                        if (a.Kind != Value.KInt) Fail("nombre attendu apres '-'");
                        else Push(Value.Int(-a.I));
                        break;

                    case Op.Jmp: pc = code[pc]; break;
                    case Op.Jz:
                        {
                            int target = code[pc++];
                            if (!Truthy(Pop())) pc = target;
                            break;
                        }

                    case Op.Print: output.Print(ToText(Pop())); break;

                    case Op.Pop: Pop(); break;

                    case Op.Appel:
                        {
                            int f = code[pc++], n = code[pc++];
                            if (f < 0 || f >= Natives.Noms.Length || n < 0 || n > 8 || sp < n) { Fail("appel de fonction invalide"); break; }
                            Value[] args = new Value[n];
                            for (int k = n - 1; k >= 0; k--) args[k] = Pop();
                            try { Push(Natives.Appeler(f, args, Ctx)); }
                            catch (System.Exception e) { Fail(Natives.Noms[f] + " : " + e.Message); }
                            // attendre() ou evenement() sans evenement : on cede la main tout de suite
                            if (Ctx.ReveilA != 0) return;
                            break;
                        }

                    default: Fail("instruction inconnue " + op); break;
                }
            }
        }
    }
}
