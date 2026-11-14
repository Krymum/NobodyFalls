# NobodyFalls — Plan de la phase 1

> Statut : **proposition, en attente de validation.** Rien n'est codé.
> Les décisions marquées **[D]** sont celles que je prends par défaut si tu
> ne dis rien ; les points marqués **[Q]** (section 9) ont besoin de ta réponse.
> Références : `GAME_DESIGN.md` (§n) et `ART_DIRECTION.md` (DA).

---

## 1. Arborescence

```
/
├─ NobodyFalls.slnx                     # solution .NET (format slnx)
├─ global.json                          # SDK .NET 10 (LTS)
├─ Directory.Build.props                # Nullable, TreatWarningsAsErrors, analyzers, LangVersion
├─ Directory.Packages.props             # versions NuGet centralisées
├─ .editorconfig  .gitattributes  .gitignore
├─ .env.example
├─ docker-compose.yml                   # postgres, redis, app (API + SPA)
├─ docker-compose.override.yml          # dev : ports exposés, mode dev activé
├─ Dockerfile                           # multi-stage : build web → build api → runtime
│
├─ src/
│  ├─ NobodyFalls.Domain/               # règles pures, zéro dépendance (pas même EF)
│  │  ├─ Common/        (Result, Error, GameDay, PlayerId, Pseudo…)
│  │  ├─ Rules/         (GameRules, ThresholdPolicy, HandPolicy, ScoringPolicy,
│  │  │                  LifeDebtPolicy, WatcherPolicy, StormPolicy)
│  │  ├─ Night/         (DaySnapshot, NightResolver, NightOutcome)  ← implémentation de référence
│  │  ├─ Players/  Friends/  Seasons/
│  │  └─ Skins/         (Slot, SkinItem, Appearance, UnlockCondition, UnlockEvaluator)
│  ├─ NobodyFalls.Application/          # cas d'usage, ports (interfaces), DTO
│  │  ├─ Abstractions/  (IClock, IGameSettingsProvider, IRealtimePublisher,
│  │  │                  INotificationOutbox, ITurnstileVerifier, IUnitOfWork…)
│  │  ├─ Hands/  Tonight/  Edge/  World/  Friends/  Profile/  Registration/
│  │  ├─ Night/         (RunMidnightResolution, RunWatcherPass)
│  │  └─ Dev/           (SeedPlayers, AdvanceClock, SimulateBots)
│  ├─ NobodyFalls.Infrastructure/       # EF Core, SQL de minuit, Redis, WebPush, Turnstile
│  │  ├─ Persistence/   (NobodyFallsDbContext, Configurations/, Migrations/)
│  │  ├─ Night/         (SqlNightResolver + scripts .sql embarqués)
│  │  ├─ Realtime/      (BatchingRealtimePublisher, Redis backplane)
│  │  ├─ Push/          (VapidKeyStore, WebPushSender, OutboxDispatcher)
│  │  ├─ Clock/         (SystemClock, SimulatedClock)
│  │  └─ Settings/      (DbGameSettingsProvider + invalidation Redis pub/sub)
│  ├─ NobodyFalls.Api/                  # Minimal APIs, auth, hub, hosted services, sert la SPA
│  │  ├─ Endpoints/  Hubs/  Auth/  RateLimiting/  Hosting/  Resources/ (.resx FR/EN)
│  │  └─ DevTools/                      # endpoints /dev/* (Development uniquement)
│  └─ (NobodyFalls.Admin.Api/)          # phase 2 — non créé, prévu
│
├─ tests/
│  ├─ NobodyFalls.Domain.Tests/         # xUnit + FsCheck (propriétés)
│  ├─ NobodyFalls.Application.Tests/
│  ├─ NobodyFalls.Infrastructure.Tests/ # Testcontainers Postgres/Redis, tests différentiels minuit, concurrence des dons
│  └─ NobodyFalls.Api.Tests/            # WebApplicationFactory + Testcontainers
│
├─ web/                                 # workspace pnpm
│  ├─ package.json  pnpm-workspace.yaml  tsconfig.base.json  eslint.config.js  .prettierrc
│  ├─ apps/
│  │  ├─ game/                          # le jeu (PWA)
│  │  │  ├─ src/
│  │  │  │  ├─ core/        # logique hors composants : stores, posture, countdown, layout voisinage
│  │  │  │  ├─ api/         # client HTTP typé + client SignalR
│  │  │  │  ├─ screens/     # Onboarding, Tonight, Edge, Friends, Profile, World
│  │  │  │  ├─ ui/          # composants légers (TabBar, Button, Label, FadePanel…)
│  │  │  │  ├─ i18n/        # fr.json, en.json, init
│  │  │  │  └─ sw/          # service worker (cache + push)
│  │  │  ├─ e2e/            # Playwright
│  │  │  └─ public/         # manifest, icônes, polices auto-hébergées (Barlow Condensed, OFL)
│  │  └─ (admin/)                       # phase 2 — bundle séparé, non créé
│  └─ packages/
│     ├─ theme/             # design tokens (TS) → génère tokens.css au build
│     ├─ render/            # CharacterRenderer, RopeRenderer, SceneRenderer, rig, parts
│     └─ api-types/         # types générés depuis l'OpenAPI du back (vérifiés en CI)
│
├─ docs/
│  ├─ GAME_DESIGN.md  ART_DIRECTION.md  reference/
│  ├─ adr/                 # 0001-…md
│  ├─ architecture.md  api.md  realtime.md  night-resolution.md  rendering.md  dev-mode.md
│  └─ plan/PHASE_1_PLAN.md
│
├─ .github/
│  ├─ workflows/ (ci.yml, e2e.yml, gitleaks.yml)
│  ├─ dependabot.yml  ISSUE_TEMPLATE/  pull_request_template.md
└─ README.md  LICENSE (MIT)  CONTRIBUTING.md  CODE_OF_CONDUCT.md  SECURITY.md
```

