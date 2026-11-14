# NobodyFalls — Plan de la phase 2 : administration, modération, retours des joueurs

> Statut : **validé le 14/11/2026** (réponses en section 10).
> **[D]** = décision prise par défaut si tu ne dis rien ; **[Q]** (section 10) =
> besoin de ta réponse. Références : `GAME_DESIGN.md` §8 et §9 (§n), ADR 0001–0015,
> plan de la phase 1.

---

## 1. Périmètre

| Inclus | Exclu (phase 3 ou plus tard) |
|---|---|
| Joueur : idées + votes + statuts, contact (tickets), signalement d'un joueur, écran « Idées et contact » depuis le Profil | Carte, vestiaire, classement |
| Admin `/admin` : rôles, 2FA TOTP, ré-authentification, audit, tableau de bord, joueurs (fiche, sanctions, comptes liés, RGPD), signalements, tickets, idées | Édition du catalogue de skins (phase 3, avec le vestiaire, Q4) |
| Configuration en direct : règles et seuils, Veilleurs, tempêtes, saisons, feature flags | E-mails (aucune infrastructure d'envoi ; tout passe par l'app et le push) |
| Annonces in-app et push, à tous ou à un segment | Temps réel dans l'admin (rafraîchissement périodique suffit) |

---

## 2. Architecture

```
src/
  NobodyFalls.Domain         + Staff/ (Role, Permission, PermissionMatrix)
                             + Moderation/ (Sanction, Report, Ticket, Idea, règles pures)
                             + Security/Totp.cs (RFC 6238, sans dépendance)
  NobodyFalls.Application    + Admin/ (cas d'usage admin, chacun = 1 transaction + 1 ligne d'audit)
                             + Feedback/ (idées, tickets, signalements côté joueur)
                             + Audit/ (IAuditTrail), Flags/ (IFeatureFlags), Announcements/
  NobodyFalls.Infrastructure + stores Postgres, audit chaîné, segments SQL, purge des données
  NobodyFalls.Api            + Admin/ : endpoints /api/v1/admin/*, filtre d'accès, session
                             admin ; sert /admin (2e index.html) ; endpoints joueur
web/
  packages/theme             tokens partagés (dont layout, ADR 0016)
  packages/layout            layout responsive partagé jeu + admin (ADR 0016)
  apps/game                  + écran « Idées et contact » en chunk différé
  apps/admin                 NOUVEAU : app Vite séparée, servie sous /admin/
```

**[D] Un seul déployable.** L'admin vit dans le même processus et sur la même
origine : les cookies restent simples, sans CORS (ADR 0017).
*Écart à la réalisation :* le code admin est un dossier `Admin/` de `NobodyFalls.Api`
plutôt qu'un projet `NobodyFalls.Admin.Api` (« si pertinent », §14) : un projet à
part n'aurait pas eu accès à la plomberie interne de l'API sans un projet partagé
de plus. La frontière est tenue par le filtre d'accès (fermé par défaut) et par un
test qui exige que chaque route `/api/v1/admin` figure dans la table d'autorisation.

**[D] Le bundle admin est séparé** (ADR 0017) :
- `apps/admin` est une deuxième application Vite, construite dans
  `wwwroot/admin/`, avec son propre `index.html`. L'API sert
  `/admin/{**path}` → `admin/index.html` (`no-cache`, `X-Robots-Tag: noindex`,
  CSP stricte).
- Le jeu n'importe **rien** de `apps/admin`. Une règle ESLint
  (`no-restricted-imports`) et le script de budgets le vérifient.
- **Budgets** :
  - le JS initial du jeu reste **≤ base de fin de phase 1 + 1 Ko** ; la base
    (111,6 Ko) est inscrite dans `web/budgets.json` ;
  - l'écran « Idées et contact » est un chunk différé ;
  - admin : JS initial ≤ 200 Ko et CSS ≤ 30 Ko gzip, également vérifiés en CI.
- Le service worker du jeu **ignore `/admin`** : pas de cache, pas
  d'interception. L'admin n'a pas de service worker.

---

