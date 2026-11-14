# NobodyFalls — Game design & spécifications

Source de vérité fonctionnelle et technique du projet. Toute ambiguïté se
tranche ici ; si ce document est muet ou contradictoire, on pose la question
avant de coder.

## 1. Concept
Chaque joueur est un personnage suspendu au-dessus du vide. On ne peut pas
se sauver soi-même : on survit uniquement si d'autres joueurs nous tiennent.
Chaque jour, chacun a 3 mains à donner. À minuit UTC, ceux qui n'ont pas
assez de mains tombent. Le score ne récompense que l'aide apportée aux
autres.

Accroche : « Personne ne tombe. » / "Nobody falls."

## 2. Règles du jeu
- Journée de jeu = 00:00:00–23:59:59 UTC.
- Mains : 3 par joueur et par jour, non cumulables. Une main = un lien
  donneur → receveur, valable pour la journée en cours uniquement.
  Interdit : se tenir soi-même, donner 2 mains à la même personne le même
  jour, donner une main à un joueur bloqué. Une main donnée est définitive.
- Seuil de survie : 2 mains reçues (voir §3 pour le seuil dynamique).
  À minuit, tout joueur actif sous le seuil tombe.
- Nouveaux joueurs « fragiles » : immunisés au premier minuit après
  l'inscription, puis seuil = seuil normal + 1 au minuit suivant.
- Joueurs tombés : la chute est enregistrée (compteur de morts, profil),
  sans autre perte (score, amis, skins conservés). Le joueur revient
  automatiquement, sans action de sa part, à sa prochaine visite : il
  reprend la journée en cours au seuil normal (ni immunité ni +1). S'il
  revient trop près de minuit (délai configurable), il reprend à la
  journée suivante. Tant qu'il n'est pas revenu, il reste dans le vide et
  ne compte ni parmi les vivants ni comme nouveau mort.
- Compteurs : joueurs en vie (en direct) et nombre total de morts (une
  mort par chute), par saison et depuis toujours.
- Nuit de tempête : programmable par l'admin, seuil + 1 pour tous.
  Annoncée à tous les joueurs 6 h avant minuit.
- Score Gardien, attribué à minuit pour chaque main donnée à un joueur
  qui survit cette nuit-là :
  - +1 si le receveur est un ami, +2 s'il ne l'est pas ;
  - 0 si la main est réciproque (A tient B et B tient A le même jour) ;
  - +2 de bonus « Dernier rempart » si la main est donnée dans la dernière
    heure et fait atteindre le seuil au receveur.
- Dette de vie : si une main a été décisive (sans elle, le joueur tombait),
  on enregistre « X a été sauvé par Y ». Les deux le voient sur leur profil.
- Série mondiale : nombre de nuits consécutives sans aucune chute, affichée
  avec le nombre de chutes de la dernière nuit.
- Bord du vide : liste des joueurs actifs sous le seuil, triée par urgence,
  depuis laquelle n'importe qui peut donner une main.
- Invitations : chaque joueur a un lien d'invitation ; on stocke l'arbre
  (qui a invité qui).

### Résolution de minuit
Job serveur **idempotent** (rejouable sans effet de bord), protégé par un
verrou (advisory lock Postgres), écrit en opérations ensemblistes SQL pour
tenir à grande échelle. Il calcule : chutes, retours programmés, points, dettes de
vie, série mondiale, compteurs de morts, déblocages de skins, puis
notifie.

## 3. Démarrage avec peu de joueurs
- Seuil dynamique : seuil = min(2, nombre de joueurs actifs − 1), et
  seuil = 1 tant qu'il y a moins de 50 joueurs actifs. Paramétrable.
- Veilleurs : personnages non-joueurs, **toujours étiquetés comme tels**
  (jamais présentés comme de vrais joueurs), qui tiennent les joueurs sans
  main quand la population est faible. Leur nombre diminue automatiquement
  à mesure que la population augmente, jusqu'à disparaître. Leurs mains ne
  rapportent aucun point.
- Saisons à date de départ : liste d'attente avant le lancement ; la
  première nuit s'ouvre pour tout le monde au même moment.
