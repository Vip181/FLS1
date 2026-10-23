# File System OS

**File System OS Company** — créé par **Vincent Senut**.
Système d'exploitation écrit en C# avec Cosmos.

## Écran noir après GRUB sur un vrai PC

Cosmos **ne peut pas allumer l'écran graphique tout seul** : c'est le chargeur de démarrage
(GRUB ou Limine) qui doit préparer un « framebuffer » VBE avant de lancer le noyau. Sans cela,
le noyau démarre mais n'a aucun écran où dessiner : l'image reste noire.

Depuis la version 1.2.1, l'OS ne continue plus à l'aveugle : il bascule en **mode TEXTE de secours**
(qui marche sur tous les PC), affiche la cause, la machine reconnue, et propose d'essayer
1024x768, 800x600, 640x480, 1280x720 ou 1366x768 avec les touches 1 à 5.

### Les trois réglages à vérifier
1. **BIOS** : *Boot List Option* = **Legacy**, **Enable Legacy Option ROMs** coché, Secure Boot désactivé.
   En UEFI pur, il n'y a pas de VBE : l'écran restera noir.
2. **`.csproj`** :
   ```xml
   <CompileVBEMultiboot>True</CompileVBEMultiboot>
   <VBEResolution>1024x768x32</VBEResolution>
   ```
3. **GRUB** (si vous écrivez vous-même `grub.cfg`) — la ligne `gfxpayload` est indispensable :
   ```
   menuentry "File System OS" {
       insmod all_video
       set gfxmode=1024x768x32
       set gfxpayload=keep
       multiboot /boot/FileSystemOS.bin
       boot
   }
   ```
   Avec Limine (`creer-disque.sh`), l'équivalent est déjà en place : `resolution: 1024x768x32`.

## Correctif 1.28.2

**Compilation : « MSB6006 Arrêt de IL2CPU.exe avec le code -1073741819 »**
- -1073741819 = 0xC0000005 : violation d'accès dans IL2CPU lui-même, le compilateur de Cosmos.
- Cause : le guide embarqué en 1.28.1 était un tableau de 41 548 octets écrit en dur. Le
  compilateur C# le range dans un bloc de données brutes (RuntimeHelpers.InitializeArray), qu'IL2CPU
  gère mal à cette taille.
- Correction : le guide est rangé en texte base64, en 14 morceaux de 4 000 caractères (comme
  Cosmos range ses polices), et décodé une seule fois par notre propre décodeur, validé en Python
  (octets identiques, tous les cas de bourrage). Règle pour la suite : pas de grand tableau
  d'octets écrit en dur dans le noyau.

## Correctif 1.28.1

**Ouverture de session bloquée sur « En cours : 10. session : guide du FSL »**
- Cause : depuis la 1.25, l'OS fabriquait lui-même le guide PDF à l'ouverture de la session
  (pendant la connexion automatique). Cette fabrication crée des dizaines de milliers de petites
  chaînes de texte (caractère par caractère) ; sous Cosmos, le gestionnaire de mémoire ralentit
  à mesure qu'elles s'accumulent, et la session restait bloquée sur cette étape.
- Correction : le guide est fabriqué d'avance, par le même programme et avec le même contenu
  (94 blocs, annexe des 44 fonctions), puis embarqué tel quel (`Systeme/GuidePdf.cs`, 41 Ko,
  11 pages). L'OS se contente d'écrire ces octets dans `\FSOS\Documents\Guide-FSL.pdf` :
  instantané. Le menu « Guide FSL » et la commande `guide` l'écrivent s'il manque et l'ouvrent.
- `GuideFsl.Fabriquer` reste la description exacte du contenu, mais n'est plus appelé dans l'OS.

## Nouveautés 1.28

**Pilote de transparence (logiciel)** (`Graphique/Transparence.cs`)
- Ni Cosmos ni la puce graphique Intel ne fournissent de mélange de couleurs matériel
  utilisable : la transparence est calculée par le processeur. Ce module la rend rapide et sûre :
  une table de 65 536 cases (calculée une fois) remplace les divisions et multiplications par
  pixel ; les boucles travaillent directement en mémoire ; chaque zone est rognée aux bords
  avant la boucle, donc aucune écriture ne peut déborder.
- Validé en Python : 4 000 rectangles tordus (bords négatifs, trop grands, rayons excessifs),
  aucun débordement, exactement les bons pixels, écart d'au plus 1 sur 255 avec l'ancien calcul.
- Toutes les fonctions de transparence (`Melanger`, `Teinter`, `FillRoundRectAlpha`, `Flouter`)
  passent par lui.