## 3. Modèle de données (nouvelles tables, migrations EF)

| Table | Colonnes principales | Contraintes / remarques |
|---|---|---|
| `staff_members` | player_id PK → players, roles `text[]` (`admin`/`moderator`/`support`), granted_by, granted_at, disabled_at | Un membre de l'équipe est un compte joueur (connexion Google/Discord) qui reçoit un ou plusieurs rôles |
| `totp_credentials` | player_id PK, secret_protected (Data Protection), confirmed_at, last_step `bigint` (anti-rejeu), failed_attempts, locked_until | Secret chiffré au repos |
| `totp_recovery_codes` | id, player_id, code_hash, used_at | 10 codes, hachés (SHA-256 + sel) |
| `audit_log` | id `bigserial`, at, actor_id null (système), actor_roles `text[]`, action, target_type, target_id, before `jsonb`, after `jsonb`, reason, ip, request_id, prev_hash, hash | **Ajout seul**, voir §5 |
| `sanctions` | id, player_id, kind (`warning`/`pseudo_reset`/`score_exclusion`/`ban`), reason, public_message, starts_at, ends_at null (définitif), issued_by, revoked_at, revoked_by, revoke_reason, acknowledged_at | Index (player_id) et sanctions actives |
| `players` + | session_version `int` (révocation de session), banned_until null, score_excluded `bool`, pseudo_reset_pending `bool` | Ban et exclusion dénormalisés pour le SQL de minuit |
| `login_events` | id, player_id, at, ip `inet`, device_id, user_agent_family | IP en clair (Q5), purge automatique après **90 jours** ; index (ip), (device_id) |
| `reports` | id, reporter_id, target_type (`player`/`idea`), target_id, category, details, target_snapshot `jsonb`, status (`open`/`in_review`/`actioned`/`dismissed`), assignee_id, resolution_note, sanction_id null, created_at, resolved_at | Un seul signalement ouvert par couple (auteur, cible) |
| `tickets` | id, player_id, category (`support`/`appeal`/`account_deletion`), sanction_id null, subject, status (`open`/`waiting_player`/`resolved`/`closed`), assignee_id, locale, created_at, updated_at | |
| `ticket_messages` | id, ticket_id, author_kind (`player`/`staff`), author_id, body, internal `bool` (note interne de l'équipe), created_at | Un message interne n'est jamais renvoyé au joueur |
| `ideas` | id, author_id, title, body, category (`gameplay`/`social`/`interface`/`other`), locale, status (`new`/`under_review`/`planned`/`shipped`/`declined`), staff_answer, answered_at, hidden_at, hidden_reason, merged_into_id null, votes_count, created_at | Index (status, votes_count) |
| `idea_votes` | idea_id, player_id, created_at | PK (idea_id, player_id) |
| `feature_flags` | key PK, enabled, description, updated_at, updated_by | Clés déclarées dans le code (§7) ; une clé inconnue est refusée |
| `announcements` | id, title `jsonb` {fr,en}, body `jsonb`, channels (`in_app`, `push`), segment `jsonb`, publish_at, expires_at, status (`draft`/`scheduled`/`sent`/`cancelled`), recipients_count, created_by | |
| `announcement_receipts` | announcement_id, player_id, seen_at, dismissed_at | Affichage in-app ; le push passe par l'outbox existante (dedupe `announcement:{id}:{player}`) |
| `player_visits` | player_id, day | PK (player_id, day), 1 insertion par jour et par joueur ; sert aux rétentions J1/J7/J30 |
| `notification_preferences` + | announcements `bool` (activé par défaut) | |

**Déjà en place, réutilisé :**
- `game_settings` : règles versionnées, figées par journée (ADR 0005) ;
- `storms` ;
- `seasons` ;
- `notification_outbox` (ADR 0009).

---

## 4. Rôles et permissions

Les permissions sont une énumération du domaine. La matrice est **une seule
table dans le code** (`PermissionMatrix`), lue par le serveur, par l'admin pour
masquer ce qui est inaccessible, et par les tests.

| Permission | Admin | Modérateur | Support | Sensible (ré-auth) |
|---|:-:|:-:|:-:|:-:|
| `dashboard.view` | ✓ | ✓ | ✓ | |
| `players.search`, `players.view` (fiche : mains, chutes, amis, signalements, tickets) | ✓ | ✓ | ✓ | |
| `players.view_links` (comptes liés, IP, appareils ; consultation auditée) | ✓ | ✓ | | |
| `sanctions.warn`, `sanctions.reset_pseudo`, `sanctions.exclude_score`, `sanctions.ban_temporary` | ✓ | ✓ | | |
| `sanctions.ban_permanent` | ✓ | | | ✓ |
| `sanctions.revoke` | ✓ | ✓ | | |
| `reports.view`, `reports.handle` (assigner, statuer, sanctionner depuis le signalement) | ✓ | ✓ | | |
| `tickets.support` (support, suppression de compte) | ✓ | | ✓ | |
| `tickets.appeal` (contestation : jamais traitée par l'auteur de la sanction) | ✓ | ✓ | | |
| `ideas.moderate` (masquer, fusionner, changer le statut, répondre) | ✓ | ✓ | ✓ | |
| `gdpr.export` | ✓ | | ✓ | |
| `gdpr.delete` | ✓ | | | ✓ |
| `config.rules` (seuils, Veilleurs), `config.storms`, `config.seasons`, `config.flags` | ✓ | | | ✓ |
| `announcements.send` | ✓ | | | ✓ |
| `audit.view` | ✓ | | | |
| `staff.manage` (donner ou retirer des rôles) | ✓ | | | ✓ |

Règles générales :
- **Plusieurs rôles par personne** : les permissions s'additionnent.
- **Personne ne se sanctionne soi-même**, et personne ne modifie ses propres
  rôles.
- **Un membre de l'équipe ne peut être sanctionné** qu'après le retrait de son
  rôle, par un Admin.
- **Les rôles sont relus en base à chaque requête** (cache de 15 s), pour qu'un
  retrait prenne effet immédiatement (ADR 0018).

---

## 5. Authentification admin, 2FA, ré-authentification, audit

### Session admin (ADR 0019)

1. On se connecte comme d'habitude (Google, Discord, dev), avec le cookie
   joueur `nf`.
2. `/admin` exige en plus le cookie **`nf.admin`** (`__Host-`, HttpOnly,
   SameSite=Strict, Path=/), émis seulement après un code TOTP valide. Durée :
   **8 h au maximum, expiré après 30 min d'inactivité**. Il contient
   `auth_time` et `reauth_at`.
3. **Ré-authentification** : une action sensible exige `reauth_at` de moins de
   **5 min**. Sinon, l'API répond `401 admin.reauth_required` ; l'interface
   redemande un code puis rejoue l'action.
4. **Enrôlement** : la première visite d'un compte qui a un rôle ouvre l'écran
   d'enrôlement.
   - Il affiche un QR `otpauth://` et le secret en clair.
   - Le compte confirme avec un code, puis reçoit **10 codes de secours**,
     affichés une seule fois.
   - Tant que l'enrôlement n'est pas fait, aucun endpoint admin ne répond.
5. **TOTP** :
   - RFC 6238 (SHA-1, 6 chiffres, 30 s, ±1 pas), implémenté sans dépendance et
     testé avec les vecteurs de la RFC ;
   - anti-rejeu par `last_step` ;
   - 5 échecs → verrouillage de 15 min, audité.
   - Un Admin peut réinitialiser le TOTP d'un autre membre ; c'est une action
     sensible et auditée.
6. **Révocation** :
   - `players.session_version` est inscrit dans les cookies `nf` et `nf.admin`
     et vérifié à chaque requête (cache de 15 s) ;
   - un ban, un retrait de rôle ou une réinitialisation du TOTP déconnecte
     **immédiatement**, sur tous les appareils (ADR 0021).
7. **Premier Admin** :
   - une commande `dotnet NobodyFalls.Api.dll staff grant <pseudo> admin` ne
     fonctionne que s'il n'existe encore aucun Admin ;
   - en dev, un bouton du panneau DEV fait de soi un Admin, et
     `GET /dev/totp/{playerId}` renvoie le code courant (pour Playwright).
8. **Anti-CSRF** : l'en-tête `X-NF-Request` existant, plus `SameSite=Strict`
   sur `nf.admin`.

### Audit (ADR 0020)

- **Une action, une ligne, dans la même transaction.**
  - Chaque cas d'usage admin passe par `IAuditTrail.RecordAsync(tx, …)`.
  - Si l'audit échoue, l'action est annulée.
  - Les lectures sensibles sont aussi auditées : fiche des comptes liés,
    export RGPD, journal d'audit lui-même.
- **Contenu d'une ligne** :
  - qui : `actor_id` et ses rôles au moment de l'action ;
  - quoi : un code d'action stable (`sanction.ban`, `report.resolve`,
    `config.rules.update`, `storm.schedule`…) ;
  - sur quoi : `target_type` et `target_id` ;
  - l'état **avant et après** (le diff JSON des champs modifiés), le motif, et
    l'IP de l'acteur.
- **Non modifiable, à trois niveaux** :
  1. un trigger Postgres rejette `UPDATE`, `DELETE` et `TRUNCATE` sur
     `audit_log` ;
  2. l'application n'a **aucun** chemin de code pour modifier une ligne ;
  3. **chaînage** : chaque ligne contient
     `hash = SHA-256(prev_hash ‖ contenu canonique)`, écrit sous verrou
     consultatif ; `GET /api/v1/admin/audit/verify` recalcule la chaîne, ce qui
     détecte toute altération, même par un superutilisateur Postgres.
- **Consultation** : filtres par acteur, action, cible et période, avec
  pagination par curseur. Sur une fiche joueur, sur un signalement ou sur un
  ticket, les lignes qui les concernent forment leur historique.
- **Tests** : pour chaque endpoint admin qui modifie quelque chose, le test
  vérifie la ligne d'audit attendue (action, acteur, avant/après).
- **Données personnelles** : l'audit garde des identifiants, pas d'e-mails.
  - Après une suppression RGPD, les lignes restent : c'est un intérêt légitime
    (preuve des actions de modération).
  - La cible y apparaît alors comme « compte supprimé ».

---

## 6. Fonctionnalités joueur (§8)

L'écran **« Idées et contact »** (`/fr/profile/feedback`, chunk différé) est
accessible depuis le Profil et compte trois onglets.

**Idées**
- Liste triée par votes ou par nouveauté, filtres par statut, idées dans ta
  langue d'abord.
- Le statut public est affiché, ainsi que la réponse de l'équipe (signée
  « L'équipe NobodyFalls », jamais le pseudo d'un membre).