**[D]** Le conteneur `app` sert l'API **et** la SPA buildée (même origine :
cookies et CORS simples). Le futur `/admin` sera un second bundle servi par
`Admin.Api` sous le même domaine.

---

## 2. Modèle de données (PostgreSQL)

Conventions : `snake_case`, clés `uuid` v7 (triables), timestamps `timestamptz`,
`day` = `date` UTC (journée de jeu). Unicité garantie en base, pas seulement en code.

### Phase 1 (implémenté)

| Table | Colonnes principales | Contraintes / index |
|---|---|---|
| `players` | id, kind (`human`/`watcher`), pseudo (`citext`), status (`waitlisted`/`active`/`fallen`), locale, joined_day, invited_by_id → players, invite_code, guardian_score, falls_count, last_fall_day, returns_on_day null, last_seen_at, created_at, deleted_at, **map_x, map_y, tile_x, tile_y** (nullables, phase 3), map_name_visibility (`friends`/`everyone`/`nobody`) | `unique(pseudo)`, `unique(invite_code)`, index (status, kind) ; l'arbre d'invitation = `invited_by_id` |
| `external_logins` | provider, provider_key, player_id, email_hash | PK (provider, provider_key) |
| `appearances` | player_id PK, skin_tone, slots `jsonb` `{head:{item,color}, top:{…}, scarf, hands, feet}` | validé par le domaine (items possédés, couleurs hors palette de statut) |
| `skin_items` | id (slug), slot, rarity (`base`/`unlockable`/`exclusive`), render_key, color_options `jsonb`, unlock_condition `jsonb` null, active, sort | catalogue configurable sans redéploiement |
| `player_skin_unlocks` | player_id, item_id, day, reason | PK (player_id, item_id) |
| `seasons` | id, number, name, opens_at, ends_at null, status, deaths | une seule saison `open` à la fois (index partiel) |
| `game_days` | day PK, season_id, night_number, base_required, storm, alive_at_start, watcher_count, settings_version, resolved_at, falls, streak_after, deaths_all_time_after | une ligne par journée, créée à la résolution de la veille |
| `player_falls` | player_id, day, season_id | PK (player_id, day) ; historique des chutes (profil, stats admin) |
| `storms` | id, day, created_by, created_at, announced_at | `unique(day)` |
| `player_days` | (day, player_id) PK, required, immune, received, joined_at (arrivée dans la journée : inscription ou retour), outcome null, score_gained | une ligne = un joueur **en jeu** ce jour-là ; index partiel `(day, required - received) where received < required` → Bord du vide ; partitionnable par mois |
| `hands` | id bigint, day, giver_id, receiver_id, slot (1..3), given_at, giver_kind, giver_was_friend, points null, last_rampart null | `unique(giver_id, day, slot)`, `unique(giver_id, receiver_id, day)`, `check(giver_id <> receiver_id)`, index (day, receiver_id, given_at) |
| `life_debts` | day, saved_id, saver_id | PK (day, saved_id, saver_id) |
| `season_scores` | season_id, player_id, points | PK ; sert au classement phase 3 |
| `friendships` | player_low, player_high, source (`request`/`invite`), created_at | PK (low, high), `check(low < high)` |
| `friend_requests` | id, from_id, to_id, status, created_at, answered_at | `unique(from_id, to_id) where status='pending'` |
| `blocks` | blocker_id, blocked_id, created_at | PK ; vérifié dans les deux sens |
| `hold_pairs` | player_low, player_high, nights, last_day | PK ; alimente les suggestions |
| `push_subscriptions` | id, player_id, endpoint, p256dh, auth, created_at, last_success_at, failures | `unique(endpoint)` |
| `notification_preferences` | player_id PK, friend_below, friend_fell, saved_me, night_result, storm | |
| `notification_outbox` | id, dedupe_key, player_id, kind, payload `jsonb`, created_at, sent_at, attempts | `unique(dedupe_key)` → envoi exactement une fois |
| `game_settings` | version PK, document `jsonb`, created_at, created_by, is_current | historique conservé (l'audit phase 2 s'y branche) |
| `data_protection_keys` | standard ASP.NET | clés de cookies partagées entre instances |

