import { prefersReducedMotion, startRenderLoop } from '@nobodyfalls/render';
import { useEffect, useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'wouter';
import type { PlayerRef, Tonight as TonightView } from '../../api/types.ts';
import {
  layoutNeighbourhood,
  ROWS,
  SCENE_HEIGHT,
  SCENE_WIDTH,
  type Neighbourhood,
  type Person,
} from '../../core/neighbourhood.ts';
import { myState, type MyState } from '../../core/standing.ts';
import { errorKey, game, useGame } from '../../core/store.ts';
import { isLastHour } from '../../core/time.ts';
import { Header, StatusMark, toneClass } from '../../ui/Chrome.tsx';
import ui from '../../ui/ui.module.css';
import { useServerNow } from '../../ui/useServerNow.ts';
import styles from './Tonight.module.css';
import { createTonightScene, type TonightScene } from './tonightScene.ts';

/** "Ce soir": my status, my hands, my neighbourhood, full screen (GAME_DESIGN §10.2). */
export function Tonight() {
  const { t } = useTranslation();
  const tonight = useGame((s) => s.tonight);
  const session = useGame((s) => s.session);
  const renderKeys = useGame((s) => s.renderKeys);
  const top = useGame((s) => s.edge.entries[0] ?? null);
  const world = useGame((s) => s.world);
  const lastThrow = useGame((s) => s.lastThrow);
  const pending = useGame((s) => s.pending);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    void game.loadTonight();
    void game.loadEdge();
    void game.loadWorld();
  }, []);

  const me: PlayerRef | null = session?.player
    ? {
        id: session.player.id,
        pseudo: session.player.pseudo,
        isWatcher: false,
        appearance: session.player.appearance,
      }
    : null;
  const neighbourhood = useMemo(
    () => (tonight && me ? layoutNeighbourhood(tonight, me, renderKeys, top) : null),
    // `me` is rebuilt each render from the session; its id and look are what matter.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [tonight, me?.id, me?.appearance, renderKeys, top],
  );

  const hold = async (person: PlayerRef): Promise<void> => {
    setError(null);
    try {
      await game.hold(person.id);
    } catch (e) {
      setError(errorKey(e));
    }
  };

  if (!tonight || !neighbourhood) {
    return <div className={styles.stage} aria-busy="true" />;
  }

  const state = myState(tonight);
  const candidate = neighbourhood.candidate;
  return (
    <div className={styles.stage}>
      <TonightCanvas
        neighbourhood={neighbourhood}
        label={describe(t, tonight, candidate)}
        lastThrowKey={lastThrow}
      />
      <Header night={tonight.nightNumber} midnightAt={tonight.seasonOpen ? tonight.midnightAt : null} />
      <Labels neighbourhood={neighbourhood} />

      <section className={styles.panel} aria-live="polite">
        <span className={ui.label}>{statusLine(t, tonight, state)}</span>
        <h1 className={styles.title}>{t(`tonight.title.${state}`)}</h1>
        {sublines(t, tonight, state, candidate).map((line) => (
          <p key={line} className={ui.text}>
            {line}
          </p>
        ))}
        <LastHourHint midnightAt={tonight.midnightAt} active={tonight.handsLeft > 0 && tonight.seasonOpen} />
        {error && (
          <p className={ui.notice} role="alert">
            {t(error)}
          </p>
        )}
        {candidate?.player && (
          <button
            type="button"
            className={ui.primary}
            disabled={pending.has(candidate.id)}
            onClick={() => {
              if (candidate.player) {
                void hold(candidate.player);
              }
            }}
          >
            {t(tonight.handsLeft === 1 ? 'tonight.holdCandidateLast' : 'tonight.holdCandidate', {
              name: candidate.player.pseudo,
            })}
          </button>
        )}
        <Link href="/edge" className={ui.secondary}>
          {world && world.underThreshold > 0
            ? t('tonight.toEdge', { count: world.underThreshold })
            : t('tonight.toEdgeEmpty')}
        </Link>
      </section>
    </div>
  );
}

type T = ReturnType<typeof useTranslation>['t'];

function statusLine(t: T, tonight: TonightView, state: MyState): string {
  const standing = tonight.standing;
  switch (state) {
    case 'held':
    case 'attention':
      return t(`tonight.status.${state}`, {
        count: standing?.received ?? 0,
        required: standing?.required ?? 0,
      });
    case 'danger':
      return t('tonight.status.danger', { required: standing?.required ?? 0 });
    default:
      return t(`tonight.status.${state}`);
  }
}