- On vote ou on retire son vote, sauf sur ses propres idées.
- Pour proposer une idée : titre (5–80), description (≤ 1000), catégorie.
  Limite : 3 idées par jour, et Turnstile.
- Une idée fusionnée renvoie vers l'idée principale. Les votes sont réunis,
  sans doublon.

**Contact**
- Formulaire avec catégorie (support, contestation d'une sanction, suppression
  du compte), sujet et message.
- Liste de mes demandes, avec leur fil de messages. Je peux répondre tant que
  la demande n'est pas fermée.
- Quand l'équipe répond, je reçois un push (`ticket.replied`) et une pastille
  s'affiche sur le Profil.

**Signaler**
- Le bouton « Signaler » est accessible depuis la fiche d'un joueur ou depuis
  le formulaire (par pseudo).
- On choisit une catégorie (pseudo inapproprié, harcèlement, triche ou abus du
  jeu, autre) et on ajoute des détails (≤ 1000).
- L'auteur n'est **jamais** révélé à la cible. L'auteur reçoit « Ton
  signalement a été traité », sans le détail de la décision.
- Limite : 10 signalements par jour.

**Côté joueur sanctionné**
- **Avertissement** : un écran s'affiche à la prochaine visite avec le message
  public, et doit être confirmé (`acknowledged_at`).
