# NobodyFalls — Direction artistique : Dark Cinematic Stylized Survival

Une véritable identité de jeu vidéo indépendant : un univers sombre,
vertical, atmosphérique, où des personnages étranges mais attachants sont
suspendus au-dessus du vide et reliés par de vraies cordes.

Références d'ambiance : Inside, Little Nightmares. On s'en inspire pour la
lumière, l'échelle et l'étrangeté ; on ne copie ni personnages, ni
costumes, ni compositions.

Scène de référence validée : `docs/reference/scene-demo.dc.html`
(canvas « NobodyFalls — maquettes », page « DA — Dark Cinematic (démo) »).
Toute nouvelle scène doit pouvoir s'y juxtaposer sans rupture.

## Ce qu'on ne fait plus
- Personnages en pictogrammes ou bonshommes filiformes, sans volume.
- Liens dessinés comme les arêtes d'un graphe.
- Fond uni sans profondeur ; UI de site web (cartes, listes encadrées).

## Univers
- Noir profond et gris bleuté. Sensation de vide et de hauteur.
- Profondeur atmosphérique : plans successifs, brume, silhouettes lointaines.
- Éclairage directionnel subtil : une lumière principale froide, venant
  d'en haut à gauche, avec poussière en suspension.
- Contraste entre personnages éclairés et environnement sombre.
- Aucun décor inutile : ni falaises, ni bâtiments ; seulement le vide, la
  lumière, les cordes et les gens.

## Personnages
- Humanoïdes stylisés, avec volume et proportions travaillés : tête
  grosse, corps compact, bras et jambes anatomiquement lisibles.
- Apparence légèrement étrange mais attachante : petits yeux noirs avec un
  reflet, pas de bouche (sauf en panique), joues à peine rosées.
- Éclairage sur chaque volume : face éclairée, face dans l'ombre, liseré
  de lumière (rim light) côté source, occlusion au cou.
- Postures naturelles, pilotées par un squelette (épaules, coudes, mains,
  hanches, genoux, pieds) ; les bras tirent réellement vers les cordes.
- Personnalisation par vêtements et accessoires : manteaux trop grands,
  bonnets, capuches, chapeaux, écharpes, gants, bottes. Tons sourds et
  désaturés.