### Phase 2 (esquissé, non migré)
`player_roles` (player_id, role), `totp_credentials`, `audit_log` (append-only :
acteur, action, cible, avant/après `jsonb`, ip, at — révocation UPDATE/DELETE au
niveau des droits Postgres), `sanctions`, `reports`, `tickets` + `ticket_messages`,
`ideas` + `idea_votes`, `announcements`, `login_events` (ip/appareil, comptes liés).

### Phase 3 (esquissé)
Positions déjà dans `players` ; `map_tile_aggregates` (tile_x, tile_y, zoom,
counts) rafraîchi à minuit ; `player_titles` ; vues de classement sur
`season_scores` / `player_days.score_gained` ; `appearances.slots` sert déjà
d'« équipement » du vestiaire.

---

## 3. Domaine (cœur des règles)

```csharp
public sealed record GameRules(            // lu depuis game_settings, figé par journée
    int BaseRequired,                      // 2
    int LowPopulationRequired,             // 1
    int LowPopulationBelow,                // 50
    int FragileExtra,                      // +1
    TimeSpan ReturnCutoff,                 // 6 h : retour plus tard → journée suivante
    int StormExtra,                        // +1
    TimeSpan StormNotice,                  // 6 h
    TimeSpan LastRampartWindow,            // 1 h
    int PointsFriend, int PointsStranger,  // 1, 2
    int LastRampartBonus,                  // 2
    WatcherRules Watchers,
    int FriendLimit,                       // 500
    int SuggestionMinNights);              // 2
```

- `ThresholdPolicy.BaseRequired(activePopulation)` = `pop < 50 ? 1 : min(2, pop − 1)`.
- `ThresholdPolicy.Required(base, storm, isSecondNight)`.
- `HandPolicy.CanGive(giver, receiver, today)` → `Result` avec codes d'erreur
  stables (`hand.self`, `hand.duplicate`, `hand.none_left`, `hand.blocked`,
  `hand.season_closed`, `hand.night_resolving`) traduits côté front.
- `NightResolver.Resolve(DaySnapshot) → NightOutcome` : **implémentation de
  référence en C# pur** (chutes, points, Dernier rempart, dettes de vie,
  série, compteurs de morts). Testée unitairement et par propriétés.
- `ReturnPolicy.ReturnDay(fallenPlayer, visitAt)` : journée en cours si
  `minuit − visitAt ≥ ReturnCutoff`, sinon journée suivante.
- `WatcherPolicy.Count(pop)` et `WatcherPolicy.Assign(candidates, watchers)`.
- `UnlockEvaluator` : conditions de skins déclaratives (`rescues >= n`,
  `strangers_held >= n`, `storms_survived >= n`, `season_rank <= n`…).