- **Pseudo réinitialisé** : le pseudo devient `joueur-xxxx` et le joueur choisit
  un nouveau pseudo à sa prochaine visite.
- **Ban** :
  - l'écran de ban indique le motif public et la date de fin ;
  - la seule action possible est d'ouvrir **une contestation** ;
  - effets en jeu (Q2) : le joueur est retiré du jeu sans compter comme une
    mort, et les mains qu'il a déjà données sont conservées.
- **Exclusion du score** (Q1) :
  - le joueur continue de jouer et **reste dans les compteurs** (en vie,
    morts) ;
  - pendant la sanction, ses mains ne rapportent aucun point Gardien ;
  - son score n'est plus montré aux autres (et sera absent du classement en
    phase 3) ;
  - il retrouve son score affiché quand la sanction est levée ;
  - la règle entre dans `NightResolver` et `NightSql`, couverte par les tests
    différentiels.

**Effets sur le jeu, cohérents avec le SQL de minuit**
- Un joueur banni n'apparaît plus : ni au Bord du vide, ni dans le voisinage,
  ni dans les suggestions.
- On ne peut pas lui donner de main (`hand.receiver_unavailable`).
- À minuit, il n'est pas résolu : il ne tombe pas et ne compte pas parmi les
  vivants.
- À la fin du ban, il revient comme un tombé qui revient (seuil normal).
- Ces règles entrent dans `NightResolver` **et** dans `NightSql` ; les tests
  différentiels les couvrent.