### Postures (une par statut, lisibles sans couleur)
| Statut | Posture |
|---|---|
| Tenu | deux mains sur deux cordes, corps calme, balancement lent |
| Attention (sous le seuil) | une main sur une corde, l'autre bras tendu dans le vide, tête inclinée |
| Danger (0 main) | agrippé à un fil effiloché, balancement fort, jambes qui battent, yeux agrandis, bouche ouverte |
| Soulagé (vient d'être sauvé) | attrape la corde lancée, balancement qui s'apaise, léger sourire |
| Tombé | aspiré dans la brume du vide, atténué, bras ballants |

## Cordes (le filet)
- De vraies cordes : fibres torsadées, ombre, reflet, épaisseur variable.
- Tension physique : une corde qui porte un poids est presque droite ; une
  corde libre pend en courbe. Elles suivent le balancement des corps.
- Attache : nœud à la ceinture de celui qui tient (anneau métallique),
  agrippée à deux mains par celui qui est tenu.
- Main libre = corde enroulée à la hanche. Donner une main = lancer cette
  corde (animation de déroulé, puis mise en tension).
- Fil effiloché = danger, visible avant même de lire l'étiquette.

## La carte
Une scène de jeu, pas un diagramme :
- Plans en parallaxe : figures lointaines floues dans la brume, plan
  moyen, personnages proches nets et éclairés.
- Le vide en bas : brume froide qui monte ; les tombés y disparaissent.
- Zoom : de loin, une foule suspendue (silhouettes simplifiées) ; de près,
  les personnages complets avec leurs skins et leurs postures.
- Les cordes qui te concernent sont nettes et éclairées ; les autres
  s'estompent avec la distance.

## Palette
| Rôle | Couleur |
|---|---|
| Fond haut / bas | #0E141D → #030405 |
| Lumière principale (froide) | rgba(140,165,195) en faible opacité |
| Brume | rgba(110,130,155) en faible opacité |
| Texte principal (os) | #E4E1DA |
| Texte secondaire | #A3ACB6, étiquettes #7F8B98 |
| Action principale | #D9D3C7 (os) sur #0B0E13 |
| Cordes | #6B5E4F, ombre #1E1915, reflet #B3A48A |
| Attention | #D8A85A |
| Danger | #D0645F |

Les couleurs de statut sont réservées aux statuts : aucun vêtement ne les
utilise. Les statuts restent lisibles sans couleur (posture, état de la
corde, icône, texte). Contraste AA pour tout texte.

## Interface
- Conserver la hiérarchie et les fonctionnalités existantes ; changer le
  traitement graphique pour qu'il appartienne au même monde.
- La scène occupe tout l'écran ; l'UI flotte dessus, sans cadres.
- Titres en condensé (Barlow Condensed 500/600), étiquettes en capitales
  très espacées, texte courant en police système.
- Texte posé sur un fondu vers le noir en bas de l'écran (lisibilité), pas
  de cartes.
- Bouton principal plein couleur os, angles presque droits ; secondaires
  en texte.
- Pas de HUD surchargé, pas de glow, pas de néon, pas d'emojis.
- Microcopie directe et un peu grave : « Tu tiens encore. », « Yuki ne
  tient plus qu'à un fil. »
- Les dégradés sont autorisés uniquement pour la lumière, la brume et la
  lisibilité du texte, jamais en décoration.

## Responsive : mobile, tablette, desktop
Mobile-first ne veut pas dire mobile-only. Sur grand écran, on ne
centre jamais une colonne de téléphone au milieu d'un écran noir.
- Points de rupture : < 640 px (mobile), 640–1023 px (tablette),
  ≥ 1024 px (desktop). Layout basé sur la largeur réelle, pas sur le
  user-agent.
- La scène (canvas) occupe toujours toute la fenêtre, quelle que soit
  sa taille. Une caméra cadre la scène selon le ratio : en paysage on
  voit plus de vide et de foule sur les côtés, les personnages proches
  restent à une hauteur confortable (dimensionnés sur la hauteur de la
  fenêtre, bornés en taille min/max), jamais étirés ni pixelisés.
- Desktop : l'UI quitte le bas de l'écran. Navigation en rail vertical
  à gauche (icône + libellé), panneau d'information à droite ou à
  gauche (360–420 px), posé sur un fondu latéral, sans cadre. La scène
  reste au centre, visible.
- Écrans de liste (Bord du vide, Amis, Classement, Profil) : contenu
  dans une colonne de 640–760 px max, la scène reste visible en fond
  (atténuée) ou dans une colonne latérale ; Bord du vide en desktop =
  liste + aperçu de la scène du joueur sélectionné côte à côte.
- Typographie fluide (`clamp()`), titres plus grands en desktop ; zones
  cliquables ≥ 44 px partout ; survol (hover) et focus visibles en
  desktop.
- Clavier : navigation complète, raccourcis discrets en desktop
  (ex. flèches dans la carte, Échap pour fermer un panneau).
- L'admin est desktop-first mais doit rester utilisable sur mobile.
- Tests Playwright sur 3 viewports : 390×844, 820×1180, 1440×900.

## Animations
- Balancement continu et lent de chaque personnage (pendule), amplitude
  liée au statut.
- Sauvetage : la corde se déroule (~0,8 s), se tend, la posture change.
- Chute : le personnage glisse dans la brume. Remontée : l'inverse.
- Poussière qui dérive dans la lumière, brume qui avance lentement.
- `prefers-reduced-motion` : image fixe, changements d'état sans animation.

## Contraintes techniques
- Rendu Canvas 2D, derrière des renderers (`CharacterRenderer`,
  `RopeRenderer`, `MapRenderer`) qui prennent des données (apparence,
  posture, positions, cordes) et dessinent. Aucune logique métier dedans.
- Personnages : squelette 2D (rig) + parties en calques (tête, torse,
  bras, jambes, et un calque par vêtement/accessoire). Prototype : parties
  dessinées en code comme dans la scène de référence. Production : parties
  illustrées par un artiste, livrées en atlas de sprites, mêmes points
  d'attache.
- Éclairage : ombrage et liseré baked dans les sprites ; seule la lumière
  d'ambiance et la brume sont calculées à l'exécution.
- Parallaxe : les plans lointains sont rendus une fois dans des calques
  hors écran (pré-floutés), puis recomposés ; `ctx.filter` n'est pas
  disponible partout (Safari), ne pas en dépendre.
- Budget : 60 fps visé sur mobile milieu de gamme, 30 fps minimum ; boucle
  stoppée onglet caché ; devicePixelRatio plafonné à 2.
- Tous les visuels passent par des tokens de thème ; la DA reste
  modifiable sans toucher à la logique métier.