**[D] ADR 0003** : le SQL ensembliste de minuit (Infrastructure) est vérifié par
des **tests différentiels** : scénarios aléatoires générés (FsCheck) → injectés
en base → `SqlNightResolver` vs `NightResolver` du domaine → résultats
identiques. La règle vit une fois dans le domaine ; le SQL en est
l'implémentation à l'échelle, prouvée équivalente.

---

## 4. Algorithme de minuit

Déclencheur : `NightScheduler` (BackgroundService) qui consulte `IClock` toutes
les 2 s ; dès que `now ≥ D+1 00:00:00Z` et `game_days[D].resolved_at is null`,
il appelle `RunMidnightResolution(D)`. Rattrape les jours manqués dans l'ordre.

Une seule transaction, dans cet ordre :

0. `pg_advisory_xact_lock(NIGHT_KEY)` ; si `resolved_at` non nul → **sortie sans
   effet** (idempotence). Le don de main prend un verrou *partagé* sur la même
   clé de journée : la résolution attend les dons en vol, et un don arrivé après
   reçoit `hand.night_resolving`.
1. Recalcule `player_days.received` depuis `hands` (la source de vérité est
   `hands`, le compteur n'est qu'un cache).
2. `outcome` ensembliste sur les joueurs en jeu ce jour-là (`player_days[D]`) :
   immunisé → `immune` ; received ≥ required → `survived` ; sinon → `fell`.
3. Chutes : `players.status = fallen`, `falls_count + 1`, `last_fall_day = D` ;
   `INSERT player_falls` ; `seasons.deaths += falls`.
4. Points (`UPDATE hands … FROM …`) :
   0 si receveur non `survived/immune`, 0 si donneur Veilleur, 0 si
   réciproque (`EXISTS` main inverse le même jour) ; sinon
   `giver_was_friend ? 1 : 2` + `2` si Dernier rempart
   (`given_at ≥ minuit − 1 h` **et** rang de la main chez le receveur
   (`row_number() over (partition by receiver order by given_at, id)`) = seuil du receveur).
   Agrégation → `player_days.score_gained`, `season_scores`, `players.guardian_score`.
5. Dettes de vie : receveur `survived` avec `received = required` → chaque
   donneur humain est décisif (`INSERT … ON CONFLICT DO NOTHING`).