**Bureau classique** (`Graphique/Taskbar.cs` réécrit ; inspiré d'un bureau Linux classique)
- Barre pleine sur toute la largeur (40 pixels) : le F bleuté semi-transparent (bouton Démarrer),
  le lancement rapide (Fichiers, Navigateur, Éditeur, Console, Créateur), un onglet par fenêtre
  (icône et titre ; trait bleu sous la fenêtre active ; un clic la réduit ou la ramène), le compte et
  l'heure à droite. Surfaces pleines : rien à mélanger, le plus léger possible.
- Menu Démarrer à trois colonnes : à gauche le compte (avatar, nom, rôle) et les lieux (Dossier
  FSOS, Documents, Images, Applications, Corbeille) ; au milieu les catégories (Toutes,
  Accessoires, Bureautique, Graphisme, Internet, Jeux, Programmation, Système, Mes programmes) ;
  à droite les applications avec leur description ; en bas Verrouiller, Se déconnecter, Éteindre
  et la recherche (on tape ; Entrée lance, Échap efface puis ferme). Nouveau : « Guide FSL ».
- La partie fixe du menu est calculée à l'ouverture et à chaque changement de catégorie ou de
  recherche ; le survol ne redessine qu'une ligne.

**Page de connexion vitrée** (`Graphique/PageConnexion.cs` ; inspirée d'une fenêtre de connexion)
- Le fond d'écran, un panneau de verre au centre (identifiant avec son icône, mot de passe, bouton
  « Voir », bouton rond en flèche), Éteindre et Redémarrer ; les comptes en avatars au-dessus.
- Le fond et le verre sont calculés une fois (à l'ouverture ou si le fond change) ; le curseur et
  la saisie ne redessinent que les champs. Textes posés sur le fond : sombres sur un fond clair,
  blancs sur un fond foncé. Même logique qu'avant : premier compte, verrouillage, connexion.
- Aperçus : `Apercu-bureau-classique.png`, `Apercu-connexion.png`.

## Nouveautés 1.27

**Fonds d'écran simples, au choix** (`Graphique/Window.cs`, `Graphique/DisplayWindow.cs`)
- 7 fonds simples, dessinés instantanément (un dégradé à trois couleurs, ligne par ligne) :
  **Ciel** (par défaut), Nuit, Aurore, Lagon, Forêt, Sable, Graphite. Aucun calcul lourd : rien
  ne peut ralentir ni bloquer le démarrage.
- **Vagues** devient une option (calculée en arrière-plan après le démarrage, comme en 1.26.2).
- Paramètres : une rangée de vignettes (chaque dégradé en miniature) ; un clic l'applique.
  Console : `fond` (liste), `fond lagon`, `fond vagues`, `fond <image>`.

**Vos images en fond d'écran (JPEG, PNG, BMP, GIF)**
- Visionneuse d'images : bouton « Fond d'écran ». Avant, l'image était décodée pendant le
  changement de fond (un grand JPEG pouvait figer le système et déclencher le gardien).
- Désormais : le fond simple « Ciel » s'affiche tout de suite, puis le thread « fond » prépare
  l'image en arrière-plan, par tranches de 15 ms : décodage (JPEG par étapes, avec le pourcentage
  affiché), mise à la taille de l'écran (l'image remplit l'écran sans déformation), puis une copie
  toute prête dans `\FSOS\Systeme\fond-ecran.cache`. Aux démarrages suivants, cette copie est
  simplement relue : plus aucun décodage.
- `JpegImage.Commencer` / `Decodeur.Avancer` : décodage JPEG par étapes (mêmes calculs que la
  version validée ; `JpegImage.Load` fait simplement toutes les étapes d'affilée).
- PNG et GIF se décodent d'un bloc : un grand PNG peut figer l'écran plusieurs dizaines de
  secondes, une seule fois (le gardien est suspendu le temps de ce travail). Pour une grande
  image, préférez le JPEG. Une image de la taille de l'écran (par exemple 1920 x 1080) donne le
  meilleur résultat ; une photo de plus de 4 millions de pixels est lue au 1/8 (plus floue).

## Correctif 1.26.2

**Démarrages irréguliers (parfois bon, parfois écran noir, parfois figé sans message)**
- Mesure de départ : sous Cosmos, le Dell traite environ dix millions d'opérations par seconde
  (le calcul des vagues de la 1.26, environ 90 millions d'opérations, dépassait 8 secondes).
  Plusieurs effets de la 1.26 recalculaient beaucoup trop de pixels au mauvais moment.
- **Verre rapide** (`PixelBuffer.VerreRapide`) : la zone est réduite au quart (un pixel pour
  4 x 4), floutée et teintée à cette taille, puis agrandie ligne par ligne. Seize fois moins de
  pixels : barre du haut, dock et menu Démarrer.
- **Barre des tâches** : sa partie fixe (fond, verre du dock et de l'horloge, séparateurs) est
  calculée une fois et ne change que si le dock change de taille ou si le fond change ; un survol
  ne coûte plus qu'une recopie, le F, les icônes et le surlignage.
- **Menu Démarrer** : sa partie fixe (verre, profil, pastilles, icônes, noms, barre de recherche)
  est calculée à l'ouverture et à chaque changement de recherche ; le survol recopie l'image et
  redessine une seule case.
- **Noms des icônes du bureau** : plus de pastille translucide recalculée à chaque image ; la
  couleur du texte (sombre ou claire) dépend de la luminosité du fond sous le nom.
- Copies de pixels par `MemoryAccelerator.Copy32` (la copie éprouvée et protégée du projet)
  au lieu d'`Array.Copy`, que le projet n'utilisait que sur des octets.
- Calcul des vagues protégé : une erreur l'arrête (le ciel reste) au lieu de se répéter.
- **Détecteur de gels silencieux** (thread gardien) : si l'écran de démarrage n'avance plus
  pendant 15 s, ou si aucune image ne sort pendant 10 s alors que l'écran doit être redessiné,
  un bandeau orange affiche l'étape, la dernière erreur et les précédentes. Le gardien d'urgence
  (bandeau rouge) ne voyait que l'arrêt complet de l'ordonnanceur.
- L'écran de démarrage affiche en rouge la dernière erreur survenue pendant le chargement.

## Correctif 1.26.1

**Blocage au démarrage** (« BLOCAGE dans : demarrage / chargeur », étape Interface graphique, redémarrage)
- Cause : le nouveau fond « Vagues » était calculé d'un seul bloc pendant l'étape Interface
  graphique. Sur l'écran du Dell, ce calcul dépassait les 8 secondes du gardien, qui redémarrait.
- Correction : au démarrage, seul le ciel est dessiné (instantané) ; les rubans sont calculés
  ensuite par le thread « fond » (processus « interface »), par tranches de 15 ms, pendant l'écran
  de connexion ; le verre et l'écran sont rafraîchis à la fin. Le démarrage n'attend plus.
- Calcul par tables (lumière selon la position, couleur selon la lumière) : il ne reste qu'une
  division par pixel. Image identique pixel pour pixel au calcul direct (vérifié en Python).
- Ouverture du menu Démarrer : flou calculé à demi-résolution (4 fois moins de pixels) puis
  agrandi. Ombre et liseré du menu limités aux bords, au lieu de mélanger environ 600 000 pixels à
  chaque image affichée.

## Nouveautés 1.26

**Nouveau bureau : dock vitré, menu Démarrer centré, F semi-transparent** (`Graphique/Taskbar.cs` réécrit)
- **Dock** flottant au centre du bas de l'écran, en verre dépoli : le **F semi-transparent**
  (bouton Démarrer : F en verre bleuté, dégradé, reflet, liseré clair ; tuile claire au survol),
  les applications épinglées (Fichiers, Créateur, Éditeur, Console, Navigateur, Images), puis les
  fenêtres ouvertes. Trait sous l'icône : fenêtre ouverte (long et bleu : celle qui a la main).
  Info-bulle au survol. Horloge et compte dans une pastille de verre à droite.
- **Menu Démarrer** centré au-dessus du dock (touche Windows ou clic sur le F) : profil (avatar à
  l'initiale, rôle), boutons Documents, Images, Paramètres, Alimentation ; 20 applications
  épinglées avec les icônes de File System OS ; « Mes programmes » (dossier Applications, ordre
  alphabétique) ; **recherche au clavier** (on tape, Entrée lance le premier résultat, Échap
  efface puis ferme) ; Verrouiller et Se déconnecter.
- **Fond d'écran « Vagues »** (dessin original : trois rubans bleus brillants sur un ciel clair),
  calculé en entiers au seizième de pixel. Il remplace le dégradé sombre quand aucun fond n'est choisi.
  La barre du haut devient du verre sombre ; les noms des icônes du bureau ont une pastille.
- Technique : le verre (flou + teinte) est calculé une fois — au changement de fond pour le dock et
  la barre du haut, à l'ouverture pour le menu (ce qui est derrière lui) — et ne coûte rien ensuite.
  `PixelBuffer` : `Flouter` (moyenne glissante, coût indépendant du rayon), `Teinter`,
  `CopierZone` / `CopierArrondi` (copie par lignes), `FillRoundRectAlpha`, `Melanger`.
  `Desktop.TaskbarH` passe à 56 ; le fond couvre tout l'écran ; `Window.IconeSource`.
- Aperçu : `Apercu-nouveau-bureau.png` (rendu Python avec les mêmes calculs et les vraies icônes).

## Nouveautés 1.25

**Créateur de programmes : programmer sans écrire de code** (`Graphique/StudioWindow.cs`, `Fsl/Generateur.cs`)
- Menu des applications (ou icône du bureau, ou commande `createur`). À gauche, les éléments
  (bouton, étiquette, champ, zone de texte, dessin) ; au centre, la fenêtre du futur programme,
  où l'on pose, glisse et redimensionne les éléments (coin bleu ; flèches ; Suppr) ; à droite,
  leurs propriétés.
- Action d'un bouton (ou d'un champ, touche Entrée), choisie avec les flèches < > : écrire un
  texte, ajouter une ligne, calculer (+ - * / %), effacer, compter, remettre à zéro, enregistrer
  ou charger un fichier, fermer le programme, ou du code FSL libre. Renommer un élément met à
  jour les actions qui le visent.
- Enregistrer : `Applications\NOM.fsp` (projet, se rouvre dans le Créateur, aussi par
  l'Explorateur) et `Applications\NOM.fsl` (le programme). Tester : enregistre et lance.
  Voir le code : ouvre le code produit dans l'Éditeur. Modèles : Bonjour, Calculette,
  Compteur, Bloc-notes.
- Validé en Python : les 4 modèles et 2 000 projets aléatoires (noms bizarres, doublons, mots
  réservés, guillemets...) donnent du code qui compile ; 3 000 projets enregistrés puis relus
  sont identiques.

**Guide du File Syst Langage en PDF** (`Utils/PdfEcrivain.cs`, `Systeme/GuideFsl.cs`)
- L'OS fabrique lui-même un vrai PDF (A4, Helvetica et Courier, accents) : couverture avec
  sommaire, 13 chapitres (variables, calculs, conditions, boucles, fonctions, fenêtres et
  événements, dessin, textes et fichiers, Créateur, compilation, pilotes, erreurs fréquentes),
  annexe des fonctions construite depuis la table du langage (toujours à jour), mots réservés.
- Créé à l'ouverture de la session s'il manque : `\FSOS\Documents\Guide-FSL.pdf`, lisible
  dans le Lecteur PDF et sous Windows. Commande `guide` : le refait et l'ouvre.
- Validé : qpdf sans erreur, rendu contrôlé page par page, les 12 programmes du guide compilent.

**Lecteur PDF** : l'indentation des lignes de code est gardée, les guillemets « » s'affichent,
et le fichier est lu sur le disque mémoire (FatDirect) comme les images.

## Nouveautés 1.24

**File Syst Langage, version 2** (`Fsl/Lexer.cs`, `Fsl/Compiler.cs`, `Fsl/Vm.cs`)
- Types : `entier` (ou `int`), `texte` (ou `chaine`), `booleen` (`vrai`, `faux`) ; `rien` pour une fonction.
- Fonctions : `fonction entier carre(entier x) { retourner x * x; }` — paramètres typés,
  variables locales, `retourner`, récursivité (256 appels imbriqués). On peut appeler une
  fonction écrite plus bas : une première passe relève tous les en-têtes.
- Boucle `pour (entier i = 0; i < 10; i = i + 1) { }` (plusieurs boucles peuvent réutiliser `i`),
  `sortir;`, commentaires `/* ... */`, nombres hexadécimaux `0x42`.
- 44 fonctions intégrées : fenêtres, dessin, textes (`majuscules`, `contient`, `remplacer`...),
  calcul (`abs`, `min`, `max`, `racine`, `puissance`), fichiers (`lire_fichier`, `ecrire_fichier`),
  `journal`, et les ports matériels pour les pilotes. Console : `fonctions`.
- Le compilateur cherche les noms dans une table de hachage maison (Dictionary n'est pas fiable
  sous Cosmos) : des milliers de variables ne le ralentissent pas. Fichiers jusqu'à 1 Mo.
- Exemple : `Applications\Calculs.fsl` (factorielle récursive, nombres premiers).

**Compiler en binaire, puis retrouver le code** (`Fsl/Decompilateur.cs`)
- Binaire `.fsc` au format `FSC2` (avec la table des fonctions) ; les anciens `FSC1` se lisent toujours.
- `decompiler prog.fsc` crée `prog-decompile.fsl` : code lisible, parenthèses seulement là où
  elles comptent, `vrai`/`faux`, noms d'origine. Les commentaires ne sont pas dans le binaire.
- `fslverifier prog.fsl` : compile, décompile, recompile et compare le bytecode mot à mot.
- Validé sur un modèle Python du compilateur : 12 000 programmes aléatoires et un programme de
  6 000 lignes (1 500 variables, 200 fonctions) redonnent exactement le même binaire.
  Ce test a trouvé une ambiguïté : un `sinon { }` vide ne génère donc plus aucun saut.

**Pilotes écrits en FSL, décrits par un fichier .config** (`Systeme/PilotesFsl.cs`)
- Dossier `\FSOS\Systeme\Pilotes` ; un fichier `.config` par pilote :
  `nom`, `description`, `programme` (le .fsl ou .fsc), `ports` (ex. `0x42, 0x43, 0x61`),
  `demarrage = oui|non` (démarrage automatique avec le bureau).
- Chaque pilote tourne dans son processus `pilote-nom`. Seuls les pilotes peuvent appeler
  `port_lire` / `port_ecrire` (8, 16 et 32 bits), et seulement sur les ports de leur .config.
  S'il plante, il est mis en attente comme toute application.
- Console (administrateur) : `pilotes`, `pilote demarrer NOM`, `pilote arreter NOM`,
  `pilote nouveau NOM` (crée un modèle .config + .fsl), `pilote exemples`.
- Exemples : `HautParleur` (trois notes par la puce PIT) et `HorlogeCmos` (lit l'heure
  directement dans la puce CMOS et l'affiche dans une fenêtre).

## Nouveautés 1.23

**Le File Syst Langage crée des fenêtres et des jeux** (`Fsl/Natives.cs`, `Graphique/FslWindow.cs`)
- Appels de fonctions `nom(arg, arg)` (instruction ou valeur), virgule, et `\n`, `\"`, `\\`
  dans les textes. Deux instructions de bytecode : `Appel` (vérifié par la sentinelle) et `Pop`.
- Fenêtres et éléments : `fenetre`, `bouton`, `etiquette`, `champ` (une ligne), `zone`
  (texte de plusieurs lignes), `dessin` (surface), `lire`, `ecrire`, `fermer`.
- Événements : `evenement()` renvoie `"clic:N"`, `"valider:N"`, `"touche:gauche"`,
  `"touche:a"`, `"souris:N:x:y"`, `"fermeture:N"`, ou `""` (le programme cède alors la main).
- Dessin : `effacer`, `rectangle`, `point`, `dessiner_texte`, `rgb`.
- Outils : `hasard`, `temps`, `attendre`, `longueur`, `vers_texte`, `vers_nombre`, `sous_texte`.
- Exemples : `Applications\Fenetre.fsl` (bouton, champ, zone) et `Applications\Balle.fsl` (jeu).
- Console : `fonctions` liste toutes les fonctions avec leur mode d'emploi.

**Mise en attente des processus et des threads**
- Un thread d'application qui plante est **mis en attente** au lieu de replanter à chaque tour :
  ses données et ses fenêtres restent, rien d'autre n'est touché. Le Gestionnaire des tâches
  l'affiche en rouge « attente (erreur) » ; son bouton d'attente le reprend.
- Console : `attente PID [TID]`, `reprendre PID [TID]`, `threads PID`. Les processus du système
  ne peuvent pas être mis en attente.

**Images : JPEG et GIF animés** (PNG et BMP étaient déjà lus)
- `JpegImage.cs` : JPEG standard (« baseline »), gris ou couleur 4:4:4 / 4:2:2 / 4:2:0,
  marqueurs de redémarrage, transformée inverse en entiers ; une photo de plus de 4 millions de
  pixels est lue au 1/8 (instantané). Validé contre PIL : écart moyen 0,1 à 0,9 sur 255.
  Non gérés : JPEG progressif, CMJN.
- `GifLecteur.cs` : GIF animés joués dans la visionneuse (palettes locales, transparence,
  entrelacement, effacement), une image décodée à la fois. Validé : images identiques à PIL.

## Diagnostic 1.22.1 — « mémoire : copie hors limites évitée (Copy32) »

Ce message vient du garde-fou ajouté en 1.20.2 : une copie de pixels allait écrire en dehors
de sa zone de mémoire. Elle a été bloquée (rien n'est abîmé), mais elle révèle un défaut de
dessin qui, avant le garde-fou, abîmait silencieusement la mémoire.
- Le rapport donne maintenant **le nom des deux images concernées** (fenêtre, scène, écran…),
  leurs tailles et la position de la copie, pour les 3 premiers cas (F12 = diagnostic).
- Au démarrage, la taille de l'image d'écran fournie par Cosmos est comparée à la résolution.

## Nouveautés 1.22

**Capture d'écran et vidéo**
- Application **Capture d'écran** (menu et bureau) : « Capturer maintenant », « Capturer dans
  3 s » (la fenêtre se cache le temps de la photo), et **« Enregistrer une vidéo »** : GIF animé,
  2 images par seconde en demi-résolution, lisible sous Windows et dans tout navigateur.
  L'encodage se fait par petits morceaux (thread « video ») : l'interface reste fluide.
  Limite 24 Mo, arrêt automatique. Encodeur LZW validé en Python avant traduction.
- Touche **Impr. écran**, et **Ctrl + Alt + P** si le clavier n'envoie pas Impr. écran.

**Fluidité : la session du SSD s'enregistre en arrière-plan**
- Avant : à chaque action (déplacer une icône, enregistrer un fichier…), toute la session
  était relue et comparée, puis réécrite sur le SSD, pendant l'action : souris et système figés.
- Maintenant, `FsTools` note chaque fichier écrit, dossier créé ou élément supprimé, et le
  thread **« synchro »** les recopie UN PAR UN, 1,5 s après la dernière modification.
  « Sauvegarder la session », l'arrêt et le redémarrage écrivent tout ce qui reste.
- Console : `session` (état, opérations en attente), `session enregistrer`, `session comparer`.

**L'installateur crée lui-même la partition FSOS**
- `Pilotes/Partitionneur.cs` : dans l'**espace non alloué** d'un disque GPT ou MBR, crée une
  partition FSOS (alignée sur 1 Mo, 64 Mo à 32 Go) et la formate en FAT32. Les sommes de
  contrôle GPT sont vérifiées avant d'écrire ; la copie de secours est écrite en premier,
  puis la copie principale, puis tout est relu. Logique validée en Python sur une image GPT.
- L'assistant propose « Espace libre … (non utilisé) » en tête de la liste des partitions.
- **Il ne réduit jamais une partition existante** (C: en NTFS : seul Windows sait déplacer ses
  fichiers sans risque). Pour faire de la place, réduisez C: dans la Gestion des disques, puis
  laissez File System OS créer sa partition.

## Correctif 1.21.4

- **Erreurs CS1002 dans EditorWindow.cs** (lignes 82 et 106) : deux points-virgules oubliés
  lors du remplacement automatique de la 1.21.1.
- **Blocage « systeme / entree » après « Ouvrir la session »** de l'assistant : la session
  venait d'être copiée de la mémoire vers le SSD, puis était intégralement recopiée dans
  l'autre sens. Elle est maintenant simplement **rattachée** (`SessionSsd.Adopter`) : rien
  n'est recopié, le premier enregistrement écrit chaque fichier une fois.
- Suivi en jaune ajouté au chargement de la session (au démarrage et après l'installation,
  fichier par fichier) et à la lecture des réglages : un nouveau blocage dira où.

## Correctif 1.21.3 — la ligne jaune « 99. session : dernier utilisateur »

Le suivi a montré 99 étapes là où il en faut une vingtaine : la session du SSD réécrivait,
puis EFFAÇAIT, tous ses fichiers à chaque enregistrement. Cause : sous Cosmos,
`List<string>.IndexOf` et `Contains` ne reconnaissent pas deux textes identiques lus à des
moments différents (comparaison des adresses).
- **`Utils/Listes.cs`** : recherche dans une liste de textes en comparant leur contenu (`==`).
  Remplace `IndexOf`/`Contains` dans la session du SSD, les réglages (`Config.Get` ne lisait
  donc jamais les fichiers !), le bureau et l'installation.
- **Garde-fou** : la session ne supprime plus jamais du SSD un fichier qui existe encore en
  mémoire.
- **Lectures du disque mémoire par FatDirect** aussi (`FsTools.Exists/ReadBytes`, utilisés par
  `TextCodec.ReadLines`, les réglages, les comptes et le journal) : Cosmos ne lit plus les
  fichiers système.
- Suivi plus fin dans l'écriture des réglages (lecture, écriture du fichier).

## Diagnostic 1.21.2 — blocage total à la création du compte

Sur le Dell, un clic sur « Créer le compte » fige tout (même la souris) pendant plus d'une
minute, sans la bande rouge du chien de garde : le processeur s'est arrêté (exception
matérielle ou interruptions coupées), pas une opération lente.
- **`Noyau/Suivi.cs`** : chaque étape délicate s'affiche AUSSITÔT en bas de l'écran
  (« En cours : N. … »), directement dans l'image envoyée à l'écran, sans attendre le
  compositeur. Si tout se fige, la dernière ligne dit où.
- Étapes suivies : création du compte (sel, empreinte SHA-256, comptes.cfg, dossiers),
  journal, enregistrement de la session (chaque fichier écrit ou effacé sur le SSD),
  ouverture de session (dernier utilisateur, bureau, chaque fenêtre de départ).

## Correctif 1.21.1 — « Compte créé mais non enregistré : Argument out of range »

Le pilote de fichiers de Cosmos écrivait des noms longs invalides sur le disque mémoire
(« Applications », « Documents », « Utilisateurs »…), comme il l'avait fait sur le SSD
(JOURNA~1). Il échouait ensuite en relisant ses propres dossiers.
- **Toutes les écritures du disque mémoire passent par le pilote FAT32 de File System OS**
  (`FsTools.WriteBytes / MakeDir / DeleteFile / DeleteDir`, routés vers `Live.Direct`). Les
  15 endroits qui écrivaient directement avec Cosmos (Explorateur, Éditeur, Bureau, Corbeille,
  Captures, Comptes, Téléchargements, mode Live, session du SSD…) passent par ces fonctions.
  Cosmos ne sert plus qu'à lire.
- L'enregistrement de la session sur le SSD lit aussi le disque mémoire avec FatDirect.
- Disque mémoire de **288 Mo** (FAT32 en grappes de 4 Ko, le format le plus courant) dès
  2 Go de mémoire : plus de place pour la session.

## Nouveautés 1.21 — connexion

- **Directement au bureau** : au démarrage, dès qu'un compte existe, File System OS ouvre la
  session du dernier compte utilisé, sans page intermédiaire (`connexion_auto=oui` dans
  `demarrage.cfg`, par défaut). Au tout premier démarrage, la page de création du compte
  s'affiche d'abord. Commande Console : `connexion auto oui|non`.
- **Plus d'assistant d'installation** (choix Live / installation) quand File System OS est déjà
  installé sur le SSD, même si sa session n'a pas pu être chargée.
- **Vraie page de reconnexion** (déconnexion, démarrage sans connexion automatique) :
  « Veuillez entrer votre identifiant », une **tuile par utilisateur** avec son avatar (rond de
  couleur propre à chacun, initiale en grand, mention « admin »), clic ou flèches pour choisir,
  puis le mot de passe. Le dernier compte utilisé est déjà choisi. Verrouillage : grand avatar
  de la session et mot de passe seulement.

## Correctif 1.20.2 — mémoire abîmée

Symptômes sur le Dell : texte impossible dans les champs de la page de connexion (¤, Ï : le
champ n'accepte pourtant que les lettres du clavier), ligne de disque vide dans l'assistant,
erreurs « systeme/entree » et « cogestion/rendus : IndexOutOfRange ». Ce sont des objets
abîmés en mémoire après coup, pas un problème de clavier.
- **Pilote SATA** : le `try/finally` ajouté en 1.18.1 autour de chaque commande disque (avec un
  `return` dans le `try`, construction risquée pour IL2CPU) est retiré. Depuis la 1.19, la
  session est chargée depuis le SSD au démarrage : des milliers de commandes, donc autant
  d'occasions d'abîmer la mémoire.
- **Garde-fous** : `MemoryAccelerator.Copy32/Fill32` (copies de pixels par pointeurs, sans
  contrôle) ne peuvent plus sortir de leurs tableaux. Une tentative est bloquée et signalée
  une fois dans le diagnostic (F12) : « copie hors limites évitée ».
- **Ramasse-miettes** toutes les 3 secondes au lieu de plusieurs fois par seconde.

## Correctifs et nouveautés 1.20

- **Bug « cogestion/rendus : Arg_IndexOutOfRangeException »** : la file des rendus de fenêtres
  ne déplace plus d'éléments (la fenêtre active est rendue d'abord, directement) ; si la file
  est abîmée, elle est vidée proprement et chaque fenêtre redemande son dessin.
- **L'ancienne fenêtre Installation** (« Choisissez le disque », jamais utilisable sur le SSD)
  est remplacée par l'assistant d'installation, dans le menu, et ne s'ouvre plus d'elle-même.
- **Disque mémoire du mode Live en FAT32** (FAT16 en secours) : sur le Dell, le dossier \FSOS
  n'était pas créé dans le FAT16. L'état du mode Live s'affiche au démarrage.
- **Noyau déjà installé : rien à réinstaller.** `installer-ssd.ps1` compare le noyau de l'ISO à
  celui du SSD : identique, il ne fait rien ; différent, il remplace seulement le noyau (ni
  téléchargement, ni GRUB). `-Reinstaller` force une installation complète. La version est
  notée dans `X:\boot\noyau-version.txt` (lue dans le noyau grâce au marqueur
  `FSOS-NOYAU-VERSION=`).
- **Démarrage sur une clé plus récente** : File System OS compare sa version à celle du noyau
  du SSD et, si elles diffèrent, affiche à la connexion comment mettre le SSD à jour.

## Nouveautés 1.19.1 — démarrer sans la clé

Tout File System OS (programme de démarrage de Cosmos, noyau, threads, processus, multitâche,
interface, connexion, bureau, programmes) tient dans un seul fichier : le noyau de l'ISO.
**`Outils/installer-ssd.ps1`** (Windows, PowerShell administrateur) :
1. copie le noyau et le menu GRUB de l'ISO sur la partition FSOS (`X:\boot`) ;
2. télécharge GRUB 2.06 pour Windows (projet GNU, même version que Rufus) ;
3. crée une partition de démarrage BIOS de 1 Mo à la fin de l'espace non alloué ;
4. sauvegarde le premier Mo du disque dans Documents, demande confirmation, installe GRUB ;
5. vérifie que GRUB est bien au début du disque.

Puis BIOS : Boot List Option **Legacy** et « Internal HDD ». Windows : **UEFI**.
Après chaque recompilation, relancer le script. `-Retirer` annule tout. Il remplace
`demarrage-ssd.ps1` (Limine), qui n'avait jamais abouti sur le Dell.

## Nouveautés 1.19 — la session installée passe par le pilote de File System OS

Constat sur le Dell : le pilote de fichiers de Cosmos, à qui la partition FSOS était confiée
depuis la 1.18, y écrivait des noms longs invalides (JOURNA~1, signalé par chkdsk), et le
démarrage en AHCI restait ensuite noir. Après `chkdsk X: /f`, le démarrage est revenu.

- **Le pilote de Cosmos ne touche plus au SSD** (sauf fichier `FSOS-MONTER.TXT`, expérimental).
- **`Systeme/SessionSsd.cs`** : au démarrage, si File System OS est installé sur le SSD
  (dossier FSOS), tout ce dossier est chargé en mémoire avec FatDirect : réglages, comptes,
  documents, images, programmes. Pas d'assistant : directement la page de connexion.
- À chaque sauvegarde (fichier modifié, compte créé, déconnexion, arrêt, « Sauvegarder la
  session »), seuls les fichiers qui ont changé sont réécrits sur le SSD (empreinte FNV-1a de
  chaque fichier), et ceux qui ont été supprimés sont effacés du SSD.
- Assistant : après l'installation, **« Ouvrir la session »** charge tout de suite la session
  du SSD, sans redémarrer.
- Les commandes `ssd` partagent le même montage que la session (un seul cache de FAT) et
  oublient ce cache avant chaque commande.
- Le chien de garde n'écrit jamais sur le SSD pendant une alerte de blocage.

## Correctif 1.18.2 — revue de l'installation sur le SSD

Constat : aucun fichier de File System OS sur la partition FSOS vue depuis Windows.
- **L'assistant ne pouvait pas s'afficher sur le Dell** : il attendait que le mode Live soit
  prêt, or le disque mémoire du mode Live ne s'y prépare pas. Il s'affiche maintenant dès qu'un
  SSD est présent et que File System OS n'est installé sur aucun vrai disque.
- **Commande `installation`** : ouvre l'assistant à tout moment depuis la Console (il se ferme
  alors sans déconnecter).
- **Commande `ssd test`** : test d'écriture de bout en bout. Trouve la partition FSOS, y écrit
  `FSOS-TEST.TXT`, le relit avec un montage tout neuf, compare octet par octet, affiche les
  compteurs du SSD et laisse le fichier pour vérifier depuis Windows.
- Revue du pilote SATA : la commande d'écriture (bit W, WRITE DMA EXT, adresses 48 bits, table
  PRDT, vidage du cache par FLUSH CACHE EXT) est conforme. Les lectures sont prouvées sur le
  Dell ; les écritures doivent l'être avec `ssd test`.
- Côté Windows : **désactivez le démarrage rapide** (`powercfg /h off`). Sinon Windows garde en
  mémoire l'ancien contenu de la partition FSOS, ne voit pas les fichiers écrits par File
  System OS, et peut même les écraser.

## Correctif 1.18.1 — blocage « noyau / gardien »

Sur le Dell, la page de connexion s'est figée 8 s puis l'ordinateur a redémarré, avec le
bandeau « BLOCAGE dans : noyau / gardien ».
- Le gardien ne lit plus la puce d'horloge RTC à chaque tour de l'ordonnanceur (des milliers de
  fois par seconde) : il utilise l'horloge interne du PIT.
- Le nettoyage de la mémoire (ramasse-miettes) a droit à 20 s avant l'alerte.
- Le bandeau rouge a une nouvelle ligne **« Étape : »** qui dit exactement ce que faisait le
  système : programme en cours, ordonnanceur, nettoyage de la mémoire, lecture de l'horloge,
  ou attente du SSD (avec la commande et le secteur). Elle est aussi notée dans le journal.

## Nouveautés 1.18 — assistant d'installation

Au démarrage, **avant la page de connexion**, un assistant plein écran s'ouvre si un SSD est
détecté et que File System OS n'y est pas encore installé :

1. **Accueil** : « Installer sur le SSD » ou « Essayer en mode Live ».
2. **Disque** : la liste des SSD (SATA et NVMe), avec le nombre de partitions FAT32 utilisables.
3. **Partition** : toutes les partitions du disque. Seules les FAT32 sont cliquables ; celles de
   Windows (NTFS, EFI de démarrage) sont affichées mais protégées. La partition nommée FSOS est
   entourée en vert.
4. **Confirmation**, puis copie (dossier `FSOS` ajouté, rien n'est effacé ni formaté).
5. **Redémarrer** : au démarrage suivant, la partition où File System OS est installé est montée
   comme lecteur système (réglages, comptes et documents conservés).

**Sans SSD, le démarrage passe directement en mode Live.** Déjà installé : directement la
connexion. Souris, ou clavier : chiffres pour choisir, Entrée pour valider, Échap pour revenir.

Si un jour le montage de la partition installée bloquait le démarrage (à l'étape « montage des
disques »), déposez depuis Windows un fichier `FSOS-PAS-MONTER.TXT` à sa racine.

**Écran noir en AHCI en démarrant sur la clé** : le BIOS essaie d'abord le SSD, dont la zone de
démarrage est vide. Mettez la clé avant « Internal HDD » (F2 > General > Boot Sequence), ou
choisissez-la avec F12.

## Correctif 1.17.3 — démarrer le SSD en mode AHCI

Constat sur le Dell E7470 : le SSD démarre en mode normal (RAID, le chargeur d'Intel le lance)
mais reste noir en AHCI, sans même afficher le menu Limine. En Legacy, le BIOS ne lance un disque
que si une partition y est marquée « active » ; un disque GPT n'en a jamais.

Nouvelle commande, depuis la clé en AHCI :
```
ssd amorce sata0            affiche l'état (drapeau, présence de Limine)
ssd amorce sata0 actif      marque le MBR de protection comme démarrable
ssd amorce sata0 inactif    annule
```
Seul l'octet 446 du secteur 0 change. L'UEFI doit ignorer ce drapeau, donc Windows démarre
toujours ; sinon, `ssd amorce sata0 inactif` depuis la clé remet tout comme avant.

## Nouveautés 1.17 — File System OS s'installe sur le SSD

**La partition FSOS devient un vrai lecteur.** Au démarrage, si le SSD Windows contient une
partition FAT32 nommée FSOS, elle est présentée seule à Cosmos (un petit disque virtuel :
faux MBR + les vrais secteurs de la partition, `Pilotes/VueFsos.cs`). Les partitions Windows
restent invisibles et tout accès en dehors de FSOS est refusé. Conséquences :
- elle apparaît dans l'Explorateur et dans la fenêtre Installation ;
- après l'installation (`installer`, ou le bouton de la fenêtre Installation), File System OS
  démarre sur cet environnement : réglages, comptes, documents et programmes sont conservés
  d'un démarrage à l'autre ;
- avant l'installation, le mode Live y sauvegarde la session (dossier `FSOS-Live`).

**1.17.2 : désactivé par défaut.** Sur le Dell E7470 en mode AHCI, le montage de cette
partition par Cosmos laissait l'écran noir au démarrage. Il ne se fait plus que si un fichier
`FSOS-MONTER.TXT` existe à la racine de la partition (expérimental). Les commandes `ssd`, qui
passent par le pilote de File System OS, fonctionnent toujours.

**Démarrer depuis le SSD en mode Legacy** (remplacé en 1.19.1 par `installer-ssd.ps1`) : `Outils/demarrage-ssd.ps1`, à lancer dans Windows
en administrateur. Il place le chargeur Limine au début du disque (avec sa propre petite
partition de 32 Mo) et copie le noyau sur la partition FSOS. Windows n'est pas modifié (il
démarre en UEFI), mais en mode Legacy seul File System OS démarre. `-Retirer` annule tout.

Désactivez le démarrage rapide de Windows (`powercfg /h off`) pour partager la partition
sans risque.

## Correctifs 1.16.3

- `ssd installer` ne s'arrête plus sur « environnement \FSOS introuvable dans l'OS » quand on
  démarre sur la clé : il crée directement l'environnement complet sur le SSD (Systeme,
  Applications, Documents, Images, Utilisateurs, fichiers de configuration et exemples FSL).
- `installer` tout court fait la même chose que `ssd installer`.

## Correctifs 1.16.2 — écriture sur la partition FSOS

- **Écriture refusée sur une partition formatée par Windows** : Windows range le nom du volume
  (« FSOS ») dans le répertoire racine et laisse « NO NAME » dans le secteur de démarrage. Le pilote
  ne lisait que ce dernier, donc croyait le volume étranger et le mettait en lecture seule. Il lit
  maintenant aussi la racine.
- `ssd partitions` n'affiche plus « Windows (NTFS) » pour une partition de données qui est en FAT32.
- `ssd monter sata0` sans numéro choisit la partition nommée FSOS, et évite la partition EFI.
- L'installateur ne dit plus « aucun contrôleur de stockage » quand le SSD est bien vu en AHCI.

## Nouveautés 1.16 — jeu d'icônes original

21 icônes dessinées pour File System OS, dans un style unique : **tuile arrondie en dégradé** de la
couleur de l'application, avec un **pictogramme blanc** et une ombre douce.

| Icône | Application | Icône | Application |
|---|---|---|---|
| page bleue | Éditeur | appareil photo | Capture |
| paysage | Images | console sombre | Sortie FSL |
| page rouge | Lecteur PDF | globe | Navigateur |
| page + flèche | TextWar | trois nœuds reliés | Réseau |
| barres | Gestionnaire des tâches | calculatrice | Calculatrice |
| serpent en escalier | Serpent | pile + éclair | Énergie |
| silhouette | Comptes | carton + flèche | Installation |
| « i » | À propos | cadenas | Verrouiller |
| porte + flèche | Se déconnecter | symbole marche | Alimentation |
| poubelle | Corbeille | triangle « ! » | Alerte |
| disque + coche | Sauvegarder la session | | |

Chaque icône existe en **deux tailles**, toutes deux en hexadécimal : 16x16 pour les menus et les
barres de titre, et **32x32 dessinée** pour le bureau (au lieu d'un simple agrandissement).
Chacune a sa propre palette de 16 couleurs ARGB, avec transparence pour des bords lisses.
`Icons.PaletteDe(icone)` donne la bonne palette, `Icons.Grand(icone)` la version 32x32.

Les icônes Console, Explorateur et Paramètres restent celles de Vincent Senut.

## Menu de démarrage : File System OS ou Windows (`Outils/menu-demarrage.ps1`)

Un script PowerShell installe, sur la partition EFI du SSD, un menu affiché par le chargeur
**Limine** à chaque démarrage, avec un compte à rebours de 10 secondes et le dernier choix retenu :

```
File System OS     Systeme d'exploitation de Vincent Senut
Windows            Gestionnaire de demarrage de Windows
```

### Installation (dans Windows, PowerShell en administrateur)
```
cd C:\chemin\FileSystemOS\Outils
powershell -ExecutionPolicy Bypass -File menu-demarrage.ps1
```
Le script trouve tout seul l'ISO compilée (ou `-Iso C:\...\FileSystemOS.iso`), en extrait le noyau,
télécharge Limine, sauvegarde la partition EFI dans **Documents**, copie ses fichiers dans `\EFI\fsos`
et `\fsos`, puis crée l'entrée « File System OS (menu) » en tête de la liste du BIOS.

### Sécurité
- Le dossier `\EFI\Microsoft` n'est **jamais modifié** ; l'entrée « Windows Boot Manager » reste.
- En cas de problème : **F12** au démarrage → Windows Boot Manager.
- Le script **refuse de continuer si BitLocker est actif** : un chargeur placé devant Windows
  provoquerait la demande de clé de récupération à chaque démarrage.
- Tout retirer : `menu-demarrage.ps1 -Retirer` (n'efface que ce que le script a installé).

### Réglages du BIOS (F2)
- **Boot List Option : UEFI** ;
- **Secure Boot : désactivé** (Limine n'est pas signé par Microsoft) ;
- **SATA Operation : AHCI**.

### À savoir
L'entrée Windows est sûre : Limine se contente de passer la main au chargeur de Windows.
L'entrée File System OS démarre le noyau **en mode UEFI**, alors que le Cosmos classique a été
pensé pour le BIOS (Legacy) : selon la version de Cosmos, l'écran peut rester noir, faute de
« VBE ». Dans ce cas, File System OS reste disponible par la clé USB en mode Legacy (F12).

## Nouveautés 1.15 — page de connexion et déconnexion

### Page « Connexion » plein écran (`Graphique/PageConnexion.cs`)
Elle remplace l'ancienne petite fenêtre. Elle s'affiche au **démarrage**, à la **déconnexion** et au
**verrouillage**, et couvre tout l'écran : rien d'autre n'est accessible tant qu'elle est ouverte.

- On **écrit** son nom d'utilisateur et son mot de passe (Tab pour changer de champ, Entrée pour valider).
- Le bouton **Voir / Cacher** affiche le mot de passe en clair ou le masque.
- Les comptes existants sont proposés en bas : un clic remplit le nom.
- En cas d'erreur, le message est le même que le nom soit faux ou le mot de passe : on ne révèle pas
  quels comptes existent. Chaque échec est inscrit au journal des connexions.
- Au premier démarrage, la même page sert à créer le compte administrateur.
- En bas : éditeur, auteur et heure.

### Verrouiller ou se déconnecter
| | Verrouiller (Ctrl + Alt + L) | Se déconnecter |
|---|---|---|
| Fenêtres | gardées, cachées | fermées |
| Programmes de l'utilisateur | continuent | arrêtés |
| Page de connexion | nom déjà rempli | champs vides |

« Se déconnecter » est dans le menu Démarrer, et la commande `deconnexion` existe dans la console.

### Clés USB : ce qu'il en est
Une fois l'OS lancé, le BIOS ne sert plus à rien : lire une clé demande un pilote de contrôleur
**xHCI** plus le protocole **USB Mass Storage**. Le Cosmos classique utilisé par ce projet n'en a pas,
et **aucun réglage sur la clé ne peut y remédier**. Les fichiers `UsbMassStorage` du nouveau Cosmos
(DriverKit) en sont la bonne base, mais ils dépendent de son pilote xHCI : la vraie voie vers l'USB
est de passer File System OS sur le nouveau Cosmos.

**En attendant, pour échanger des fichiers avec Windows** : une partition FAT32 sur le SSD, lue et
écrite par Windows ET par File System OS (commande `ssd`). Voir le guide pour faire démarrer Windows
en mode AHCI, afin de ne plus changer le BIOS à chaque fois.

## Nouveautés 1.14 — bureau avec icônes, clic droit et corbeille

### Le bureau (`Graphique/Bureau.cs`)
- **Icônes d'applications** : Explorateur, Console, Éditeur, Calculatrice, Serpent, Navigateur,
  Paramètres, Tâches, plus la **Corbeille** (avec une pastille indiquant son nombre d'éléments).
- **Fichiers et dossiers réels** : tout ce qui est dans `\FSOS\Utilisateurs\<nom>\Bureau` apparaît
  sur le bureau, avec l'icône de son type (texte, programme FSL, PDF, image, dossier).
- **Glisser-déposer** : une icône se déplace à la souris, s'aimante sur la grille, ne se pose jamais
  sur une autre ; sa place est enregistrée dans `bureau.cfg` et retrouvée à la connexion suivante.
- **Double-clic** : ouvre l'application, le fichier avec le bon programme, ou le dossier dans
  l'explorateur.

### Le clic droit
| Où | Menu |
|---|---|
| Sur le fond | Nouveau fichier texte, Nouveau dossier, Ouvrir la corbeille, Restaurer les icônes d'applications, Actualiser, Paramètres |
| Sur une application | Ouvrir, **Retirer l'icône du bureau** (l'application reste dans le menu Démarrer) |
| Sur un fichier ou dossier | Ouvrir, **Mettre à la corbeille** |
| Sur la corbeille | Ouvrir, Vider la corbeille |

### La corbeille (`Graphique/CorbeilleWindow.cs`)
Un élément supprimé du bureau va dans `\FSOS\Utilisateurs\<nom>\Corbeille`. La fenêtre Corbeille
permet de le **restaurer** sur le bureau, de le **supprimer définitivement**, ou de **vider** toute
la corbeille. Les dossiers sont déplacés avec tout leur contenu.

### Corrections
- **Calculatrice et Serpent transparents** : leur hauteur était fixée *après* la création du buffer
  de pixels de la fenêtre, qui était donc créé avec une hauteur de 0. Rien ne s'affichait.
  La taille est maintenant calculée avant.
- **Le Serpent avance même sans horloge matérielle** (il compte alors les tours de l'ordonnanceur).
- **Transparence des icônes** : `DrawImage` écrasait les pixels à demi transparents au lieu de les
  mélanger au fond. Les icônes Console et Paramètres (blanc à 15 niveaux d'opacité) auraient été
  des taches blanches. Le mélange alpha est maintenant fait pixel par pixel.

## Nouveautés 1.13.1 — diagnostic du stockage

- `ssd diag` : diagnostic **pas à pas**, sans rien écrire : contrôleurs vus sur le bus PCI et leur
  mode (AHCI, RAID, NVMe), ce que les pilotes ont trouvé, lecture du secteur 0, table de partitions,
  puis pour **chaque partition** le contenu brut de son secteur de démarrage et le verdict
  (montable ou non, et pourquoi). À photographier en cas de problème.
- `ssd secteur sata0 <n>` : affiche un secteur brut en hexadécimal.
- `ssd monter sata0` sans numéro : choisit **automatiquement** la première partition FAT32.
  (Sur beaucoup de Dell, la partition 1 est une partition de récupération NTFS, pas la partition EFI.)
- La détection FAT32 suit maintenant le secteur de démarrage, comme Windows et Linux, au lieu
  d'exiger 65 525 grappes : certaines partitions EFI en ont moins.

## Nouveautés 1.13 — gestion directe du SSD, et nouvelles icônes

### Pilote de fichiers FAT32 direct (`Pilotes/FatDirect.cs`)
Cosmos refuse de monter le SSD du Dell (table GPT, partitions Windows). File System OS a donc
maintenant **sa propre chaîne de pilotes**, sans Cosmos entre le disque et les fichiers :

```
Ahci / Nvme  ->  Gpt (tables de partitions)  ->  FatDirect (fichiers)  ->  commande ssd
```

`FatDirect` lit et écrit une partition FAT32 en parlant directement au pilote de disque :
table FAT avec cache et écriture dans **les deux copies**, chaînes de grappes protégées contre les
boucles, **noms longs** (jusqu'à 255 caractères, avec la somme de contrôle de Windows), noms courts
`NOMLON~1.TXT` uniques, création et suppression de fichiers et de dossiers, agrandissement
automatique des répertoires, mise à jour de FSInfo, dates et heures.

**Sécurité** :
- un volume est ouvert en **lecture seule**, sauf s'il a été formaté par File System OS ;
- la partition EFI de Windows se lit sans risque ; l'écrire demande `ecriture` explicitement ;
- après chaque écriture, le fichier est **relu et comparé octet par octet** ;
- après chaque opération, le cache interne du SSD est vidé (`FLUSH CACHE EXT`).

Les algorithmes ont été testés sur une image disque de 80 Mo : noms courts et longs, dossiers
imbriqués, fichier de 586 grappes, remplacement avec libération de l'ancienne chaîne, répertoire
agrandi à 60 fichiers, suppression puis réutilisation, deux tables FAT identiques, remontage à froid.

### Commande `ssd`
| Commande | Effet |
|---|---|
| `ssd` | aide + toutes les partitions FAT32 de tous les disques |
| `ssd monter sata0 1 [ecriture]` | ouvre la partition 1 du SSD |
| `ssd ls`, `ssd cd`, `ssd info`, `ssd libre`, `ssd disque` | parcourir, espace, fiche du SSD |
| `ssd lire` / `ssd ecrire <fichier> <texte>` | lire ou écrire un fichier texte |
| `ssd mkdir` / `ssd rm` | dossiers et suppression |
| `ssd copier <fichier OS> <fichier SSD>` / `ssd importer` | copies dans les deux sens |
| `ssd installer` | copie **tout \\FSOS** de l'OS sur le SSD, vérifié |

### Pilote AHCI amélioré
Réveil du lien SATA (**COMRESET**) quand le BIOS a laissé un port éteint, **réparation** automatique
du port après une erreur avec une seconde tentative, **vidage du cache** du SSD, modèle complet
(40 caractères au lieu de 20 : « SK hynix SC300 M.2 2280… »), numéro de série, micrologiciel,
prise en charge LBA 48 bits, compteurs de commandes, d'erreurs et de réparations.

### Nouvelles icônes
Console, Explorateur et Paramètres sont maintenant les icônes dessinées par Vincent Senut, converties
de 96x96 en 16x16 hexadécimal. Chacune a **sa propre palette** de 16 couleurs ARGB : les dégradés et
la transparence des originaux sont conservés. La fenêtre Affichage devient **Paramètres**.

## Nouveautés 1.12 — pilote d'affichage Intel, GPT et formatage FAT32

### Pilote d'affichage Intel (`Pilotes/IntelGraphics.cs`)
Pour les HD Graphics des Core i de 2ᵉ à 9ᵉ génération. Il **lit le matériel**, il ne le reprogramme pas :

| Ce qu'il lit | Comment |
|---|---|
| Prises occupées (écran interne, HDMI, DP, VGA) | registre `SDEISR` du chipset |
| Sorties allumées par le BIOS, et leur taille | `PIPECONF` / `PIPESRC` des pipes A, B, C |
| Prises réellement alimentées | `DDI_BUF_CTL` |
| **Écran branché : fabricant, modèle, taille, résolution préférée** | **EDID lu par le bus I2C du chipset (GMBUS)** |

Commande `ecrans` (ou `ecrans rapide` sans l'EDID), et bouton « Chercher les écrans » dans Affichage.

**Pourquoi il n'allume pas une sortie éteinte.** Il faudrait programmer les PLL qui fabriquent
l'horloge pixel, les tables de temps d'affichage, l'encodeur TMDS du HDMI, le plan d'image et son
adresse mémoire, puis la séquence d'allumage dans l'ordre exact. Une erreur d'un bit éteint l'écran
du portable, et cela ne peut pas s'écrire sans matériel de test. En pratique : branchez l'écran HDMI
**avant d'allumer** le PC, le BIOS allume alors les deux sorties en mode clone.

### Tables de partitions GPT (`Pilotes/Gpt.cs`)
Cosmos ne lit que les tables MBR simples. File System OS lit maintenant **MBR et GPT** : type de
chaque partition (EFI, Windows NTFS, récupération, Linux…), taille, secteur de départ et nom.
Commande `partitions`.

### Formatage FAT32 (`Systeme/Fat32.cs`)
Écrit entièrement à la main : secteur de démarrage (BPB), FSInfo, copies de secours, deux tables FAT
et répertoire racine. La taille des grappes suit les règles de Windows, donc la partition est lisible
par File System OS **et** par Windows.

Commande `formater sata0 2` : crée un FAT32 sur la partition 2 du disque gardé de côté, puis
`monterdisque sata0` pour l'utiliser. **Cela efface la partition choisie.**

## Nouveautés 1.11 — écrans HDMI et sorties vidéo

`Pilotes/Affichage.cs` détecte les **sorties vidéo**. Sur les puces Intel (HD Graphics des Core i de
2ᵉ à 9ᵉ génération, donc le Dell), il lit le registre de détection de branchement du chipset
(`SDEISR`) et indique quelles prises sont occupées : écran interne (eDP), HDMI, DisplayPort, VGA.
C'est une **lecture seule** : rien n'est programmé, l'écran en cours ne risque rien.

Menu → **Affichage** → bouton « Chercher les écrans », ou commande `ecrans`.

### Ce qui marche, et ce qui ne marche pas
- **Écran HDMI en recopie (clone)** : le BIOS envoie souvent la même image sur les deux sorties.
  Dans ce cas le second écran affiche le bureau, sans que l'OS ait rien à faire. Branchez-le
  **avant d'allumer** l'ordinateur, ou activez le mode clone dans le BIOS.
- **Second bureau indépendant** : impossible. Cosmos dessine dans **un seul cadre mémoire**, celui
  que le chargeur de démarrage a préparé (VBE). Piloter une deuxième sortie demanderait un vrai
  pilote graphique : pipes, encodeurs, horloges, lecture de l'EDID de l'écran. C'est le plus gros
  pilote d'un système d'exploitation, impossible à écrire sans pouvoir tester le matériel.

## Nouveautés 1.10.3 — montage protégé des disques

Le pilote AHCI voit le disque (exemple réel : `SK hynix SC300 M.2 2, 238 Go` sur le port 2), mais
l'OS restait bloqué sur **« montage des disques »**. La cause : Cosmos ne connaît que le **FAT**, et
un disque Windows en **GPT/NTFS** le fait tourner en rond.

Désormais, File System OS **lit lui-même la table de partitions** (`Systeme/Partitions.cs`) avant de
confier un disque à Cosmos :

| Contenu du disque | Décision |
|---|---|
| Au moins une partition FAT | confié à Cosmos, monté normalement |
| Disque vierge (pas de table) | confié : l'installateur peut le formater |
| GPT (Windows, Linux moderne) | **gardé de côté** : le démarrage continue |
| NTFS, ext4, autre | **gardé de côté** |

Le disque reste visible dans `stockage`, avec la mention « gardé de côté ». Pour forcer son montage
malgré le risque : `monterdisque` (liste) puis `monterdisque sata0`. Si l'OS se fige, le chien de
garde redémarre la machine au bout de 8 secondes.

Pour installer sur un disque Windows, il faut d'abord le formater : Installation →
« Disque N entier » → **Formater + installer**, ce qui efface tout.

## Nouveautés 1.10 — pilote AHCI (disques SATA)

`Pilotes/Ahci.cs` complète le pilote NVMe : les disques **SATA** sont maintenant lus et écrits.
Il est inspiré du pilote AHCI 1.3.1 du **nouveau** Cosmos (`Cosmos.Kernel.Drivers.Pci.Storage.Ahci`),
mais réécrit pour l'API **classique** utilisée par ce projet (`BlockDevice` + `PCIDevice`) : les deux
générations de Cosmos ont des API totalement différentes et ne se mélangent pas.

Fonctionnement : une **liste de 32 en-têtes de commande** par port, une **zone de réception** où le
disque dépose ses réponses, et une **table** contenant le FIS à envoyer plus la zone mémoire à
utiliser (PRDT). On remplit une entrée, on met son bit dans le registre `CI`, on attend qu'il retombe.
Comme pour le NVMe : attente active, aucune interruption, une page de 4 Ko par transfert.
`IDENTIFY` donne le modèle et la capacité (LBA 48 bits).

**À retenir** : il ne marche qu'avec le BIOS en **AHCI**. En « RAID On », le contrôleur se présente
en sous-classe 04 et le pilote le signale explicitement au lieu d'essayer.

## Nouveautés 1.9 — calculatrice, jeu, double tampon, sentinelle, énergie

### Ce qui n'est pas possible : le Wi-Fi
Aucune box ne peut être listée, et ce n'est pas une question d'effort. Il faudrait **quatre choses** :
le pilote propre à la puce (Intel 8260, Realtek RTL8822, Qualcomm… aucun ne ressemble à un autre),
le **micrologiciel fermé** du fabricant qu'on n'a pas le droit de redistribuer, toute la pile 802.11,
et le chiffrement **WPA2/WPA3**. C'est le morceau le plus lourd d'un système d'exploitation, et il ne
peut pas être écrit sans matériel de test. La fenêtre Réseau et la commande `wifi` affichent la carte
détectée et expliquent ce qui manque. **Par câble Ethernet, aucun mot de passe n'est nécessaire.**

### Batterie : ce qui est affichable
Le **pourcentage de charge** se lit par l'ACPI, qui exige un interpréteur du langage AML (les tables
de la carte mère sont de petits programmes à exécuter). Cosmos n'en a pas. La fenêtre **Énergie**
affiche donc ce qui est réellement accessible : durée de fonctionnement, **charge du système** avec
jauge, type de machine, contrôleur SMBus détecté — et dit clairement que le niveau n'est pas lisible.

### Sentinelle : les programmes corrompus n'abattent plus l'OS
`Systeme/Sentinelle.cs` agit à trois niveaux :
1. **avant** l'exécution, le bytecode est vérifié (instructions connues, sauts dans les bornes,
   numéros de variables et de textes valides) ; un fichier abîmé est **refusé** ;
2. **pendant**, les erreurs de chaque thread sont comptées : au-delà de 5, le processus est arrêté ;
3. une fenêtre **« Programme corrompu »** s'affiche avec la raison, et l'incident est journalisé.

Commande `sentinelle` pour la liste des incidents.

### Double tampon et mémoire dédiée
`Graphique/DoubleBuffer.cs` : deux images, une affichée et une en cours de dessin, échangées d'un coup
(`Basculer`). Plus de scintillement. La mémoire est réservée en **pages attribuées au processus** du
programme, rendues automatiquement à sa fermeture.

### Calculatrice et jeu du Serpent
- **Calculatrice** : quatre opérations, pourcentage, racine carrée (méthode de Newton), mémoire
  (M+, MR, MC), clavier et souris. Tout est calculé en **virgule fixe** (entiers × 1000), car Cosmos
  n'a pas de calcul flottant fiable.
- **Serpent** : la démonstration du double tampon. Le jeu a **son propre processus et son propre
  thread**, dessine dans l'image arrière puis bascule. Le compteur d'images s'affiche en haut.

## Nouveautés 1.8 — captures d'écran, touche Windows, gestionnaire des tâches

### Captures d'écran (touche Impr. écran)
L'image affichée est déjà en mémoire dans le buffer « écran » : `Graphique/Capture.cs` l'enregistre
en **BMP 24 bits**, format que l'application Images sait relire.

| Raccourci | Effet |
|---|---|
| **Impr. écran** | capture enregistrée, message en bas de l'écran pendant 3 s |
| **Maj + Impr. écran** | capture puis ouverture dans Images |
| `capture` / `capture ouvrir` | la même chose depuis la console |
| `captures` | liste les captures du dossier |

Les fichiers vont dans `\FSOS\Utilisateurs\<nom>\Captures`, nommés `ecran-20261009-213145.bmp`.

### Touche Windows
Elle ouvre et ferme le **menu Démarrer**. Si ta version de Cosmos ne la reconnaît pas,
**Ctrl + Alt + M** fait la même chose.

### Ctrl + Alt + Suppr : gestionnaire des tâches
Une fenêtre à la Windows (`Graphique/TacheWindow.cs`) : chaque processus avec son **PID**, son
**utilisateur**, son **état**, son **temps processeur**, ses **pages** et ses **threads**, rafraîchis
chaque seconde. En haut, le nombre de processus, les images par seconde, les commutations de contexte
et les pages utilisées.

Boutons : **Arrêter** (un processus système ou celui d'un autre utilisateur est refusé),
**En attente** (gèle ou reprend un processus), **Actualiser**, **Verrouiller**, **Redémarrer**.
Au clavier : flèches pour choisir, **Suppr** pour arrêter, **F5** pour actualiser.

### Autres raccourcis
**Ctrl + Alt + L** verrouille la session, **F12** ouvre le panneau de diagnostic.

## Nouveautés 1.7 — redémarrage fiable et reprise automatique

### Le redémarrage ne reste plus bloqué
La méthode ACPI de Cosmos ne fait rien sur beaucoup de machines réelles : l'écran restait figé sur
« Redémarrage en cours ». `Systeme/Redemarrage.cs` essaie maintenant **quatre méthodes** de suite :

1. le **contrôleur clavier 8042** (port `0x64`, commande `0xFE`) — la méthode des PC depuis 1984 ;
2. le **registre de remise à zéro** des chipsets Intel (port `0xCF9`) ;
3. l'**ACPI** de Cosmos ;
4. de nouvelles impulsions en boucle, car certaines cartes mères ne répondent qu'après plusieurs essais.

Si vraiment rien ne marche, l'OS l'écrit à l'écran avec la dernière méthode essayée, au lieu de rester
figé sans rien dire. L'arrêt suit le même principe, avec les méthodes propres aux machines virtuelles.

### Redémarrage automatique après un blocage
Le chien de garde affiche le bandeau rouge après **3 secondes** sans réponse, puis **compte à rebours
5 secondes** et **redémarre la machine lui-même**, sans rien attendre du système bloqué.
L'événement est inscrit au journal des connexions.

Réglages : `chiendegarde 10` (délai en secondes), `chiendegarde non` (désactiver).
Commande `reboot` pour un redémarrage matériel immédiat, sans écran d'arrêt.

## Nouveautés 1.6

### Journal des connexions
`Systeme/Journal.cs` enregistre chaque **démarrage, connexion, échec de mot de passe, verrouillage,
déverrouillage, arrêt et redémarrage**, avec la date, l'heure, l'utilisateur, la machine reconnue et
le **support de démarrage** (disque interne, ou clé USB / ISO en mode Live).

Il est écrit dans `\FSOS\Systeme\journal-connexions.txt` dès que l'OS est installé, sur l'ordinateur
comme sur la clé, et relu au démarrage suivant pour garder l'historique. En mode Live sans disque,
il reste en mémoire pour la session.

Commande `journal [n]` (ou `connexions`), et les trois dernières lignes s'affichent dans la fenêtre **Comptes**.

 — multiprogrammation : tranches de temps, états, IPC, interblocages

Chaque programme s'exécute à son tour, par **tranches de temps**, et l'OS mesure ce que chacun consomme.

### Tranches de temps et commutations de contexte
Une horloge matérielle compte les millisecondes (`Processus/Horloge.cs`). À chaque fois qu'un thread
reçoit le processeur, c'est une **commutation de contexte** : l'OS note le temps consommé par le
thread et par son processus. Un thread qui dépasse souvent sa tranche (8 ms par défaut) voit son
processus **passer en priorité basse**, pour que les autres continuent de répondre.
Commande `quantum [ms]` pour changer la tranche.

### États des processus
`nouveau → prêt → élu → prêt …`, plus `bloqué` (il attend un verrou), `gelé` (buffer d'attente)
et `terminé`. La commande `ps` affiche pour chaque processus son **utilisateur**, son **état**,
sa **priorité**, son **temps processeur**, ses **pages** et ses threads — avec, pour chaque thread,
sa pire tranche et ses dépassements.

### Multi-utilisateur
Chaque processus appartient au **compte connecté** qui l'a lancé. `kill` refuse de tuer le
processus d'un autre utilisateur, sauf pour un administrateur. Les processus système sont protégés.

### Communication entre processus (IPC)
Une **boîte aux lettres** par processus (`Processus/Ipc.cs`) : `envoyer <processus> <texte>` dépose
un message, `ipc` affiche l'état des boîtes. Quand un processus meurt, sa boîte disparaît.

### Verrous et interblocages
`Processus/Verrou.cs` fournit l'exclusion mutuelle. Avant d'accorder une attente, l'OS suit la chaîne
« qui attend qui » : si elle revient au demandeur, c'est un **interblocage**, et la demande est
**refusée** au lieu de figer les deux processus. Un processus qui meurt rend automatiquement ses
verrous. Commandes `verrous`, et `demoverrou` qui met en scène deux processus et deux verrous croisés.

## Nouveautés 1.5 — mémoire paginée

`Memoire/Pages.cs` découpe une réserve en **pages de 4 Ko**. Chaque page attribuée porte :

| Information | Exemple |
|---|---|
| **Programme** (à quoi elle sert) | `Compteur.fsl (code)` |
| **Processus** propriétaire | `PID 14 fsl-compteur` |
| **Thread** propriétaire (facultatif) | `vm` |
| **Attributs** | `r-x`, `rw-`, `noyau`, `partagée`, `verrouillée` |

- `Attr.Code` = lecture + exécution, `Attr.Donnees` = lecture + écriture, `Attr.Systeme` = noyau.
- `Pages.Ecrire()` **refuse** d'écrire dans une page de code : les droits sont vérifiés par l'OS.
- Quand un processus se termine, **toutes ses pages reviennent automatiquement** au système
  (`Process.Kill()` → `Pages.LibererProcessus`). Un thread qui meurt rend ses pages propres.
  Un programme ne peut donc plus faire fuir de la mémoire.
- Les programmes FSL en profitent : chaque programme reçoit ses pages de code (r-x), une page de
  données (rw-) et une page de pile pour son thread. La fenêtre Sortie FSL l'affiche au lancement
  et indique les pages rendues à la fin.

Commandes : `pages` (résumé par processus), `pages detail` (page par page : adresse, droits,
processus, thread, programme). `ps` affiche le nombre de pages de chaque processus, et le panneau
**F12** reprend l'état. Réglage `pages_memoire=512` dans `demarrage.cfg` (512 pages = 2 Mo).

C'est une pagination **logicielle** : Cosmos garde la table de pages du processeur en
correspondance directe, et y toucher casserait le système. Les attributs sont donc appliqués
par l'OS, pas par la MMU.

## Clés USB et installation sur NVMe

### Lire/écrire une clé USB : ce qui bloque
Pour voir une clé USB, il faut **deux pilotes entiers** : le contrôleur (**xHCI** sur les PC récents,
comme le `8086:9D2F` du Dell) et le protocole **stockage de masse USB**. C'est un travail de plusieurs
mois, impossible à écrire sans pouvoir tester le matériel. Ce n'est donc pas disponible, et
promettre le contraire ne servirait à rien. La commande `stockage` liste les contrôleurs USB vus
et le rappelle.

**Ce qui marche à la place :** le disque interne (NVMe ou SATA) pour les fichiers, la « clé
virtuelle » (`creer-cle-virtuelle.sh`) en machine virtuelle, et le téléchargement par le navigateur
ou TextWar une fois le réseau actif.

### Installer sur un disque NVMe
L'installateur affiche désormais la nature de chaque disque (NVMe, SATA/IDE, mémoire).

1. BIOS → **SATA Operation = AHCI** (sinon le NVMe reste caché par Intel RST).
2. **Les fichiers**, depuis File System OS : Menu → Installation → choisir le disque NVMe →
   « Formater + installer ». Le disque est formaté en FAT32 et reçoit `\FSOS`.
3. **Le démarrage** : l'OS ne peut pas écrire son propre chargeur sur un disque qu'il n'a pas
   démarré. Depuis une clé Ubuntu live :
   ```bash
   lsblk                       # repérer le disque, ex : /dev/nvme0n1
   sudo ./creer-disque.sh FileSystemOS.iso --vers /dev/nvme0n1
   ```
   Le script efface le disque, y écrit le chargeur Limine, le noyau et `\FSOS`. Le PC démarre
   ensuite directement sur File System OS, sans clé ni ISO.

## Nouveautés 1.4 — disques NVMe (SSD nouvelle génération)

Cosmos ne lit que l'IDE et l'AHCI : les SSD **NVMe** (M.2 / PCIe) des portables récents étaient
totalement invisibles. `Pilotes/Nvme.cs` est un **pilote NVMe écrit pour File System OS**.

Comment ça marche : NVMe échange par **files d'attente** en mémoire. L'OS écrit une commande de
64 octets dans la file d'envoi, prévient le disque par une « sonnette » (doorbell), puis lit la
réponse dans la file de retour. Le pilote crée une file d'administration (identification, capacité)
et une file de lecture/écriture, puis se présente au système comme un disque normal : l'explorateur,
l'éditeur et l'installateur l'utilisent sans rien savoir du NVMe.

Choix volontaires : **une page de 4 Ko par commande** et attente active, sans interruption.
C'est plus lent qu'un vrai pilote, mais simple et prévisible.

### Important : le mode SATA du BIOS
Même avec un disque NVMe, le réglage **RAID On** (fréquent chez Dell) le **cache derrière le
contrôleur Intel RST** : il n'apparaît pas sur le bus PCI et aucun pilote ne peut le voir.
→ BIOS (F2) → **SATA Operation = AHCI**. Le NVMe devient alors un périphérique PCI visible
(classe 01, sous-classe 08) et le pilote le démarre au boot.

### Attention aux données
Un disque Windows est en **GPT/NTFS** : File System OS ne lit que le FAT, donc il ne verra
que la petite partition EFI. N'installez rien dessus tant que vous voulez garder Windows.
Les commandes `stockage` et l'installateur indiquent ce qui est vu et pourquoi.

## Nouveautés 1.3 — Dell / portables réels

### Carte Ethernet Intel I217 / I218 / I219 (pilote expérimental)
Ces puces de portables utilisent **les mêmes registres** que l'E1000 pour émettre et recevoir,
mais leur PHY appartient au micrologiciel de la carte mère : une remise à zéro bloquerait la machine.
Le pilote les gère donc **sans reset et sans EEPROM** (adresse MAC lue dans les registres RAL/RAH).

C'est expérimental, donc **désactivé par défaut** : Menu → Réseau → bouton rouge
**« Essayer le pilote I219 »**, câble Ethernet branché sur la box. Le choix est enregistré
(`pilote_i219=oui` dans `reseau.cfg`). En cas de blocage, il suffit de redémarrer.

### Disque invisible sur un vrai PC : le mode SATA du BIOS
Cosmos lit les disques en **IDE** et en **AHCI**, mais pas en **RAID**. Or beaucoup de Dell sortent
d'usine en « RAID On », ce qui rend le disque totalement invisible.

→ BIOS (F2) → **SATA Operation = AHCI**.

L'installateur et la commande `stockage` affichent maintenant les contrôleurs trouvés, leur mode
(IDE, AHCI, RAID, NVMe) et le conseil correspondant. Les disques **NVMe** ne sont pas gérés par Cosmos :
il faut un disque SATA.

## Nouveautés 1.2 — reconnaissance de l'ordinateur et pilotes réseau sûrs

### Un pilote dépend de la PUCE, pas de la marque
Il n'existe pas de « pilote Dell » ou de « pilote HP » : deux Dell peuvent avoir deux puces réseau
différentes, et un Dell et un HP peuvent avoir la même. `Pilotes/PiloteReseau.cs` associe donc chaque
**puce connue** à son pilote.

### Reconnaissance de la machine (`Pilotes/Machine.cs`, commande `machine`)
- **Marque** : déduite du *subsystem vendor* inscrit dans chaque puce soudée
  (Dell 1028, HP 103C, ASUS 1043, Lenovo 17AA, Acer 1025, MSI 1462…).
- **Processeur** : nom complet, fabricant, et génération Intel Core (i5-6300U → 6ᵉ).
- **Carte mère** : chipset lu sur le pont hôte PCI. **Carte graphique** : modèle.
- **Profil** : combien de cartes réseau sont pilotables, sans pilote, ou Wi-Fi.

### Sécurité : plus de plantage au démarrage
Sur une machine physique, **seules les puces testées sont démarrées**. Une puce inconnue n'est plus
touchée du tout — c'est ce qui faisait planter l'OS. Réglage `pilotes_reseau=auto|tous|aucun`
dans `reseau.cfg`.

| Puce | Pilote | État |
|---|---|---|
| AMD PCnet (VirtualBox) | Cosmos | prêt |
| Realtek RTL8139 | Cosmos | prêt |
| Intel E1000 / 82540EM, 82545EM, 82574L | File System OS | prêt |
| Intel I219, I211, I225 (portables récents) | e1000e / igc | à écrire |
| Realtek RTL8111/8168 | r8169 | à écrire |
| Broadcom, Qualcomm Atheros | tg3 / alx | à écrire |
| Toutes les puces Wi-Fi | — | impossible (micrologiciel fermé + WPA2) |

## Nouveautés 1.1

### Lire et enregistrer des fichiers partout
- L'**éditeur** ouvre n'importe quel fichier texte ou programme (`.txt`, `.fsl`, `.cfg`, `.csv`, `.htm`…).
- **Enregistrer sous** (bouton « Sous ») : tapez le chemin complet sur **n'importe quel disque** — `0:\` disque dur,
  `1:\` deuxième disque, clé virtuelle… Les dossiers manquants sont créés. Bouton **+** ou Ctrl+N : nouveau document.
- Un document sans nom propose « Enregistrer sous » au premier Ctrl+S (par défaut dans vos Documents).
- **Clé USB réelle : toujours impossible** — Cosmos n'a pas de pilote USB de stockage (contrôleurs xHCI/EHCI).

### Installer l'OS depuis un disque dur (installation complète)
Si File System OS a démarré **depuis un disque** créé par `creer-disque.sh` (disque virtuel ou 2e disque),
l'installateur propose en bleu « **Disque N : installation COMPLETE (démarrable)** ». Il recopie secteur par
secteur le disque de démarrage (chargeur Limine + noyau + `\FSOS`) vers le disque choisi, 64 Ko par tour.
Le disque copié démarre ensuite **tout seul** : plus d'ISO, de clé ni de Linux.
Exemple VirtualBox : disque 1 = `FileSystemOS-disque.vdi`, disque 2 = disque vide → installation complète
sur le disque 2 → retirer le disque 1 → la VM démarre sur le disque 2.
(Si l'OS a démarré sur l'ISO ou une clé USB, cette option n'apparaît pas : le support de démarrage est invisible.)

### Réseau : toutes les cartes identifiées
- Menu → **Réseau** (ou `reseau`, `peripheriques`) : chaque carte réseau du bus PCI est identifiée
  (fabricant, modèle, Ethernet ou Wi-Fi, identifiant, emplacement) avec l'état de son pilote, ainsi que
  **tous les autres périphériques** (stockage, USB, audio, graphique…).
- Pilotes : AMD PCnet et Realtek RTL8139 (Cosmos), et **nouveau pilote Intel E1000** écrit pour File System OS
  (`Pilotes/E1000.cs`) : carte par défaut de VirtualBox (« Intel PRO/1000 MT Desktop »), VMware et QEMU.
- Bouton **Se connecter** (ou `connecter`) : demande l'adresse à la box par DHCP. **Par câble, aucun mot de passe.**
- **Wi-Fi : détecté et nommé, mais pas utilisable.** Chaque puce demande son propre pilote, souvent un
  micrologiciel fermé du fabricant (Intel), et le chiffrement WPA2. Branchez un câble Ethernet sur la box.
- Cartes Intel récentes des portables (I219, e1000e) et Realtek Gigabit (RTL8111) : détectées, pas encore de pilote.

## 1. Installer File System OS (démarrage SANS l'ISO ni la clé USB)

L'outil `Outils/creer-disque.sh` fabrique un **disque complet et démarrable** :
partition FAT32, noyau, chargeur de démarrage **Limine**, et l'environnement `\FSOS`
déjà installé (applications FSL, documents). Une fois ce disque en place, l'ISO et la clé
USB ne servent plus à rien.

Il tourne sous **Linux** ou sous **Windows avec WSL** (Ubuntu) :

```bash
sudo apt install fdisk dosfstools mtools git build-essential p7zip-full python3 qemu-utils
cd FileSystemOS/Outils
./creer-disque.sh /chemin/vers/FileSystemOS.iso
```

Il produit :

| Fichier | Pour |
|---|---|
| `FileSystemOS-disque.vdi` | VirtualBox |
| `FileSystemOS-disque.vmdk` | VMware |
| `FileSystemOS-disque.img` | QEMU, ou copie sur un vrai disque |

Option : `--taille 2048` pour un disque de 2 Go (1 Go par défaut).

### a) VirtualBox
1. Nouvelle VM : type *Other / Other/Unknown*, 512 Mo de RAM.
2. **Stockage → Contrôleur IDE** : ajouter `FileSystemOS-disque.vdi` comme disque dur.
3. **Retirer l'ISO** du lecteur CD (ou supprimer le lecteur CD).
4. Système : ordre d'amorçage **Disque dur** en premier. Souris **PS/2**, écran **VBoxVGA**.
5. Démarrer : Limine affiche « File System OS » puis le système démarre depuis le disque.

### b) VMware
Créer une VM *Other 32-bit*, puis « Utiliser un disque virtuel existant » → `FileSystemOS-disque.vmdk`
(contrôleur **IDE**). Pas d'ISO nécessaire.

### c) QEMU
```bash
qemu-system-i386 -m 512 -hda FileSystemOS-disque.img
```

### d) Sur le disque interne d'un vrai ordinateur (avec une clé USB)
Cosmos n'a pas de pilote USB : File System OS lui-même ne peut pas lire la clé.
On utilise donc une **clé USB Linux « live »** (Ubuntu par exemple) pour copier le disque :

1. Démarrer le PC sur la clé Ubuntu (« Essayer Ubuntu »).
2. Copier le dossier `FileSystemOS` et l'ISO sur la clé ou les télécharger, puis :
   ```bash
   lsblk                                   # repérer le disque interne, ex : /dev/sda
   sudo ./creer-disque.sh FileSystemOS.iso --vers /dev/sda
   ```
   Il faut taper **OUI** : le disque interne est **entièrement effacé**.
3. Retirer la clé USB et redémarrer : le PC démarre directement sur File System OS.

> Le PC doit démarrer en mode **BIOS / Legacy (CSM)**, et le **Secure Boot** doit être désactivé.
> Le disque interne doit être en mode **IDE / compatible** (ou AHCI si ta version de Cosmos le gère).

### Le nom du projet

Dans le fichier `.csproj` du projet Cosmos (nommé **FileSystemOS**), ajoute :

```xml
<PropertyGroup>
  <AssemblyName>FileSystemOS</AssemblyName>
  <Product>File System OS</Product>
  <Company>File System OS Company</Company>
  <Authors>Vincent Senut</Authors>
  <Version>1.16</Version>
</PropertyGroup>
```

L'ISO produite s'appellera alors `FileSystemOS.iso`.

## 2. Installateur intégré (depuis File System OS)

Menu → **Installer File System OS**. Il sert à (ré)installer ou réparer l'environnement `\FSOS`
sur un lecteur, à vider un lecteur, ou à formater un disque entier. Il ne rend pas le disque
démarrable à lui seul : pour cela, utilise `creer-disque.sh` (section 1).

### Options de l'installateur

- **Lecteur existant + « Installer »** : ajoute `\FSOS` sans rien effacer.
- **Lecteur existant + case « Supprimer tous les dossiers et fichiers »** : vide entièrement le lecteur
  (suppression par lots de 25 éléments, l'OS reste fluide), puis installe.
- **« Disque N entier »** : efface la table de partitions, recrée une partition, la formate en FAT32, puis installe.
  Fonctionne sur un disque vierge comme sur un disque déjà utilisé.

Chaque action destructrice demande une confirmation (« Confirmer ? »).

### Si l'installation échoue dans VirtualBox

Erreur **« failed to find an unallocated directory entry »** : le pilote FAT32 de Cosmos
n'arrive pas à écrire sur ce disque (formatage incompatible ou disque trop grand).
La solution la plus fiable : un disque **VHD de 1 Go formaté en FAT32 par Windows**.

Dans un invite de commandes Windows **administrateur**, tape `diskpart`, puis :

```
create vdisk file="C:\VMs\fsos.vhd" maximum=1024 type=fixed
select vdisk file="C:\VMs\fsos.vhd"
attach vdisk
convert mbr
create partition primary
format fs=fat32 quick label=FSOS
detach vdisk
exit
```

Dans VirtualBox : **Configuration → Stockage → Contrôleur IDE** → retire les autres disques durs,
ajoute `fsos.vhd`, garde l'ISO dans le lecteur CD. Démarre, choisis **« 0:\ … pret »**,
**décoche** « Supprimer tous les dossiers… », puis **Installer**.

## 3. Démarrage de l'OS

Au lancement, le noyau prépare seulement l'écran, la souris et l'ordonnanceur, puis lance le
processus **demarrage**. Il affiche le **logo** — un V invisible dont on ne voit que des points
gris qui glissent le long de son tracé — et charge les modules un par un, chacun dans son fichier
(`Demarrage/Modules/`) et avec ses propres processus :

| # | Module | Processus créés |
|---|---|---|
| 1 | Ouverture du noyau | `noyau` (thread `gardien` : mesure tours/s et images/s) |
| 2 | Mémoire et accélérateur | — |
| 3 | Système de fichiers | — |
| 4 | Fichiers système internes | — (lit `\FSOS\Systeme\*.cfg`) |
| 5 | Accélération (caches) | — (police et icônes précalculées) |
| 6 | Horloge | `horloge` |
| 7 | Réseau | `reseau` (DHCP) |
| 8 | Cogestion des threads | `cogestion` (thread `rendus`) |
| 9 | Interface graphique | `affichage`, `interface`, `attente` |
| 10 | Clavier et souris | `systeme` |
| 11 | Console et commandes | — |
| 12 | Menu démarrer | — (applications FSL) |
| 13 | Extinction et redémarrage | `alimentation` |
| 14 | Applications | une fenêtre = un processus |

Commande `demarrage` dans la console : journal complet du chargement.
L'arrêt et le redémarrage passent par le processus `alimentation`, qui affiche le même logo.

### Cogestion threads / processus / buffers

Les fenêtres gardent leur processus et leur thread, mais ne dessinent plus elles-mêmes :
elles déposent une demande dans le **buffer de cogestion**. Le thread `rendus` en traite au plus
`rendus_par_tour` par tour (la fenêtre active d'abord). Les processus ont une **priorité** :
haute et normale à chaque tour, basse un tour sur quatre. Commandes `ps` et `perf`.

### Fichiers internes (`\FSOS\Systeme`)

| Fichier | Réglages |
|---|---|
| `demarrage.cfg` | `duree_etape` (images par étape ; 1 = démarrage le plus rapide) |
| `interface.cfg` | `attente_auto`, `fenetres_actives_max`, `rendus_par_tour` |
| `console.cfg` | `message_accueil` |
| `reseau.cfg` | `dhcp` |
| `horloge.cfg` | `decalage_heures` (ex : 2 en France l'été si la VM est en UTC) |

Recréés automatiquement s'ils manquent. Commande `config` pour les afficher.
Réseau dans VirtualBox : carte **PCnet-FAST III** en mode NAT.

## 4. Écran, images et fichiers externes

### Résolution adaptée à chaque ordinateur
Au démarrage, `Systeme/Materiel.cs` détecte le **processeur**, la **mémoire**, la **carte graphique**
et la **liste des résolutions** qu'elle accepte, puis choisit une résolution sûre :
1. celle demandée dans `demarrage.cfg` (`resolution=1280x720`), si l'écran la gère ;
2. sinon, en mode `auto` : 1280x720, 1366x768, 1280x800, 1024x768 puis 800x600,
   la première acceptée par la carte **et** compatible avec la mémoire disponible ;
3. sinon un repli de sécurité (1024x768, 800x600, 640x480), puis le mode par défaut du pilote.

Si un mode échoue, le suivant est essayé : plus d'écran noir au démarrage.
La fiche est écrite dans `\FSOS\Systeme\materiel.txt`.
Menu → **Affichage** pour changer la résolution (appliquée au redémarrage), ou `resolution 1280x720`.
Commande `materiel` pour voir la détection.

### Écran rayé / lignes en biais sur un vrai PC
Sur un vrai ordinateur, c'est le BIOS (VBE) qui règle l'écran au démarrage ; Cosmos ne peut pas le changer
ensuite. Si l'OS change quand même la résolution, la carte reste dans son mode d'origine et l'image part
en diagonale (rayures vertes / bleues, texte rouge illisible).
- Depuis la version 1.0.1, sur un vrai PC l'OS **garde le mode d'origine** (voir `Systeme/Materiel.cs`).
- Dans le `.csproj`, demander un mode sûr au démarrage :
  ```xml
  <CompileVBEMultiboot>True</CompileVBEMultiboot>
  <VBEResolution>1024x768x32</VBEResolution>
  ```
- BIOS (ex. Dell) : Boot List Option = **Legacy**, **Enable Legacy Option ROMs** coché, Secure Boot désactivé.
- Pour forcer malgré tout une résolution : `resolution=force:1024x768` dans `demarrage.cfg`.

### Images et fond d'écran
- Format lu : **BMP 24 ou 32 bits** (Paint → « Enregistrer sous » → BMP 24 bits).
- Double-clic sur un `.bmp` dans l'explorateur → visionneuse **Images** → bouton **Fond d'écran**.
- Menu → Affichage → **Retirer le fond** pour revenir au dégradé. Commande `fond`.
- `creer-disque.sh` installe un fond par défaut : `\FSOS\Images\fond.bmp`.

### Clé USB : ce qui est possible
Cosmos **n'a pas de pilote USB de stockage** : brancher une vraie clé USB sur un PC où tourne
File System OS ne la fera pas apparaître. Ce n'est pas réglable dans le code de l'OS
(il faudrait écrire tout un pilote USB : contrôleurs xHCI/EHCI + stockage de masse).

En attendant, deux solutions :
- **Machine virtuelle** : `Outils/creer-cle-virtuelle.sh mes_fichiers/` crée une « clé virtuelle »
  FAT32 (`cle.vdi` / `cle.vmdk`). Branche-la comme **2e disque IDE** : elle apparaît dans l'explorateur
  (ex : `1:\`). Les `.png`/`.jpg` sont convertis en `.bmp` automatiquement. Les `.txt` s'ouvrent
  dans l'éditeur, les `.bmp` dans Images.
- **Vrai PC** : un 2e disque dur (ou une carte SD sur un lecteur interne SATA/IDE) formaté en FAT32
  est reconnu de la même façon.

### À propos
Menu → **À propos** (ou commande `apropos`) : logo animé, version, éditeur,
**créateur : Vincent Senut**, et les mentions « Copie interdite de l'OS » et
« OS Open Source : veuillez mentionner la version et l'OS en cas de toute modification ».

## 5. Mode Live, comptes et navigateur

### Mode Live (sans installation)
Démarre simplement sur l'ISO ou la clé USB : si File System OS n'est installé sur aucun disque,
il passe en **mode Live**. Un **disque en mémoire** (64 Mo, FAT16, créé par `Systeme/RamDisk.cs`)
reçoit tout l'environnement `\FSOS` : on peut créer des textes, des images, des comptes,
lancer des programmes FSL… sans rien installer. La barre des tâches affiche « LIVE ».

**Où sont gardés les fichiers ?**
- S'il existe un disque FAT32 (disque virtuel de la VM, 2e disque, clé virtuelle…), la session y est
  **sauvegardée** dans `\FSOS-Live` à chaque arrêt (et via Menu → *Sauvegarder la session*, ou `sauver`),
  puis **restaurée** au démarrage suivant.
- **Sur la clé USB elle-même : impossible.** Une fois l'OS lancé, Cosmos n'a pas de pilote USB et ne voit
  plus la clé qui l'a démarré. Sans autre disque FAT32, les fichiers du mode Live sont perdus à l'arrêt.

### Comptes et mots de passe
- Premier démarrage : création du **compte administrateur**. Ensuite, chaque démarrage (et Menu → *Verrouiller*)
  affiche l'**écran de connexion** : rien n'est accessible avant.
- Menu → **Comptes** : créer / supprimer des comptes (administrateur), changer son mot de passe.
- Fichier interne `\FSOS\Systeme\comptes.cfg` : seule l'**empreinte SHA-256** (avec sel) du mot de passe
  est stockée, jamais le mot de passe. Chaque compte a un dossier `\FSOS\Utilisateurs\<nom>`.
- Limite : le disque n'est pas chiffré ; quelqu'un qui lit le disque depuis un autre système voit les fichiers.

### Navigateur internet
Menu → **Navigateur internet** (ou `web <adresse ou recherche>`).
- Navigateur **texte** : titres, paragraphes, listes, liens cliquables, retour, actualiser, accueil.
- **HTTP uniquement** : Cosmos n'a pas de chiffrement TLS. Les adresses `https://` et les recherches passent
  automatiquement par **FrogFind** (frogfind.com), un service qui simplifie les pages modernes pour les vieux
  navigateurs et les renvoie en HTTP.
- Pas de JavaScript, pas de CSS, pas d'images, pas de connexion à des comptes en ligne.
- Réseau requis : carte **PCnet-FAST III** en NAT dans VirtualBox. Pendant le chargement d'une page,
  le système est brièvement figé (la pile réseau de Cosmos est bloquante).

### Téléchargements, PDF et TextWar
- **Navigateur** : un lien vers un fichier qui n'est pas une page (PDF, image PNG/BMP/JPG, fichier…) est
  **téléchargé** dans `\FSOS\Utilisateurs\<nom>\Telechargements`, puis ouvert avec le bon logiciel.
  Les `[image]` des pages sont des liens : un clic télécharge l'image (si elle est servie en http).
- **Lecteur PDF** : affiche le **texte** de chaque page (pas la mise en page ni les images), navigation
  page par page, bouton « Enregistrer en .txt ». Il décompresse les PDF lui-même (`Utils/Inflate.cs`,
  DEFLATE écrit à la main) et lit les tables ToUnicode des polices. Les PDF scannés (images) n'ont pas de texte.
- **Images** : PNG (toutes variantes non entrelacées) et BMP. Le JPEG est téléchargé mais pas encore affiché.
- **TextWar** : télécharge des **documents texte**. Donne une adresse (http, https) ou des mots-clés :
  page web, fichier `.txt` ou même PDF sont convertis en texte propre (78 colonnes) et enregistrés en `.txt`.
  La liste des documents téléchargés s'ouvre d'un clic dans l'éditeur.
- Limite commune : les fichiers **https** ne peuvent pas être téléchargés directement (pas de TLS dans Cosmos) ;
  les **pages** https passent par FrogFind, mais pas les images ni les PDF.

## 6. Interface : fenêtres, attente et suppression

- Toutes les icônes sont écrites en hexadécimal : boutons de titre (attente ‖, réduire —, fermer ✕),
  icône de chaque fenêtre (titre, barre des tâches, menu) et boutons des barres d'outils.
- **Barre du haut « En attente »** : le bouton ‖ d'une fenêtre l'y épingle. Son processus est
  **gelé** (l'ordonnanceur ne l'exécute plus) et elle n'est plus composée à l'écran.
  Clic sur la puce = reprendre. Boutons à droite : tout mettre en attente / tout reprendre.
- **Anti-saturation** : au-delà de 4 fenêtres visibles, la plus ancienne (jamais celle qui a le focus)
  part automatiquement en attente (`WaitBuffer.AutoBalance`, réglable).
- **Explorateur** : bouton corbeille ou touche **Suppr** pour supprimer un fichier ou un dossier
  (avec tout son contenu), après confirmation (Entrée = oui, Échap = non).

## 7. Applications externes

Tout fichier `.fsl` (ou `.fsc` compilé) placé dans `\FSOS\Applications`
apparaît automatiquement dans le menu (« App : nom ») et s'y lance dans son propre processus.

## 8. File Syst Langage (FSL)

```
// Commentaire
int x = 5;            // entier
texte nom = "Bob";    // texte
int a = 0;            // le nombre zéro
int b = 0.nul;        // variable NULLE (s'écrit aussi 0/nul)
int c;                // sans valeur : nulle aussi

x = x + 1;
afficher("x = " + x);      // affiche aussi les nulles : "0/nul"

si (b == 0/nul) {
    afficher("b est nulle");
} sinon si (x > 3) {
    afficher("grand");
} sinon {
    afficher("petit");
}

tantque (x < 10) {
    x = x + 1;
}
```

- Opérateurs : `+ - * / %`, `== != < > <= >=`, `&& || !`, `vrai`, `faux`.
- `+` avec un texte fait une concaténation.
- Calculer avec une variable nulle provoque une erreur claire : *calcul avec une variable nulle (0/nul)*.
- Les blocs `{ }` ne créent pas de nouvelle portée : toutes les variables sont globales.

### Compilation

FSL est **compilé en bytecode** pour une machine virtuelle à pile :

- Éditeur → **Compiler** (ou `fslc fichier.fsl` dans la console) produit `fichier.fsc`.
- **Exécuter** (F5) compile en mémoire puis lance le programme.
- Chaque programme tourne dans son propre processus (`fsl-nom`), 300 instructions par tour :
  une boucle infinie ne bloque pas l'OS, et `kill <pid>` l'arrête.
