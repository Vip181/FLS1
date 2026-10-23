using System;
using System.Collections.Generic;

namespace FileSystemOS.Fsl
{
    /// <summary>
    /// COMPILATEUR du File Syst Langage (version 2) : analyse descendante recursive qui produit
    /// directement le bytecode. Une premiere passe releve les en-tetes de fonctions (on peut donc
    /// appeler une fonction ecrite plus bas), la seconde compile tout.
    ///
    ///   Types       : entier (ou int), texte (ou chaine), booleen ; "rien" pour une fonction
    ///   Instructions: si / sinon, tantque, pour (init; condition; pas), sortir, retourner, afficher
    ///   Fonctions   : fonction entier carre(entier x) { retourner x * x; }
    ///                 variables locales, parametres, recursivite (256 appels imbriques au plus)
    ///   Valeurs     : 42, 0x2A, "texte", vrai, faux, 0/nul
    /// </summary>
    public class Compiler
    {
        private List<Token> toks;
        private int pos;
        private FslProgram p;
        private FonctionFsl courante;                                   // null : programme principal
        private readonly List<List<int>> boucles = new List<List<int>>(); // sauts "sortir" a corriger

        // Recherche rapide par nom : des milliers de variables et de fonctions restent instantanees
        private readonly TableNoms globales = new TableNoms(), fonctions = new TableNoms();

        /// <summary>
        /// Table de hachage maison nom -> numero (Dictionary&lt;string, ...&gt; n'est pas fiable sous
        /// Cosmos, comme List.IndexOf). 512 cases, empreinte FNV-1a, comparaison par contenu.
        /// </summary>
        private class TableNoms
        {
            private readonly List<string>[] cles = new List<string>[512];
            private readonly List<int>[] vals = new List<int>[512];

            private static int Case(string s)
            {
                uint h = 2166136261;
                for (int i = 0; i < s.Length; i++) { h ^= s[i]; h *= 16777619; }
                return (int)(h & 511);
            }

            public void Ajouter(string nom, int v)
            {
                int b = Case(nom);
                if (cles[b] == null) { cles[b] = new List<string>(); vals[b] = new List<int>(); }
                cles[b].Add(nom);
                vals[b].Add(v);
            }

            public int Trouver(string nom)
            {
                int b = Case(nom);
                List<string> l = cles[b];
                if (l == null) return -1;
                for (int i = 0; i < l.Count; i++) if (l[i] == nom) return vals[b][i];
                return -1;
            }
        }

        public static FslProgram Compile(string source)
        {
            var c = new Compiler();
            c.toks = Lexer.Run(source);
            c.p = new FslProgram();
            c.RelevePrototypes();
            c.pos = 0;
            while (c.Peek().Kind != TokKind.End)
            {
                if (c.IsWord("fonction")) c.DefinitionFonction();
                else c.Statement();
            }
            c.p.Emit(Op.Halt, c.Peek().Line);
            return c.p;
        }

        // ---- Outils ----
        private Token Peek() => toks[pos];
        private Token Next() => toks[pos++];
        private int Line => Peek().Line;
        private bool IsSym(string s) => Peek().Kind == TokKind.Symbol && Peek().Text == s;
        private bool IsWord(string s) => Peek().Kind == TokKind.Ident && Peek().Text == s;

        private void Err(string msg)
        {
            throw new Exception("Ligne " + Line + " : " + msg + " (trouve '" + Peek().Text + "')");
        }

        private void Expect(string s)
        {
            if (!IsSym(s)) Err("'" + s + "' attendu");
            pos++;
        }

        private static int TypeDe(string w)
        {
            if (w == "int" || w == "entier") return FslProgram.TypeInt;
            if (w == "texte" || w == "chaine") return FslProgram.TypeTexte;
            if (w == "booleen") return FslProgram.TypeBool;
            if (w == "rien") return FslProgram.TypeRien;
            return -1;
        }