---

## 7. Configuration en direct et annonces (§9)

**Règles (seuils, Veilleurs)**
- C'est un formulaire sur `GameRules` : la validation se fait dans le domaine,
  et l'admin voit le diff avec la version courante avant d'enregistrer.
- L'enregistrement crée une nouvelle version, qui **s'applique à la journée
  suivante** (ADR 0005, inchangé). L'historique des versions est consultable,
  et on peut revenir à une version antérieure (ce qui crée une nouvelle
  version).

**Tempêtes**
- On programme ou on annule une tempête pour une journée **pas encore
  commencée** (`StormPolicy.CanSchedule`, inchangé). La journée en cours ne
  bouge jamais.
- **Preuve « terminé » en mode dev** :
  1. programmer une tempête pour demain ;
  2. avancer à minuit : demain s'ouvre avec `storm = true`, la tempête et le
     seuil +1 sont visibles ;
  3. avancer au minuit suivant : la résolution applique le seuil +1.
- Un test Playwright joue ce scénario.

**Saisons**
- On peut planifier la prochaine saison (nom, jour d'ouverture, dernier jour
  éventuel), la modifier tant qu'elle n'est pas ouverte, et fixer le dernier
  jour de la saison en cours.
- Les règles de fin de saison restent en phase 3, comme prévu en phase 1.

**Feature flags (ADR 0022)**
- Chaque clé est déclarée dans le code (`FeatureFlag.IdeasOpen`,
  `ReportsOpen`, `ContactOpen`, `AnnouncementsPush`…) avec sa valeur par
  défaut. En base, on ne stocke que les surcharges.
- Le serveur les lit avec un cache de 10 s. Le client reçoit celles qui le
  concernent dans `GET /api/v1/session`.
- Un flag désactivé masque la fonctionnalité **et** la refuse côté serveur
  (`feature.disabled`). Pas de déploiement progressif par pourcentage en
  phase 2.

**Annonces (ADR 0023)**
- **Contenu** : titre et texte en FR **et** EN, tous deux obligatoires.
- **Canaux** : in-app (bandeau sur « Ce soir », jusqu'à ce qu'il soit fermé ou
  qu'il expire) et/ou push.
- **Segments**, combinables :
  - tous ;
  - langue ;
  - statut (en vie, tombé, en attente) ;
  - inactifs depuis N jours ;
  - inscrits après une date ;
  - liste de joueurs.
- **Avant d'envoyer** : l'admin voit le nombre exact de destinataires et un
  aperçu dans les deux langues.
- **Envoi** : immédiat ou programmé, et annulable tant qu'il n'est pas parti.
  - Le push passe par l'outbox existante (exactement une fois, avec un débit
    limité).
  - Il respecte la nouvelle préférence « Annonces ».
  - L'in-app passe par `announcement_receipts`.