- Tous ces paramètres sont configurables sans redéploiement.

## 4. Amis
- Amitié mutuelle : demande puis acceptation. Rejoindre via le lien
  d'invitation de quelqu'un crée l'amitié automatiquement.
- Suggestions : joueurs avec qui on s'est tenus plusieurs fois
  (« vous vous êtes tenus 3 nuits »).
- Bloquer / signaler : deux joueurs bloqués ne se voient plus et ne peuvent
  plus se donner de main.
- Limite configurable (500 amis par défaut).
- Notifications push : un ami passe sous le seuil, un ami est tombé, un ami
  t'a sauvé.

## 5. Personnages et skins
- Chaque joueur a un personnage, visible partout (accueil, listes, carte,
  classement). Personnage = **données**, pas images : emplacements
  (tête : coiffure/capuche ; haut : tenue/écharpe ; mains : gants) qui
  référencent un catalogue de skins.
- Le personnage change de posture selon son statut : tenu, attention,
  danger, soulagé, tombé (voir ART_DIRECTION.md).
- Skins débloqués **uniquement** par des actions solidaires (sauvetages,
  inconnus tenus, tempête survécue, titres, fin de saison…). Rien ne
  s'achète. Catalogue et conditions configurables sans redéploiement.

## 6. Classement
Points Gardien, trois vues : cette nuit, saison, mes proches (soi + amis).
Le joueur voit toujours sa propre ligne. Fin de saison : le top 100 gagne
un skin exclusif.

## 7. Carte du filet
- Une carte 2D unique et persistante, partagée par tous. **Pas une carte
  géographique** : aucune position réelle, aucun GPS.
- Position stable par joueur : placé à l'inscription près de celui qui l'a
  invité (sinon dans une zone peu dense), puis immobile. Pas de recalcul
  global du layout.
- Bas de la carte = le vide : les joueurs tombés (pas encore revenus) y
  sont affichés à leur abscisse.
- Navigation : glisser, pincer, 3 niveaux de zoom (monde : densité agrégée ;
  quartier : points ; proche : personnages). Boutons « me recentrer » et
  « aller à un ami », flèches clavier, vue liste équivalente pour les
  lecteurs d'écran.
- Les liens (mains de la nuit) sont dessinés entre personnages ; ceux qui
  te concernent sont mis en avant.
- Confidentialité : « qui me voit nommé sur la carte » (amis par défaut,
  tout le monde, personne). Les autres voient un point anonyme.
- Performance : carte découpée en tuiles côté serveur ; le client ne charge
  que les tuiles visibles ; au zoom monde, agrégats par tuile. Index spatial
  (PostGIS ou index (tile_x, tile_y)). Temps réel par tuile visible (groupes
  SignalR). 200 personnages dessinés maximum à l'écran.

## 8. Idées, contact, signalements (côté joueur)
- Proposer une idée (titre, description, catégorie), voter pour celles des
  autres, voir leur statut public.
- Contacter le support (formulaire → ticket).
- Signaler un joueur (pseudo, comportement).