        private int Globale(string name) => globales.Trouver(name);

        private int Locale(string name)
        {
            if (courante == null) return -1;
            for (int i = 0; i < courante.Locales.Count; i++) if (courante.Locales[i] == name) return i;
            return -1;
        }

        private string Nom()
        {
            if (Peek().Kind != TokKind.Ident || Lexer.IsKeyword(Peek().Text)) Err("nom attendu");
            return Next().Text;
        }

        // ---- Premiere passe : en-tetes des fonctions ----
        private void RelevePrototypes()
        {
            int profondeur = 0;
            pos = 0;
            while (Peek().Kind != TokKind.End)
            {
                if (IsSym("{")) { profondeur++; pos++; continue; }
                if (IsSym("}")) { profondeur--; pos++; continue; }
                if (profondeur == 0 && IsWord("fonction"))
                {
                    int l = Line;
                    pos++;
                    int r = TypeDe(Peek().Text);
                    if (r < 0) Err("type de retour attendu (entier, texte, booleen ou rien)");
                    pos++;
                    var f = new FonctionFsl { Retour = r };
                    f.Nom = Nom();
                    if (fonctions.Trouver(f.Nom) >= 0) throw new Exception("Ligne " + l + " : fonction '" + f.Nom + "' deja definie");
                    if (Natives.Trouver(f.Nom) >= 0) throw new Exception("Ligne " + l + " : '" + f.Nom + "' est deja une fonction integree");
                    Expect("(");
                    if (!IsSym(")"))
                        while (true)
                        {
                            int t = TypeDe(Peek().Text);
                            if (t <= 0) Err("type de parametre attendu");
                            pos++;
                            string n = Nom();
                            for (int k = 0; k < f.Locales.Count; k++)
                                if (f.Locales[k] == n) throw new Exception("Ligne " + l + " : parametre '" + n + "' en double");
                            f.Locales.Add(n);
                            f.Types.Add(t);
                            if (IsSym(",")) { pos++; continue; }
                            break;
                        }
                    Expect(")");
                    f.NbParams = f.Locales.Count;
                    f.Entree = -1;
                    fonctions.Ajouter(f.Nom, p.Fonctions.Count);
                    p.Fonctions.Add(f);
                    continue;
                }
                pos++;
            }
        }

        // fonction entier carre(entier x) { ... }   (le corps est saute par le programme principal)
        private void DefinitionFonction()
        {
            int l = Line;
            if (courante != null || boucles.Count > 0) Err("une fonction se declare en dehors des blocs");
            pos++;                       // fonction
            pos++;                       // type de retour
            int fi = fonctions.Trouver(Nom());
            if (fi < 0) Err("fonction mal ecrite");
            FonctionFsl f = p.Fonctions[fi];
            Expect("(");
            while (!IsSym(")")) pos++;   // parametres : deja releves
            pos++;

            p.Emit(Op.Jmp, l);
            int saut = p.Emit(0, l);
            f.Entree = p.Code.Count;
            courante = f;
            Block();
            p.Emit(Op.PushNull, Line);   // fin sans "retourner" : la fonction renvoie 0/nul
            p.Emit(Op.Retour, Line);
            f.Fin = p.Code.Count;
            courante = null;
            p.Code[saut] = p.Code.Count;
        }