function sublines(t: T, tonight: TonightView, state: MyState, candidate: Person | null): string[] {
  const lines: string[] = [];
  const required = tonight.standing?.required ?? 0;
  if (tonight.returned) {
    lines.push(t(`tonight.${tonight.returned}`, { required }));
  } else if (tonight.fellOn && state !== 'fallen') {
    lines.push(t('tonight.fellOn'));
  }
  if (state === 'firstNight') {
    lines.push(t('tonight.firstNightBody', { required: required + 1 }));
  }
  if (state === 'waitlisted') {
    lines.push(t('tonight.waitlistedBody'));
  }
  if (tonight.storm) {
    lines.push(t('tonight.storm'));
  }
  if (tonight.seasonOpen && state !== 'waitlisted') {
    const hands =
      tonight.handsLeft === 0
        ? t('tonight.handsLeft_zero')
        : t('tonight.handsLeft', { count: tonight.handsLeft });
    const about = candidate?.player
      ? t(candidate.posture === 'danger' ? 'tonight.candidate' : 'tonight.candidateAttention', {
          name: candidate.player.pseudo,
        })
      : '';
    lines.push(`${hands} ${about}`.trim());
  }
  return lines;
}

function describe(t: T, tonight: TonightView, candidate: Person | null): string {
  const holders = tonight.heldBy.map((h) => h.player.pseudo).join(', ');
  const held = tonight.holding.map((h) => h.player.pseudo).join(', ');
  return [
    holders ? t('tonight.sceneHeld', { holders }) : t('tonight.sceneAlone'),
    held ? t('tonight.sceneHolding', { held }) : '',
    candidate?.player ? t('tonight.sceneCandidate', { name: candidate.player.pseudo }) : '',
  ]
    .filter(Boolean)
    .join(' ');
}

function LastHourHint({ midnightAt, active }: { midnightAt: string; active: boolean }) {
  const { t } = useTranslation();
  const now = useServerNow();
  return active && isLastHour(midnightAt, now) ? <p className={ui.text}>{t('tonight.lastHour')}</p> : null;
}

/** Names floating next to each character, positioned like the reference scene. */
function Labels({ neighbourhood }: { neighbourhood: Neighbourhood }) {
  const { t } = useTranslation();
  return (
    <div className={styles.labels} aria-hidden="true">
      {neighbourhood.people.map((person) => {
        const left = person.labelSide === 'left';
        const name =
          person.role === 'me'
            ? t('common.me')
            : person.player?.isWatcher
              ? t('common.watcher')
              : (person.player?.pseudo ?? '').toUpperCase();
        const detail =
          person.role === 'candidate' && person.posture === 'danger' ? t('common.thread') : person.count;
        const tone = person.role === 'me' ? 'calm' : person.tone;
        return (
          <span
            key={person.id}
            className={`${styles.name} ${person.role === 'me' ? styles.me : ''} ${toneClass(tone)}`}
            style={{
              top: `${((person.y + person.labelDy) / SCENE_HEIGHT) * 100}%`,
              ...(left
                ? { right: `${((SCENE_WIDTH - person.x + 26 * person.scale) / SCENE_WIDTH) * 100}%` }
                : { left: `${((person.x + 22 * person.scale) / SCENE_WIDTH) * 100}%` }),
            }}
          >
            <StatusMark tone={tone} />
            {name}
            {detail && person.role !== 'holder' ? ` · ${detail}` : ''}
          </span>
        );
      })}
      {neighbourhood.hiddenHolders > 0 && (
        <span
          className={styles.name}
          style={{ top: `${((ROWS.holders + 40) / SCENE_HEIGHT) * 100}%`, right: '4%' }}
        >
          {t('tonight.more', { count: neighbourhood.hiddenHolders })}
        </span>
      )}
      {neighbourhood.hiddenHeld > 0 && (
        <span
          className={styles.name}
          style={{ top: `${((ROWS.held + 40) / SCENE_HEIGHT) * 100}%`, left: '4%' }}
        >
          {t('tonight.more', { count: neighbourhood.hiddenHeld })}
        </span>
      )}
    </div>
  );
}

function TonightCanvas({
  neighbourhood,
  label,
  lastThrowKey,
}: {
  neighbourhood: Neighbourhood;
  label: string;
  lastThrowKey: ReturnType<typeof useGame.getState>['lastThrow'];
}) {
  const canvas = useRef<HTMLCanvasElement>(null);
  const scene = useRef<TonightScene | null>(null);
  const redraw = useRef<(() => void) | null>(null);

  useEffect(() => {
    const element = canvas.current;
    if (!element) {
      return undefined;
    }
    const created = createTonightScene();
    scene.current = created;
    const reduced = prefersReducedMotion();
    let latest: Parameters<Parameters<typeof startRenderLoop>[1]>[0] | null = null;
    redraw.current = () => {
      if (latest) {
        created.draw(latest);
      }
    };
    return startRenderLoop(
      element,
      (frame) => {
        latest = frame;
        created.draw(frame);
      },
      {
        reducedMotion: reduced,
        onResize: (w, h, ratio) => {
          created.resize(w, h, ratio);
        },
      },
    );
  }, []);

  useEffect(() => {
    scene.current?.show(neighbourhood, lastThrowKey, prefersReducedMotion());
    // With reduced motion the loop is still: draw the new state once.
    redraw.current?.();
  }, [neighbourhood, lastThrowKey]);

  return <canvas ref={canvas} className={styles.canvas} role="img" aria-label={label} />;
}
