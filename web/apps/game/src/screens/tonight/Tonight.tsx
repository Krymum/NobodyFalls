import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'wouter';
import type { PlayerRef, Tonight as TonightView } from '../../api/types.ts';
import type { Person } from '../../core/neighbourhood.ts';
import { myState, type MyState } from '../../core/standing.ts';
import { errorKey, game, useGame } from '../../core/store.ts';
import { isLastHour } from '../../core/time.ts';
import { Header } from '../../ui/Chrome.tsx';
import ui from '../../ui/ui.module.css';
import { useServerNow } from '../../ui/useServerNow.ts';
import { useNeighbourhood } from './SceneLayer.tsx';
import styles from './Tonight.module.css';

/**
 * "Ce soir": my status and my hands (GAME_DESIGN §10.2). My neighbourhood is drawn
 * full screen behind it by the shell's SceneLayer; this is the text panel.
 */
export function Tonight() {
  const { t } = useTranslation();
  const tonight = useGame((s) => s.tonight);
  const world = useGame((s) => s.world);
  const pending = useGame((s) => s.pending);
  const neighbourhood = useNeighbourhood();
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    void game.loadTonight();
    void game.loadEdge();
    void game.loadWorld();
  }, []);

  const hold = async (person: PlayerRef): Promise<void> => {
    setError(null);
    try {
      await game.hold(person.id);
    } catch (e) {
      setError(errorKey(e));
    }
  };

  if (!tonight || !neighbourhood) {
    return <div className={styles.screen} aria-busy="true" />;
  }

  const state = myState(tonight);
  const candidate = neighbourhood.candidate;
  return (
    // The scene itself is drawn behind this text by the shell's SceneLayer.
    <div className={styles.screen}>
      <Header night={tonight.nightNumber} midnightAt={tonight.seasonOpen ? tonight.midnightAt : null} />
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

function LastHourHint({ midnightAt, active }: { midnightAt: string; active: boolean }) {
  const { t } = useTranslation();
  const now = useServerNow();
  return active && isLastHour(midnightAt, now) ? <p className={ui.text}>{t('tonight.lastHour')}</p> : null;
}