        // ---- Instructions ----
        private void Statement()
        {
            int t = Peek().Kind == TokKind.Ident ? TypeDe(Peek().Text) : -1;
            if (t > 0) Declaration();
            else if (IsWord("afficher"))
            {
                int l = Line;
                pos++;
                Expect("(");
                Expr();
                Expect(")");
                Expect(";");
                p.Emit(Op.Print, l);
            }
            else if (IsWord("si")) If();
            else if (IsWord("tantque")) While();
            else if (IsWord("pour")) Pour();
            else if (IsWord("sortir"))
            {
                int l = Line;
                if (boucles.Count == 0) Err("'sortir' en dehors d'une boucle");
                pos++;
                p.Emit(Op.Jmp, l);
                boucles[boucles.Count - 1].Add(p.Emit(0, l));
                Expect(";");
            }
            else if (IsWord("retourner"))
            {
                int l = Line;
                if (courante == null) Err("'retourner' en dehors d'une fonction");
                pos++;
                if (IsSym(";")) p.Emit(Op.PushNull, l); else Expr();
                Expect(";");
                p.Emit(Op.Retour, l);
            }
            else if (IsWord("fonction")) Err("une fonction se declare en dehors des blocs");
            else if (IsSym("{")) Block();
            else if (Peek().Kind == TokKind.Ident && EstAppel())
            {
                int l = Line;
                Appel();
                p.Emit(Op.Pop, l);            // resultat inutilise
                Expect(";");
            }
            else if (Peek().Kind == TokKind.Ident) { Affectation(); Expect(";"); }
            else Err("instruction attendue");
        }

        // entier x = 5;   texte t = "a";   booleen b = vrai;   entier z;  (z vaut 0/nul)
        private void Declaration()
        {
            int l = Line;
            int type = TypeDe(Next().Text);
            string name = Nom();
            int slot;
            if (courante != null)
            {
                if (Locale(name) >= 0) throw new Exception("Ligne " + l + " : variable '" + name + "' deja declaree dans cette fonction");
                slot = courante.Locales.Count;
                courante.Locales.Add(name);
                courante.Types.Add(type);
            }
            else
            {
                if (Globale(name) >= 0) throw new Exception("Ligne " + l + " : variable '" + name + "' deja declaree");
                slot = p.VarNames.Count;
                p.VarNames.Add(name);
                p.VarTypes.Add(type);
                globales.Ajouter(name, slot);
            }

            if (IsSym("=")) { pos++; Expr(); }
            else p.Emit(Op.PushNull, l);
            p.Emit(courante != null ? Op.StoreL : Op.Store, l);
            p.Emit(slot, l);
            Expect(";");
        }

        /// <summary>nom = valeur   (sans le point-virgule : sert aussi au "pas" d'une boucle pour)</summary>
        private void Affectation()
        {
            int l = Line;
            string name = Next().Text;
            int loc = Locale(name), slot = loc >= 0 ? loc : Globale(name);
            if (slot < 0) throw new Exception("Ligne " + l + " : variable inconnue '" + name + "'");
            Expect("=");
            Expr();
            p.Emit(loc >= 0 ? Op.StoreL : Op.Store, l);
            p.Emit(slot, l);
        }

        private void If()
        {
            int l = Line;
            pos++;
            Expect("(");
            Expr();
            Expect(")");
            p.Emit(Op.Jz, l);
            int jz = p.Emit(0, l);
            Block();

            if (IsWord("sinon"))
            {
                pos++;
                p.Emit(Op.Jmp, l);
                int jmp = p.Emit(0, l);
                int apres = p.Code.Count;
                p.Code[jz] = apres;
                if (IsWord("si")) If(); else Block();
                if (p.Code.Count == apres)
                {
                    // "sinon { }" vide : on retire le saut. Sinon ce binaire serait identique a celui
                    // d'un "si" se terminant par un autre "si ... sinon { }", et le decompilateur
                    // ne pourrait pas choisir (cas trouve par le test aleatoire).
                    p.Code.RemoveAt(p.Code.Count - 1); p.Code.RemoveAt(p.Code.Count - 1);
                    p.Lines.RemoveAt(p.Lines.Count - 1); p.Lines.RemoveAt(p.Lines.Count - 1);
                    p.Code[jz] = p.Code.Count;
                }
                else p.Code[jmp] = p.Code.Count;
            }
            else p.Code[jz] = p.Code.Count;
        }