---

## 8. Contrats d'API

Toutes les réponses JSON utilisent les erreurs `problem+json` avec un `code`
stable (traduit côté client), comme en phase 1. L'en-tête `X-NF-Request` est
exigé.

### Joueur (`/api/v1`, cookie `nf`)

| Méthode | Route | Rôle |
|---|---|---|
| GET | `/ideas?status=&sort=votes|new&cursor=` | liste |
| GET | `/ideas/{id}` | détail, réponse de l'équipe, idée principale si fusionnée |
| POST | `/ideas` | proposer (Turnstile) |
| PUT / DELETE | `/ideas/{id}/vote` | voter / retirer son vote |
| GET | `/tickets` · `/tickets/{id}` | mes demandes + messages non internes |
| POST | `/tickets` · `/tickets/{id}/messages` | ouvrir / répondre |
| POST | `/reports` | signaler (`targetType`, `targetId` ou `pseudo`, `category`, `details`) |
| GET | `/me/notices` | avertissements à confirmer, annonces in-app actives, pseudo à rechoisir |
| POST | `/me/notices/{kind}/{id}/ack` | confirmer / fermer |
| PUT | `/me/pseudo` | rechoisir son pseudo après une réinitialisation |

Le compte banni n'a accès qu'à `GET /session`, `/me/notices` et aux tickets
`appeal`.

### Admin (`/api/v1/admin`, cookies `nf` + `nf.admin`, permission indiquée)

| Méthode | Route | Permission |
|---|---|---|
| GET | `/session` | (rôle requis) : rôles, permissions, enrôlement, `reauthValidUntil` |
| POST | `/session/totp/enroll` · `/session/totp/confirm` | (rôle requis, avant le 2FA) |
| POST | `/session/login` (code TOTP ou code de secours) · `/session/reauth` · `/session/logout` | (rôle requis) |
| GET | `/dashboard` | `dashboard.view` |
| GET | `/players?q=&status=&cursor=` · `/players/{id}` | `players.view` |
| GET | `/players/{id}/links` | `players.view_links` (audité) |
| GET | `/players/{id}/hands?days=` · `/players/{id}/sanctions` · `/players/{id}/audit` | `players.view` (+ `audit.view` pour l'historique d'audit complet) |
| POST | `/players/{id}/sanctions` (`kind`, `reason`, `publicMessage`, `endsAt`) | `sanctions.*` selon le type ; le ban définitif est sensible |
| POST | `/sanctions/{id}/revoke` | `sanctions.revoke` |
| GET | `/players/{id}/export` | `gdpr.export` (JSON téléchargeable, audité) |
| DELETE | `/players/{id}` (`reason`) | `gdpr.delete` (sensible) |
| GET | `/reports?status=&assignee=&cursor=` · `/reports/{id}` | `reports.view` |
| POST | `/reports/{id}/assign` · `/reports/{id}/resolve` (`outcome`, `note`, `sanction?`) | `reports.handle` |
| GET | `/tickets?category=&status=&cursor=` · `/tickets/{id}` | `tickets.support` ou `tickets.appeal` selon la catégorie |
| POST | `/tickets/{id}/messages` (`body`, `internal`) · `/tickets/{id}/status` · `/tickets/{id}/assign` | idem |
| GET | `/ideas?…` | `ideas.moderate` |
| PATCH | `/ideas/{id}` (`status`, `staffAnswer`, `hidden`, `hiddenReason`) | `ideas.moderate` |
| POST | `/ideas/{id}/merge` (`intoId`) | `ideas.moderate` |
| GET / PUT | `/config/rules` · GET `/config/rules/versions` | `config.rules` (le PUT est sensible) |
| GET / POST / DELETE | `/config/storms` · `/config/storms/{day}` | `config.storms` (sensible) |
| GET / POST / PATCH | `/config/seasons` · `/config/seasons/{id}` | `config.seasons` (sensible) |
| GET / PUT | `/config/flags` · `/config/flags/{key}` | `config.flags` (sensible) |
| GET / POST | `/announcements` · POST `/announcements/preview` (nombre de destinataires) | `announcements.send` |
| POST | `/announcements/{id}/send` · `/announcements/{id}/cancel` | `announcements.send` (sensible) |
| GET | `/audit?actor=&action=&target=&from=&to=&cursor=` · `/audit/verify` | `audit.view` |
| GET / PUT / DELETE | `/staff` · `/staff/{playerId}` · POST `/staff/{playerId}/totp/reset` | `staff.manage` (sensible) |