6. `hold_pairs` : upsert des paires qui se sont tenues (dans un sens ou l'autre).
7. `game_days[D]` : falls, `streak_after = falls = 0 ? streak(D−1)+1 : 0`,
   `deaths_all_time_after = deaths_all_time(D−1) + falls`.
8. Déblocages de skins : une requête ensembliste par type de condition, `ON CONFLICT DO NOTHING`.
9. Préparation de D+1 : population en vie, `base_required`, tempête, nombre de
   Veilleurs, `settings_version` → `INSERT game_days[D+1]` ; `INSERT player_days`
   pour tous les humains `active` (required = base + tempête + 1 si c'est
   leur 2ᵉ nuit) **et** pour les tombés dont `returns_on_day = D+1` (retour
   programmé la veille, seuil normal) — ceux-là repassent `active`.
10. Notifications : lignes `notification_outbox` avec `dedupe_key =
   kind:day:player` (résultat de la nuit, ami tombé — agrégé, « X t'a sauvé »,
   skin débloqué).
11. `resolved_at = now` ; COMMIT. Après commit : `night.resolved` en temps réel,
   réveil du dispatcher de push.

Un crash à n'importe quelle étape ⇒ rollback complet ⇒ rejouable. Le
dispatcher de push est lui aussi idempotent (`sent_at`, `dedupe_key`).

**Passage des Veilleurs** (job séparé, même modèle de verrou, idempotent par
`(day, watcher_pass)`) : à l'heure configurée, ils tiennent les joueurs
concernés (voir Q3), en dons ordinaires (`giver_kind = watcher`).

**Inscription** : crée `player_days[today]` avec `immune = true`.

**Retour d'un tombé** (`ReturnFallenPlayer`, appelé à la première requête
authentifiée d'un joueur `fallen`, idempotent) : si assez de temps avant
minuit → `status = active` + `INSERT player_days[today]` (seuil normal, 0
main) ; sinon → `returns_on_day = today + 1` (repris à l'étape 9). Le front
montre une fois la scène « Tu es tombé cette nuit. Tu reprends ta place. »
Un tombé qui ne revient pas reste dans le vide indéfiniment : il ne compte
pas parmi les vivants et n'ajoute aucune mort.

**Compteurs** : *en vie* = humains `active` (compteur en direct, Redis,
recalé à chaque minuit) ; *morts* = `seasons.deaths` et
`game_days.deaths_all_time_after` (une mort par chute).

---

## 5. Contrats d'API (REST, `/api/v1`, JSON, erreurs `application/problem+json` avec `code`)

Auth et session
| Méthode | Route | Rôle |
|---|---|---|
| GET | `/auth/login/{google\|discord\|dev}?returnUrl=` | challenge OAuth (`dev` = Development uniquement) |
| POST | `/auth/logout` | |
| GET | `/api/v1/session` | `{state: anonymous \| pendingRegistration \| player, player?, csrf}` |
| POST | `/api/v1/registration` | `{pseudo, appearance, locale, inviteCode?, turnstileToken}` |
| GET | `/api/v1/invites/{code}` | public : pseudo + apparence de l'inviteur (onboarding) |

Jeu
| GET | `/api/v1/tonight` | jour, n° de nuit, `serverNow`, `midnightAt`, mon statut (received/required/immune/fragile), mains restantes, qui me tient, qui je tiens, tempête |
|---|---|---|
| POST | `/api/v1/hands` | `{receiverId, turnstileToken}` → `201 {hand, handsLeft, receiver:{received, required}}` |
| GET | `/api/v1/edge?cursor=&limit=` | joueurs sous le seuil, triés par urgence, paginés (curseur) |
| GET | `/api/v1/world` | série, chutes de la dernière nuit, **en vie**, **morts (saison, depuis toujours)**, tempête annoncée, saison |
| GET | `/api/v1/players/{id}` | profil public (404 si blocage) |
| GET | `/api/v1/me/profile` | score Gardien, nombre de chutes, dettes de vie (sauvé par / a sauvé), lien d'invitation, titres (vide) |
| PATCH | `/api/v1/me/settings` | langue, préférences de notification, confidentialité carte |
| PUT | `/api/v1/me/appearance` | items possédés uniquement |
| GET | `/api/v1/skins/catalog` | catalogue + possédés |

Amis
| GET | `/api/v1/friends` | liste + statut du jour |
|---|---|---|
| GET/POST | `/api/v1/friends/requests` | entrantes/sortantes ; POST `{pseudo}` |
| POST | `/api/v1/friends/requests/{id}/accept\|decline` ; DELETE `/{id}` (annuler) | |
| DELETE | `/api/v1/friends/{playerId}` | |
| GET | `/api/v1/friends/suggestions` | « vous vous êtes tenus N nuits » |
| GET/POST/DELETE | `/api/v1/blocks[/{playerId}]` | |

Push
| GET | `/api/v1/push/vapid-key` |
|---|---|
| POST / DELETE | `/api/v1/push/subscriptions` |

Dev (`/dev/*`, uniquement `Development` + `DevTools:Enabled`)
`POST /dev/seed {players, friendsPerPlayer, fallenRatio}` ·
`POST /dev/clock {advance | setTo | toMidnightMinus}` · `GET /dev/clock` ·
`POST /dev/bots/play {ratio}` (des bots donnent leurs mains) ·
`POST /dev/night/run` · `POST /dev/login-as/{playerId}` · panneau dev dans le front.

Sécurité transversale : cookie `__Host-nf` (HttpOnly, Secure, SameSite=Lax),
anti-CSRF par en-tête sur toutes les mutations, Turnstile vérifié serveur
(inscription, don de main), rate limiting partitionné par utilisateur **et**
par IP (politiques : `hands`, `registration`, `friends`, `default`).
Types TS générés depuis l'OpenAPI natif d'ASP.NET (`packages/api-types`), dérive vérifiée en CI.

---

## 6. Hub SignalR `/hubs/game`

- Authentifié (cookie). Transports : WebSockets → SSE → **long polling activé**.
- À la connexion : groupes `player:{id}` et `world`. Backplane Redis.
- **Client → serveur** : `WatchEdge(bool)` (rejoint/quitte `edge`) ;
  phase 3 : `WatchTiles(TileKey[])`. Aucune action de jeu par le hub : les
  dons passent par REST (Turnstile, rate limit, codes HTTP).
- **Serveur → client** : une seule méthode `batch(ServerBatch)`, **au plus 1
  par seconde et par connexion** :

```ts
type ServerBatch = { seq: number; serverTime: string; events: GameEvent[] };
type GameEvent =
  | { type: 'me.state'; received: number; required: number; handsLeft: number }
  | { type: 'me.handReceived'; from: PlayerRef; at: string }
  | { type: 'rope.added'; giverId: string; receiverId: string; at: string } // voisinage
  | { type: 'friend.status'; playerId: string; received: number; required: number; status: PlayerStatus }
  | { type: 'friend.request' | 'friend.accepted'; player: PlayerRef }
  | { type: 'edge.delta'; upserts: EdgeEntry[]; removes: string[] }
  | { type: 'world.stats'; alive: number; underThreshold: number }
  | { type: 'storm.announced'; day: string }
  | { type: 'night.resolved'; day: string };   // le client recharge /tonight
```

`BatchingRealtimePublisher` : les cas d'usage publient des événements dans un
`Channel` ; un flush par seconde coalesce par groupe (dernier `me.state` gagne,
`edge.delta` fusionnés par receveur). `seq` permet au client de détecter un trou
et de recharger.

---

## 7. Front : renderers et thème

### Tokens (`packages/theme`)
Source unique en TS (`palette`, `type`, `space`, `motion`, `light`, `rope`,
`status`) ; un plugin Vite génère `tokens.css` (variables CSS) au build : zéro
CSS au runtime. Test : aucune couleur de vêtement du catalogue ne tombe dans la
zone des couleurs de statut.

### Personnages (`packages/render`)
```ts
type Posture = 'held' | 'attention' | 'danger' | 'relieved' | 'fallen';
type Joint = 'head' | 'neck' | 'shoulderL' | 'shoulderR' | 'elbowL' | 'elbowR'
           | 'handL' | 'handR' | 'hip' | 'hipL' | 'hipR' | 'kneeL' | 'kneeR'
           | 'footL' | 'footR' | 'belt';
interface Skeleton { joints: Record<Joint, Vec2>; headTilt: number; sway: Sway; face: FaceState }

// 1. Pur et testable : posture + temps → squelette (IK coude/genou, balancement)
function solvePose(p: PoseInput, t: number): Skeleton;

// 2. Les parties : le seul point qui change entre prototype et production
interface PartSet {
  readonly layers: readonly LayerId[];          // ordre z : backHair, legs, boots, torso, belt, neck,
                                                // head, face, headwear, scarf, arms, hands
  draw(ctx: Ctx2D, layer: LayerId, skel: Skeleton, look: ResolvedLook, mode: 'full' | 'silhouette'): void;
}
class CodePartSet implements PartSet {}         // phase 1 : reproduit scene-demo à l'identique
class AtlasPartSet implements PartSet {}        // plus tard : sprites + pivots aux mêmes joints

class CharacterRenderer {
  constructor(parts: PartSet, theme: Theme);
  draw(ctx: Ctx2D, c: CharacterInstance, t: number): Anchors; // renvoie mains + ceinture pour les cordes
}
```
`ResolvedLook` = apparence (données) résolue via le catalogue → `render_key`s
+ couleurs. Les skins ne sont que des données.

### Cordes
```ts
interface RopeSpec { from: Vec2; to: Vec2; tension: number /*0 lâche → 1 tendue*/; width: number;
                     progress?: number /*lancer 0..1*/; alpha?: number; emphasis?: boolean }
class RopeRenderer {
  rope(ctx, spec): Vec2;        // fibres torsadées, ombre, reflet ; flèche dérivée de la tension
  thread(ctx, from, to): void;  // fil effiloché (danger)
  coil(ctx, belt, count): void; // mains libres enroulées à la hanche
}
```
Lancer : `ThrowAnimation` (0,8 s, ease-out cubique) pilote `progress` et `tension`.

### Scène
`SceneRenderer` : fond, puits de lumière, plan lointain, plan moyen, bancs de
brume, distribution principale, vide, poussière, vignette.
**[D] ADR 0012** : pas de `ctx.filter` (Safari) : chaque silhouette lointaine
est pré-rendue **une fois** floutée (rendu basse résolution + upscale, ou
box-blur logiciel) dans un canvas hors écran, puis dessinée avec sa transformation
de balancement. Boucle rAF stoppée onglet caché, DPR ≤ 2, `prefers-reduced-motion` → image fixe.

`core/neighborhood.ts` (pur) : place « toi » au centre, ceux qui te tiennent
au-dessus, ceux que tu tiens en dessous, mains libres en rouleaux ; au-delà de 4
par côté, figures en arrière-plan + « +N ». Un test vérifie que la configuration
de `scene-demo` reproduit les positions de référence ; un test visuel Playwright
compare la scène à une capture de référence.

### Pile front
React 19 + TS strict, Vite, **wouter** (routeur ~2 Ko), **zustand** (stores
hors composants), i18next + react-i18next (ICU pluriels, `Intl` dates/nombres),
@microsoft/signalr chargé après le premier rendu, CSS Modules, vite-plugin-pwa
(`injectManifest`, SW maison pour le push), polices auto-hébergées.
Budget estimé initial : React ~60 Ko + i18n ~20 + routeur/store ~4 + app ~35
≈ 120 Ko gzip (signalr en chunk différé). `size-limit` en CI.

---

## 8. Découpage en PR

| PR | Contenu | Preuve que ça tourne |
|---|---|---|
| (a) `feat(domain)` | solution, Directory.*, Domain complet, tests xUnit + propriétés | `dotnet test` |
| (b) `feat(persistence)` | EF Core + migrations, settings en base, horloge, SQL de minuit, Veilleurs, tests différentiels + concurrence des dons (Testcontainers), docker-compose Postgres/Redis | `dotnet test`, `docker compose up db redis` |
| (c) `feat(api)` | Minimal APIs, OAuth Google/Discord + dev login, Turnstile, rate limiting, amis, blocage, invitations, endpoints dev, OpenAPI | tests API, `docker compose up` |
| (d) `feat(realtime)` | hub, batcher, backplane, outbox, Web Push VAPID, ressources .resx | tests d'intégration hub + outbox |
| (e) `feat(render)` | workspace web, tokens, renderers, page de démo `/dev/scene` | Vitest + capture Playwright vs référence |
| (f) `feat(web)` | écrans, barre d'onglets, i18n FR/EN, PWA, panneau dev | Playwright de bout en bout (3 moteurs + mobile) |
| (g) `chore(ci)` | workflows, budgets, Lighthouse CI, gitleaks, dependabot, README, docs, fichiers communautaires | CI verte |

ADRs prévus : 0001 ADR · 0002 couches .NET · 0003 minuit ensembliste + référence domaine ·
0004 concurrence des dons · 0005 configuration en base figée par journée ·
0006 horloge simulable · 0007 auth et session · 0008 temps réel par lots ·
0009 outbox et Web Push · 0010 pile front et budgets · 0011 rig + PartSet ·
0012 flou sans `ctx.filter` · 0013 i18n · 0014 monorepo et types générés · 0015 Veilleurs.

---

## 9. Points ambigus — à trancher

### Tranché (13/11/2026)
- **Chute et retour** : la chute n'enlève rien (score, amis, skins) et compte
  une mort. Le tombé revient automatiquement à sa prochaine visite, au seuil
  normal. La remontée par 5 mains est supprimée. Un tombé absent reste dans
  le vide et ne compte ni comme vivant ni comme nouvelle mort.
- **Compteurs** : en vie (en direct) ; morts par saison et depuis toujours,
  une par chute ; nombre de chutes sur le profil.
- **[D]** Un retour à moins de 6 h de minuit (configurable) prend effet à la
  journée suivante, pour ne pas faire retomber aussitôt le joueur.
- **Q1 résolue** : « joueur actif » = « en vie » = humain `active`, saison
  ouverte, non banni.

### Encore ouvert

**Q2. Push « un ami passe sous le seuil ».** Tout le monde repart à 0 main
chaque jour à 00:00 UTC : à la lettre, chaque ami serait « sous le seuil »
chaque matin. Proposition : un seul push par jour, à une heure configurable
(défaut 21:00 UTC, 3 h avant minuit), listant les amis encore sous le seuil.

**Q3. Veilleurs.** Propositions à valider :
- leurs mains comptent pour le seuil (sinon ils ne servent à rien) ;
- ils interviennent à une heure configurable (défaut 22:00 UTC), sur les
  joueurs actifs à **0 main** (lecture littérale de « sans main »), nouveaux
  joueurs en priorité ;
- 3 mains chacun par jour, ne reçoivent jamais de mains, ne tombent pas ;
- nombre = `clamp(ceil(max × (1 − pop / disparition)), 0, max)`
  (défauts : max 10, disparition à 200 actifs) ;
- 0 point pour eux, 0 dette de vie enregistrée (« sauvé par un Veilleur » non stocké).

**Q4. Réciprocité et Dernier rempart.** Une main réciproque donnée dans la
dernière heure et qui fait atteindre le seuil : 0 au total (proposition, pour
éviter les arrangements à deux) ou bonus de +2 conservé ?

**Q5. Statut d'ami pour les points.** Proposition : figé **au moment du don**
(sinon on gagne +1 en retirant un ami avant minuit).

**Q6. Saisons.** Phase 1 : liste d'attente avant l'ouverture (on crée son
personnage, ajoute des amis, partage son lien, mais pas de main), puis
ouverture simultanée. Propositions :
- les inscrits de la liste d'attente suivent la règle des fragiles à partir de
  l'ouverture (immunisés la 1ʳᵉ nuit, +1 la 2ᵉ) ;
- fin de saison : je modélise `ends_at` et je fige les scores de saison, mais
  je ne décide pas d'un « reset » (les tombés redeviennent-ils actifs à la
  saison suivante ?) — à préciser maintenant ou en phase 3 ?

**Q7. Tempête.** Propositions : programmation refusée à moins de 6 h du minuit
visé ; le +1 se cumule avec le +1 des fragiles.

**Q8.** *(sans objet : plus de remontée par les mains.)*

**Q9. Joueurs immunisés (1ʳᵉ nuit) dans le Bord du vide.** Proposition : ils y
figurent, étiquetés « première nuit », triés après les vraies urgences —
pour que les nouveaux reçoivent des mains. Ok ?

**Q10. Onglet « Carte ».** Proposition : masqué par feature flag en phase 1
(4 onglets : Ce soir, Bord du vide, Amis, Profil) ; Monde accessible depuis
l'en-tête « NUIT 41 » de Ce soir. Tu préfères un onglet Monde à la place ?

### Décisions prises par défaut [D] (signale si tu n'es pas d'accord)
- Seuil, tempête et Veilleurs d'un jour sont **figés à la résolution de la
  veille** ; un changement de config s'applique à la journée suivante.
- Dette de vie : main décisive ⇔ le receveur a exactement le seuil
  (alors toutes ses mains humaines sont décisives).
- Dernier rempart : fenêtre 23:00:00–23:59:59 UTC ; jamais pour un receveur immunisé.
- Une tempête peut être programmée pour le jour même jusqu'à 18:00 UTC ; elle
  relève alors les seuils déjà fixés pour la journée (seule exception au gel).
- « X t'a sauvé » est notifié quel que soit le lien (ami ou non).
- Tri du Bord du vide : déficit décroissant, puis mains reçues croissantes,
  puis ordre pseudo-aléatoire stable par jour (pour répartir l'aide au lieu de
  tout concentrer sur le haut de liste).
- Pseudo : 3–20 caractères, lettres Unicode, chiffres, `_` et `.`, unique sans
  tenir compte de la casse ni des accents.
- Un compte peut lier Google **et** Discord.
- Turnstile en mode invisible sur chaque don (vérifié serveur).
- Rate limiting en mémoire par instance en phase 1 (une instance), interface
  prête pour un limiteur Redis.
- Hors dev, sans clés OAuth configurées, le fournisseur est simplement masqué ;
  en dev, un « dev login » et les clés de test Turnstile permettent la boucle
  complète sans compte externe.
- Les positions sur la carte des inscrits de phase 1 seront attribuées en
  phase 3 par un backfill (ordre d'inscription + arbre d'invitation).