        private void While()
        {
            int l = Line;
            pos++;
            int start = p.Code.Count;
            Expect("(");
            Expr();
            Expect(")");
            p.Emit(Op.Jz, l);
            int jz = p.Emit(0, l);
            boucles.Add(new List<int>());
            Block();
            p.Emit(Op.Jmp, l);
            p.Emit(start, l);
            FinBoucle(jz);
        }

        // pour (entier i = 0; i < 10; i = i + 1) { ... }  ==  init; tantque (cond) { ...; pas; }
        private void Pour()
        {
            int l = Line;
            pos++;
            Expect("(");
            if (Peek().Kind == TokKind.Ident && TypeDe(Peek().Text) > 0)
            {
                // Comme en C : plusieurs boucles "pour (entier i = 0; ...)" a la suite. Si i existe deja
                // avec ce type, la boucle le reutilise au lieu de refuser une seconde declaration.
                int t = TypeDe(Peek().Text);
                string n = pos + 1 < toks.Count ? toks[pos + 1].Text : "";
                int existe = courante != null ? Locale(n) : Globale(n);
                int typeExistant = existe < 0 ? -1 : (courante != null ? courante.Types[existe] : p.VarTypes[existe]);
                if (existe >= 0 && typeExistant == t) { pos++; Affectation(); Expect(";"); }
                else Declaration();
            }
            else { Affectation(); Expect(";"); }

            int start = p.Code.Count;
            Expr();
            Expect(";");
            p.Emit(Op.Jz, l);
            int jz = p.Emit(0, l);

            // Le "pas" s'ecrit avant le corps mais s'execute apres : on le retient, on y revient
            int debutPas = pos, prof = 0;
            while (!(IsSym(")") && prof == 0))
            {
                if (Peek().Kind == TokKind.End) Err("')' manquant dans la boucle pour");
                if (IsSym("(")) prof++;
                if (IsSym(")")) prof--;
                pos++;
            }
            int finPas = pos;
            pos++;

            boucles.Add(new List<int>());
            Block();
            int apres = pos;
            pos = debutPas;
            Affectation();
            if (pos != finPas) Err("le pas d'une boucle pour doit etre une affectation (i = i + 1)");
            pos = apres;
            p.Emit(Op.Jmp, l);
            p.Emit(start, l);
            FinBoucle(jz);
        }

        private void FinBoucle(int jz)
        {
            p.Code[jz] = p.Code.Count;
            List<int> sorties = boucles[boucles.Count - 1];
            for (int i = 0; i < sorties.Count; i++) p.Code[sorties[i]] = p.Code.Count;
            boucles.RemoveAt(boucles.Count - 1);
        }

        private void Block()
        {
            Expect("{");
            while (!IsSym("}"))
            {
                if (Peek().Kind == TokKind.End) Err("'}' manquant");
                Statement();
            }
            pos++;
        }

        // ---- Appels : fonctions du programme, puis fonctions integrees ----
        private bool EstAppel() =>
            pos + 1 < toks.Count && toks[pos + 1].Kind == TokKind.Symbol && toks[pos + 1].Text == "(";

        private void Appel()
        {
            int l = Line;
            string nom = Next().Text;
            int uf = fonctions.Trouver(nom);
            int f = uf >= 0 ? -1 : Natives.Trouver(nom);
            if (uf < 0 && f < 0) throw new Exception("Ligne " + l + " : fonction inconnue '" + nom + "' (tapez 'fonctions')");
            Expect("(");
            int n = 0;
            if (!IsSym(")"))
            {
                Expr(); n++;
                while (IsSym(",")) { pos++; Expr(); n++; }
            }
            Expect(")");
            if (uf >= 0)
            {
                if (n != p.Fonctions[uf].NbParams)
                    throw new Exception("Ligne " + l + " : " + nom + " attend " + p.Fonctions[uf].NbParams + " argument(s), pas " + n);
                p.Emit(Op.AppelF, l);
                p.Emit(uf, l);
                p.Emit(n, l);
                return;
            }
            if (n != Natives.NbArgs[f])
                throw new Exception("Ligne " + l + " : " + nom + " attend " + Natives.NbArgs[f] + " argument(s), pas " + n +
                                    " - " + Natives.Aides[f]);
            p.Emit(Op.Appel, l);
            p.Emit(f, l);
            p.Emit(n, l);
        }