### Tests d'autorisation (obligatoires)

Une `TheoryData` liste **chaque** endpoint admin avec sa permission. Elle
s'appuie sur la table des routes réellement montées, ce qui fait échouer un
endpoint oublié. Pour chacun, le test vérifie la réponse :
- sans session : `401` ;
- avec une session joueur sans rôle : `403` ;
- avec un rôle mais sans 2FA : `403 admin.totp_required` ;
- avec chaque rôle (Admin, Modérateur, Support) : `403` ou un succès, conforme
  à la matrice ;
- pour une action sensible sans ré-authentification récente :
  `401 admin.reauth_required`.

---

## 9. Interface d'admin (apps/admin)

- **Même DA, en version dense** : fond `#0E141D`, texte os, titres en Barlow
  Condensed, étiquettes en capitales espacées, aucune carte ni ombre ni néon.
  Tableaux denses avec lignes séparées par de fins filets, et les couleurs de
  statut réservées aux statuts.
- **Desktop d'abord** : barre latérale de navigation, liste et détail côte à
  côte. Sur mobile, la navigation passe en tiroir et le détail en plein écran.
- **Écrans** :
  - connexion / enrôlement 2FA ;
  - tableau de bord ;
  - joueurs, avec la fiche ;
  - signalements, avec le traitement ;
  - tickets ;
  - idées ;
  - configuration : règles, tempêtes, saisons, flags ;
  - annonces ;
  - audit ;
  - équipe.
- **Tableau de bord** :
  - joueurs actifs ;
  - chutes par nuit (30 dernières nuits) ;
  - série mondiale ;
  - rétention J1/J7/J30 par cohorte d'inscription (`player_visits`) ;
  - invitations par jour ;
  - comptes suspects : plusieurs comptes sur un même appareil ou réseau, ou une
    rafale d'inscriptions.
  
  Les graphiques sont en SVG fait main, sans bibliothèque.
- **Pile** : React, wouter et zustand, comme le jeu. Les textes passent par
  i18next en FR et EN (Q3), avec la même règle « aucun texte en dur » et le
  test de parité des catalogues.

---

## 10. Points tranchés (14/11/2026)

- **Q1. Sanction sur le score** : « exclusion du compteur » devient
  **exclusion du score**.
  - Le joueur reste engagé et compté (en vie, morts).
  - Ses mains ne rapportent plus de points pendant la sanction.
  - Son score est masqué aux autres, puis absent du classement en phase 3.
- **Q2. Ban** :
  - retiré du jeu sans compter comme une mort, amitiés conservées ;
  - les mains déjà données aujourd'hui restent valables ;
  - retour comme un tombé à la fin d'un ban temporaire.
- **Q3. Langues de l'admin** : français et anglais, sans texte en dur.
- **Q4. Catalogue de skins** : son édition est faite en phase 3, avec le
  vestiaire.
- **Q5. Comptes liés** :
  - IP **en clair** et identifiant d'appareil (cookie aléatoire de longue
    durée) ;
  - conservation de 90 jours, puis purge automatique ;
  - visibles avec `players.view_links` seulement, et chaque consultation est
    auditée.

### Décisions par défaut [D]

- **Un membre de l'équipe est un compte joueur.** Il peut jouer, mais la règle
  « personne ne se sanctionne soi-même » s'applique, et ses actions de staff
  n'ont aucun effet sur son jeu.
- **Les réponses de l'équipe sont signées « L'équipe NobodyFalls »**, jamais le
  pseudo du membre.