## 9. Interface d'administration
Application séparée sous `/admin`, bundle distinct (n'alourdit pas le jeu).
- Rôles Admin, Modérateur, Support ; permissions vérifiées côté serveur.
  2FA (TOTP) obligatoire pour tout compte avec un rôle ; ré-authentification
  pour les actions sensibles.
- Journal d'audit de chaque action (qui, quoi, quand, avant/après),
  consultable, non modifiable.
- Tableau de bord : joueurs actifs, chutes par nuit, série mondiale,
  rétention J1/J7/J30, invitations, comptes suspects.
- Joueurs : recherche, fiche (mains, chutes, amis, signalements), sanctions
  (avertissement, pseudo réinitialisé, exclusion du compteur, ban temporaire
  ou définitif avec motif), comptes liés suspects (IP/appareil), export et
  suppression des données (RGPD).
- Signalements : file de modération avec statut, assignation, action prise.
- Demandes : tickets (support, contestation de sanction, suppression de
  compte) avec statut et réponse envoyée au joueur.
- Idées : modération, fusion des doublons, statut public (nouvelle, à
  l'étude, prévue, livrée, refusée), réponse visible des joueurs.
- Configuration en direct : seuils, Veilleurs, tempêtes, saisons, catalogue
  de skins et conditions, feature flags (tout passe par l'audit).
- Annonces : in-app et push, à tous ou à un segment.

## 10. Écrans (joueur)
Navigation principale : barre d'onglets en bas (Ce soir, Carte, Bord du
vide, Amis, Profil).
1. Onboarding : la règle en 3 écrans max, création du personnage,
   connexion, installation PWA (indispensable aux push sur iOS).
2. Ce soir : mon statut (mains reçues / seuil, temps avant minuit), mes
   mains, mon voisinage (qui me tient, qui je tiens).
3. Bord du vide : joueurs en danger, « Tenir » sur chacun.
4. Amis : ajouter (pseudo, lien), demandes, suggestions, liste avec statut.
5. Profil : score Gardien, titres, dettes de vie, lien d'invitation,
   réglages (langue, notifications, confidentialité).
6. Monde : série mondiale, chutes de la nuit, joueurs en vie, morts
   (saison et depuis toujours), tempête.
7. Carte du filet. 8. Vestiaire. 9. Classement. 10. Idées et contact.

## 11. Langues et compatibilité
- FR et EN dès le départ : aucun texte en dur (i18next côté front, .resx
  côté back pour notifications et emails). Pluriels, nombres, dates via la
  lib i18n. Langue détectée, modifiable, mémorisée. URLs `/fr` et `/en`.
- Navigateurs : 2 dernières versions de Chrome, Firefox, Safari, Edge,
  Safari iOS, Chrome Android. Mobile-first.
- PWA installable (manifest, service worker), Web Push (VAPID).

## 12. Stack
- Back : ASP.NET Core (dernière LTS), Minimal APIs, SignalR (fallback long
  polling actif).
- Données : PostgreSQL via EF Core (migrations versionnées), Redis.
- Front : React + TypeScript strict + Vite ; rendu Canvas 2D.
- Auth : OAuth Google et Discord, cookie de session (HttpOnly, Secure,
  SameSite).
- Anti-abus : Cloudflare Turnstile vérifié côté serveur (inscription, don
  de main), rate limiting par utilisateur et par IP, contraintes d'unicité
  en base.

## 13. Performance (contraintes, pas des souhaits)
- JS initial ≤ 150 Ko gzip, CSS ≤ 30 Ko gzip, vérifiés en CI.
- Lighthouse mobile ≥ 90 en performance et accessibilité.
- Temps réel par lots (1 diffusion par seconde max).
- Animations en requestAnimationFrame, stoppées onglet caché.
- Pas de bibliothèque de composants UI lourde ni de CSS runtime.

## 14. Qualité (dépôt public)
- Back en projets séparés : `Domain` (règles pures, zéro dépendance),
  `Application`, `Infrastructure`, `Api`, `Admin.Api` si pertinent.
- `Nullable`, `TreatWarningsAsErrors`, analyzers, `.editorconfig`,
  `dotnet format` en CI.
- Front : ESLint + Prettier, `strict`, aucun `any`, logique hors composants.
- Code, noms, commentaires en anglais ; commentaires pour le « pourquoi ».
- Tests : domaine (xUnit), intégration Testcontainers (Postgres réel,
  concurrence des dons), Vitest, Playwright (Chromium, Firefox, WebKit,
  viewport mobile), tests d'autorisation de chaque endpoint admin.
- Mode dev : seed de N joueurs factices, horloge simulable.
- Dépôt : README, `.env.example`, LICENSE (MIT), CONTRIBUTING,
  CODE_OF_CONDUCT, SECURITY.md, templates d'issues et de PR, `docs/adr/`.
- CI GitHub Actions : build, lint, format, tests, taille de bundle,
  Playwright, gitleaks. Dependabot.
- Commits conventionnels, petits et cohérents.

## 15. Hors périmètre (pour l'instant)
Cordées, réactions emoji, paiement, déploiement.