        // ---- Expressions (de la priorite la plus faible a la plus forte) ----
        private void Expr() => Or();

        private void Or()
        {
            And();
            while (IsSym("||")) { int l = Line; pos++; And(); p.Emit(Op.Or, l); }
        }

        private void And()
        {
            Equality();
            while (IsSym("&&")) { int l = Line; pos++; Equality(); p.Emit(Op.And, l); }
        }

        private void Equality()
        {
            Comparison();
            while (IsSym("==") || IsSym("!="))
            {
                int l = Line;
                int op = Next().Text == "==" ? Op.Eq : Op.Ne;
                Comparison();
                p.Emit(op, l);
            }
        }

        private void Comparison()
        {
            Additive();
            while (IsSym("<") || IsSym(">") || IsSym("<=") || IsSym(">="))
            {
                int l = Line;
                string s = Next().Text;
                int op = s == "<" ? Op.Lt : (s == ">" ? Op.Gt : (s == "<=" ? Op.Le : Op.Ge));
                Additive();
                p.Emit(op, l);
            }
        }

        private void Additive()
        {
            Term();
            while (IsSym("+") || IsSym("-"))
            {
                int l = Line;
                int op = Next().Text == "+" ? Op.Add : Op.Sub;
                Term();
                p.Emit(op, l);
            }
        }

        private void Term()
        {
            Unary();
            while (IsSym("*") || IsSym("/") || IsSym("%"))
            {
                int l = Line;
                string s = Next().Text;
                int op = s == "*" ? Op.Mul : (s == "/" ? Op.Div : Op.Mod);
                Unary();
                p.Emit(op, l);
            }
        }

        private void Unary()
        {
            if (IsSym("-")) { int l = Line; pos++; Unary(); p.Emit(Op.Neg, l); }
            else if (IsSym("!")) { int l = Line; pos++; Unary(); p.Emit(Op.Not, l); }
            else Primary();
        }

        private void Primary()
        {
            Token t = Peek();
            int l = t.Line;

            if (t.Kind == TokKind.Number) { pos++; p.Emit(Op.PushInt, l); p.Emit(t.Value, l); }
            else if (t.Kind == TokKind.String)
            {
                pos++;
                p.Strings.Add(t.Text);
                p.Emit(Op.PushStr, l);
                p.Emit(p.Strings.Count - 1, l);
            }
            else if (t.Kind == TokKind.Null) { pos++; p.Emit(Op.PushNull, l); }
            else if (t.Kind == TokKind.Ident && (t.Text == "vrai" || t.Text == "faux"))
            {
                pos++;
                p.Emit(Op.PushInt, l);
                p.Emit(t.Text == "vrai" ? 1 : 0, l);
            }
            else if (t.Kind == TokKind.Ident && !Lexer.IsKeyword(t.Text) && EstAppel()) Appel();
            else if (t.Kind == TokKind.Ident && !Lexer.IsKeyword(t.Text))
            {
                pos++;
                int loc = Locale(t.Text);
                if (loc >= 0) { p.Emit(Op.LoadL, l); p.Emit(loc, l); return; }
                int slot = Globale(t.Text);
                if (slot < 0) throw new Exception("Ligne " + l + " : variable inconnue '" + t.Text + "'");
                p.Emit(Op.Load, l);
                p.Emit(slot, l);
            }
            else if (IsSym("("))
            {
                pos++;
                Expr();
                Expect(")");
            }
            else Err("valeur attendue");
        }
    }
}
