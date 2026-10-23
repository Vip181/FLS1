using System;
using FileSystemOS.Fsl;
using FileSystemOS.Utils;

namespace FileSystemOS.Systeme
{
    /// <summary>
    /// GUIDE DU FILE SYST LANGAGE, en PDF (embarque deja fabrique : GuidePdf), ecrit dans
    /// \FSOS\Documents\Guide-FSL.pdf : il s'ouvre dans le Lecteur PDF, et sous Windows depuis la
    /// partition FSOS. Commande "guide" de la Console : le refait et l'ouvre.
    /// Ce fichier est GENERE depuis le meme texte que l'apercu valide (13 chapitres, 2 annexes).
    /// </summary>
    public static class GuideFsl
    {
        public const string Fichier = "Guide-FSL.pdf";

        public static byte[] Fabriquer()
        {
            var p = new PdfEcrivain("Guide du File Syst Langage", "Vincent Senut");
            p.Section("Votre premier programme");
            p.Paragraphe("Le File Syst Langage (FSL) est le langage de programmation de File System OS. Un programme FSL est un simple fichier texte dont le nom se termine par .fsl. Il est compilé en un fichier binaire .fsc, puis exécuté dans son propre processus : s'il plante, le reste du système continue de fonctionner.");
            p.Code("// Mon premier programme\nafficher(\"Bonjour !\");\nafficher(2 + 3);");
            p.Liste("Dans l'Éditeur de texte : écrivez le programme, enregistrez-le avec un nom en .fsl, puis appuyez sur F5 pour le lancer.");
            p.Liste("Dans la Console : run suivi du chemin du fichier, par exemple run 1:\\FSOS\\Applications\\Bonjour.fsl.");
            p.Liste("Ce qu'affiche afficher() apparaît dans la fenêtre Sortie FSL.");
            p.Note("Chaque instruction se termine par un point-virgule. Un commentaire commence par // et va jusqu'au bout de la ligne ; un commentaire de plusieurs lignes s'écrit entre /* et */.");
            p.Section("Les variables");
            p.Paragraphe("Une variable garde une valeur. On la déclare avec son type, son nom et, en général, une valeur de départ. Ensuite, le signe = lui donne une nouvelle valeur.");
            p.Code("entier age = 12;\ntexte prenom = \"Lea\";\nbooleen content = vrai;\nentier vide;           // vaut 0/nul tant qu'on ne lui donne rien\nage = age + 1;         // nouvelle valeur : 13");
            p.Liste("entier (ou int) : un nombre entier, positif ou négatif. On peut l'écrire en hexadécimal : 0x2A vaut 42.");
            p.Liste("texte (ou chaine) : des caractères entre guillemets. Dans un texte, \\n fait un retour à la ligne, \\\" écrit un guillemet et \\\\ une barre oblique inverse.");
            p.Liste("booleen : vrai ou faux.");
            p.Liste("0/nul : la valeur « rien ». Une variable déclarée sans valeur vaut 0/nul, et un calcul avec 0/nul arrête le programme avec un message clair.");
            p.Paragraphe("Un nom de variable commence par une lettre ou _, suivie de lettres, de chiffres ou de _. Les mots du langage (si, tantque, fonction...) sont réservés : la liste complète est en annexe.");
            p.Note("Le type est vérifié pendant l'exécution : ranger un texte dans une variable entier arrête le programme avec le message « type incompatible ».");
            p.Section("Calculs et comparaisons");
            p.Paragraphe("Les opérations s'écrivent comme en mathématiques. La multiplication et la division passent avant l'addition et la soustraction ; les parenthèses changent l'ordre.");
            p.Code("entier a = 17;\nafficher(a + 3 * 2);       // 23\nafficher((a + 3) * 2);     // 40\nafficher(a / 5);           // 3 : division entiere\nafficher(a % 5);           // 2 : le reste\ntexte t = \"Score : \" + 10; // + colle les textes : \"Score : 10\"");
            p.Paragraphe("Une comparaison donne vrai ou faux : == (égal), != (différent), <, >, <= et >=. On les combine avec && (et), || (ou) et ! (non).");
            p.Code("entier age = 15;\nbooleen majeur = age >= 18;\nsi (age > 10 && age < 20) { afficher(\"ado\"); }\nsi (!majeur) { afficher(\"mineur\"); }");
            p.Section("Conditions : si et sinon");
            p.Paragraphe("si exécute un bloc seulement quand sa condition est vraie. sinon donne ce qu'il faut faire dans le cas contraire, et sinon si permet d'enchaîner plusieurs cas.");
            p.Code("entier note = 14;\nsi (note >= 16) {\n    afficher(\"Tres bien\");\n} sinon si (note >= 10) {\n    afficher(\"Assez bien\");\n} sinon {\n    afficher(\"A retravailler\");\n}");
            p.Section("Les boucles");
            p.Paragraphe("tantque répète un bloc tant que sa condition est vraie. pour réunit sur une seule ligne le départ, la condition et le pas ; plusieurs boucles pour peuvent réutiliser la même variable.");
            p.Code("entier i = 1;\ntantque (i <= 3) {\n    afficher(i);\n    i = i + 1;\n}\n\npour (entier k = 0; k < 5; k = k + 1) {\n    afficher(\"tour \" + vers_texte(k));\n}");
            p.Paragraphe("sortir; quitte immédiatement la boucle en cours.");
            p.Code("pour (entier n = 1; n < 100; n = n + 1) {\n    si (n * n > 50) { sortir; }\n    afficher(n);\n}");
            p.Note("Un programme qui tourne sans fin ne bloque jamais File System OS : chaque programme reçoit un peu de temps, à tour de rôle. Pour l'arrêter, utilisez le Gestionnaire des tâches, ou kill suivi de son numéro (PID) dans la Console.");
            p.Section("Les fonctions");
            p.Paragraphe("Une fonction regroupe des instructions sous un nom. On indique le type de ce qu'elle renvoie (ou rien), puis ses paramètres. retourner renvoie le résultat et quitte la fonction.");
            p.Code("fonction entier carre(entier x) {\n    retourner x * x;\n}\n\nfonction rien saluer(texte nom) {\n    afficher(\"Bonjour \" + nom);\n}\n\nafficher(carre(7));     // 49\nsaluer(\"Vincent\");");
            p.Liste("Les variables déclarées dans une fonction lui appartiennent : ce sont ses variables locales.");
            p.Liste("Une fonction peut en appeler une autre écrite plus bas dans le fichier, et peut s'appeler elle-même (récursivité, 256 appels imbriqués au plus).");
            p.Liste("Les fonctions se déclarent en dehors des blocs : jamais à l'intérieur d'un si ou d'une boucle.");
            p.Code("fonction entier factorielle(entier n) {\n    si (n <= 1) { retourner 1; }\n    retourner n * factorielle(n - 1);\n}\nafficher(factorielle(6));   // 720");
            p.Section("Fenêtres, boutons et événements");
            p.Paragraphe("Un programme peut créer ses propres fenêtres. Chaque fonction de création renvoie un numéro, que l'on garde dans une variable entier. Le programme attend ensuite les événements dans une boucle.");
            p.Code("entier f = fenetre(\"Bonjour\", 400, 220);\nentier question = etiquette(f, \"Votre prenom :\", 20, 20);\nentier nom = champ(f, 20, 44, 250);\nentier ok = bouton(f, \"Valider\", 284, 42, 100, 30);\nentier reponse = etiquette(f, \"\", 20, 100);\nbooleen fini = faux;\ntantque (!fini) {\n    texte e = evenement();\n    si (e == \"clic:\" + vers_texte(ok)) {\n        ecrire(reponse, \"Bonjour \" + lire(nom) + \" !\");\n    }\n    si (e == \"fermeture:\" + vers_texte(f)) { fini = vrai; }\n}");
            p.Paragraphe("evenement() renvoie le prochain événement, sous forme de texte :");
            p.Liste("\"clic:N\" : un clic sur le bouton numéro N.");
            p.Liste("\"valider:N\" : la touche Entrée dans le champ N.");
            p.Liste("\"touche:a\", \"touche:gauche\", \"touche:espace\"... : une touche, quand aucun champ n'a le clavier.");
            p.Liste("\"souris:N:x:y\" : un clic dans le dessin N, à la position x, y.");
            p.Liste("\"fermeture:N\" : la fenêtre N vient d'être fermée.");
            p.Liste("\"\" (texte vide) : aucun événement pour l'instant ; le programme cède alors la main un court instant.");
            p.Paragraphe("lire(element) donne le texte d'un champ, d'une zone, d'un bouton ou d'une étiquette ; ecrire(element, texte) le change ; fermer(fenetre) ferme une fenêtre. zone(...) crée une zone de texte de plusieurs lignes.");
            p.Section("Dessiner et animer");
            p.Paragraphe("dessin(fenetre, x, y, largeur, hauteur) crée une surface où dessiner. Une couleur est un nombre donné par rgb(rouge, vert, bleu), chaque composante allant de 0 à 255.");
            p.Code("entier f = fenetre(\"Dessin\", 420, 320);\nentier d = dessin(f, 10, 10, 400, 280);\nentier x = 0;\ntantque (x < 360) {\n    effacer(d, rgb(20, 24, 40));\n    rectangle(d, x, 120, 40, 40, rgb(80, 140, 255));\n    dessiner_texte(d, \"FSL\", x + 8, 132, rgb(255, 255, 255));\n    x = x + 4;\n    attendre(30);       // 30 millisecondes : environ 30 images par seconde\n}");
            p.Paragraphe("point(dessin, x, y, couleur) colore un seul pixel, hasard(n) donne un nombre au hasard de 0 à n-1, et temps() le nombre de millisecondes écoulées depuis le démarrage. Le programme Balle.fsl, dans Applications, est un petit jeu complet : rattrapez la balle avec les flèches.");
            p.Section("Textes et fichiers");
            p.Code("texte s = \"File System OS\";\nafficher(longueur(s));               // 14\nafficher(majuscules(s));             // FILE SYSTEM OS\nafficher(sous_texte(s, 5, 6));       // System\nafficher(contient(s, \"OS\"));         // 1 (vrai)\nafficher(remplacer(s, \"OS\", \"!\"));   // File System !\nentier n = vers_nombre(\"42\") + 1;    // 43\necrire_fichier(\"Documents\\\\essai.txt\", \"Bonjour\");\nafficher(lire_fichier(\"Documents\\\\essai.txt\"));");
            p.Paragraphe("Les chemins de fichiers partent du dossier \\FSOS : \"Documents\\\\notes.txt\" désigne le fichier notes.txt du dossier Documents (la barre oblique inverse s'écrit deux fois dans un texte). vers_nombre renvoie 0/nul si le texte n'est pas un nombre.");
            p.Section("Le Créateur de programmes");
            p.Paragraphe("Le Créateur de programmes, dans le menu des applications, fabrique un programme sans qu'il soit nécessaire d'écrire du code.");
            p.Liste("À gauche, choisissez un élément (bouton, étiquette, champ, zone de texte ou dessin), puis cliquez dans la fenêtre pour le poser.");
            p.Liste("Glissez un élément pour le déplacer ; le petit carré bleu dans son coin change sa taille. Les flèches du clavier le déplacent aussi, et la touche Suppr l'efface.");
            p.Liste("À droite, donnez-lui un nom et un texte. Pour un bouton (ou un champ, avec la touche Entrée), choisissez son action avec les flèches < et > : écrire un texte, ajouter une ligne, calculer, compter, enregistrer ou charger un fichier, fermer le programme, ou votre propre code FSL.");
            p.Liste("Tester enregistre le projet et lance le programme. Voir le code ouvre dans l'Éditeur le code FSL produit, que l'on peut ensuite modifier à la main.");
            p.Paragraphe("Le projet est enregistré dans Applications sous deux formes : NOM.fsp, qui se rouvre dans le Créateur (par un double-clic dans l'Explorateur), et NOM.fsl, le programme lui-même. Quatre modèles sont prêts à l'emploi : Bonjour, Calculette, Compteur et Bloc-notes.");
            p.Section("Compiler, décompiler, vérifier");
            p.Paragraphe("Ces commandes s'utilisent dans la Console :");
            p.Liste("fslc fichier.fsl : compile le programme en un fichier binaire fichier.fsc.");
            p.Liste("run fichier.fsl ou run fichier.fsc : lance le programme.");
            p.Liste("decompiler fichier.fsc : retrouve le code FSL dans fichier-decompile.fsl. Les noms d'origine reviennent ; les commentaires, eux, ne sont pas dans le binaire.");
            p.Liste("fslverifier fichier.fsl : compile, décompile, recompile, et vérifie que le binaire obtenu est identique.");
            p.Liste("fonctions : affiche la liste de toutes les fonctions intégrées.");
            p.Note("Avant toute exécution, la sentinelle vérifie le binaire : un fichier .fsc abîmé ou modifié à la main est refusé au lieu d'être lancé.");
            p.Section("Les pilotes");
            p.Paragraphe("Un pilote est un programme FSL qui parle directement au matériel. Il est décrit par un fichier .config placé dans \\FSOS\\Systeme\\Pilotes :");
            p.Code("# Haut-parleur interne du PC\nnom = HautParleur\ndescription = Joue trois notes\nprogramme = HautParleur.fsl\nports = 0x42, 0x43, 0x61\ndemarrage = non");
            p.Paragraphe("Seul un pilote peut utiliser port_lire(port) et port_ecrire(port, valeur), ainsi que leurs versions 16 et 32 bits, et seulement sur les ports listés dans son .config. Il tourne dans son propre processus, nommé pilote-NOM ; s'il plante, il est mis en attente sans rien abîmer d'autre.");
            p.Code("// Extrait du pilote HorlogeCmos : lire un registre de la puce de l'horloge\nfonction entier registre(entier numero) {\n    port_ecrire(0x70, numero);       // choisir le registre\n    retourner bcd(port_lire(0x71));  // le lire, puis convertir\n}");
            p.Paragraphe("Commandes de la Console (réservées à un administrateur) :");
            p.Liste("pilotes : la liste des pilotes et leur état.");
            p.Liste("pilote demarrer NOM et pilote arreter NOM.");
            p.Liste("pilote nouveau NOM : crée un modèle de .config et de .fsl à compléter.");
            p.Liste("pilote exemples : installe les pilotes HautParleur et HorlogeCmos.");
            p.Section("Les erreurs fréquentes");
            p.Paragraphe("Chaque message indique un numéro de ligne, qui permet de retrouver l'erreur dans l'Éditeur.");
            p.Liste("« ';' attendu » : il manque un point-virgule à la fin de la ligne indiquée ou de la précédente.");
            p.Liste("« variable inconnue » : la variable n'est pas déclarée, est déclarée plus bas, ou son nom est mal écrit.");
            p.Liste("« type incompatible » : par exemple un texte rangé dans une variable entier ; utilisez vers_nombre ou vers_texte.");
            p.Liste("« calcul avec une variable nulle (0/nul) » : une variable n'a jamais reçu de valeur.");
            p.Liste("« division par zero » : vérifiez le diviseur avant de diviser.");
            p.Liste("« ... attend N argument(s) » : le nombre de valeurs entre parenthèses n'est pas le bon ; tapez fonctions dans la Console.");
            p.Liste("« plus de 256 appels imbriqués » : une fonction s'appelle elle-même sans fin.");
            p.Annexe("Annexe A : les fonctions intégrées");
            // Annexe A : construite depuis la table des fonctions integrees, donc toujours a jour
            for (int i = 0; i < Natives.Aides.Length; i++)
            {
                string a = Natives.Aides[i];
                int k = a.IndexOf(" : ");
                p.Fonction(k >= 0 ? a.Substring(0, k) : a, k >= 0 ? a.Substring(k + 3) : "");
            }
            p.Annexe("Annexe B : les mots réservés");
            p.Paragraphe("Ces mots ont un sens dans le langage et ne peuvent pas servir de noms de variables :");
            p.Code("int  entier  texte  chaine  booleen  rien  vrai  faux\nsi  sinon  tantque  pour  sortir  fonction  retourner  afficher");
            p.Paragraphe("Les noms des fonctions intégrées (annexe A) ne peuvent pas non plus être donnés à une fonction du programme.");
            return p.Terminer("Apprendre à programmer sous File System OS", "Version " + Services.Version, "File System OS Company");
        }

        private static string Documents() =>
            TextCodec.PathJoin(TextCodec.PathJoin(Live.Volume ?? "0:\\", "FSOS"), "Documents");

        public static string Chemin() => TextCodec.PathJoin(Documents(), Fichier);

        /// <summary>
        /// Ecrit le guide (deja fabrique, voir GuidePdf) dans Documents ; renvoie son chemin.
        /// Instantane : aucun calcul. Fabriquer() reste ici comme la description exacte de son
        /// contenu, mais n'est plus appele dans l'OS (trop lent sous Cosmos, voir GuidePdf).
        /// </summary>
        public static string Creer()
        {
            FsTools.MakeDir(Documents());
            string c = Chemin();
            FsTools.WriteBytes(c, GuidePdf.Octets);
            return c;
        }

        /// <summary>A l'ouverture de la session : le guide est cree s'il manque.</summary>
        public static void CreerSiAbsent()
        {
            try { if (!FsTools.Exists(Chemin())) Creer(); }
            catch (Exception e) { FileSystemOS.Noyau.Diagnostic.Report("guide", e.Message); }
        }
    }
}