- **Une contestation est traitée par quelqu'un d'autre** que l'auteur de la
  sanction ; c'est vérifié côté serveur.
- **Ticket de suppression de compte** : il est traité par un membre de l'équipe
  (Admin, après ré-authentification). La suppression est réelle :
  - le compte est anonymisé (`deleted_at`, pseudo libéré, connexions externes
    et abonnements push supprimés, contenus signés « compte supprimé ») ;
  - les mains passées restent anonymes, car les compteurs en dépendent.
- **Export RGPD** : un JSON contenant le profil, les mains données et reçues,
  les amis, les idées, les tickets, les signalements faits par le joueur et
  les sanctions.
- **Admin sans temps réel** : les files de signalements et de tickets se
  rafraîchissent toutes les 30 s, et à chaque retour sur l'onglet.
- **Pas d'e-mails en phase 2**. Les réponses arrivent dans l'app et par push.
- **Contact réservé aux joueurs connectés** (ou bannis, pour contester une
  sanction). Le signalement des failles passe par GitHub (SECURITY.md).
- **Signaler une idée** passe par la même file que les signalements de
  joueurs.

---

## 11. Découpage en PR et preuves

| PR | Contenu | Preuve |
|---|---|---|
| (a) `feat(admin-auth)` | `staff_members`, permissions, TOTP (vecteurs RFC), session admin, ré-auth, `session_version`, audit chaîné + trigger, commande `staff grant`, outils dev (devenir admin, code TOTP), squelette `apps/admin` (connexion, enrôlement, équipe, audit), garde-fou de budget du jeu | Matrice d'autorisation complète, tests audit (trigger, chaîne, `verify`), Playwright : enrôlement + connexion 2FA |
| (b) `feat(feedback)` | Idées + votes + fusion (domaine), tickets, signalements, `/me/notices`, écran « Idées et contact » (chunk différé), push `ticket.replied`, `report.handled`, préférences | Tests domaine/API, Playwright joueur, budget du jeu inchangé |
| (c) `feat(admin)` | Tableau de bord, joueurs (fiche, liens, sanctions, export, suppression), effets des sanctions sur le jeu (minuit domaine + SQL, différentiel), signalements, tickets, idées | **Playwright : un modérateur traite un signalement de bout en bout et chaque action apparaît dans l'audit** ; tests différentiels du ban |
| (d) `feat(live-config)` | Règles, tempêtes, saisons, feature flags, annonces (segments, aperçu, in-app, push) | **Playwright : un admin programme une tempête et la voit s'appliquer à minuit (horloge simulée)** ; tests des segments |
| (e) `docs` | ADR 0017–0023, architecture, mode dev (admin), README, mise à jour de GAME_DESIGN selon les réponses | CI verte, budgets |

**ADR prévus** (0016 a été pris par le correctif responsive, PR #13, fait avant
l'étape (a) ; l'admin réutilise son système de layout) :
- 0017 : application admin et frontière d'API ;
- 0018 : rôles et matrice de permissions ;
- 0019 : 2FA TOTP, session admin et ré-authentification ;
- 0020 : journal d'audit en ajout seul et chaîné ;
- 0021 : sanctions et révocation des sessions ;
- 0022 : feature flags ;
- 0023 : annonces et segments.

---

## 12. Écarts constatés à la livraison

- **Étape (a)** : le code admin est un dossier `Admin/` de l'API plutôt qu'un projet
  séparé (ADR 0017) ; les ADR sont décalées d'un numéro (0016 = responsive).
- **Étape (b)** : `/me/notices` et les push `ticket.replied` et `report.handled` passent
  à l'étape (c), où naissent les actions de l'équipe qui les déclenchent (réponses,
  décisions, sanctions). L'écran « Idées et contact » charge ses textes avec lui ; pour
  que le jeu ne grossisse pas, le build regroupe tout le code chargé au démarrage dans
  un seul chunk (`apps/game/vite.config.ts`) : sans cela, les modules partagés avec
  l'écran différé étaient découpés en chunks supplémentaires (+1,3 Ko). Résultat :
  −0,25 Ko par rapport à la base.

