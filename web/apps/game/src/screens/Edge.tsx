import { DEFAULT_LOOK, resolveLook } from '@nobodyfalls/render';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { EdgeEntry } from '../api/types.ts';
import { phaseOf, postureOf } from '../core/standing.ts';
import { errorKey, game, useGame } from '../core/store.ts';
import { CharacterCanvas } from '../ui/CharacterCanvas.tsx';
import { Header, Standing } from '../ui/Chrome.tsx';
import ui from '../ui/ui.module.css';

/**
 * The Edge of the void: players below their threshold, most urgent first (the
 * server shuffles ties per day so help spreads out). Live while the screen is open.
 */
export function Edge() {
  const { t } = useTranslation();
  const edge = useGame((s) => s.edge);
  const tonight = useGame((s) => s.tonight);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    void game.loadEdge();
    void game.loadTonight();
    game.watchEdge(true);
    return () => {
      game.watchEdge(false);
    };
  }, []);

  const handsLeft = tonight?.handsLeft ?? 0;
  return (
    <div>
      <Header
        night={tonight?.nightNumber ?? null}
        midnightAt={tonight?.seasonOpen ? tonight.midnightAt : null}
      />
      <main className={ui.page}>
        <div className={ui.section}>
          <h1 className={ui.heading}>{t('edge.title')}</h1>
          <p className={ui.text}>{t('edge.intro')}</p>
          <p className={ui.label} aria-live="polite">
            {handsLeft > 0 ? t('edge.handsLeft', { count: handsLeft }) : t('edge.noHands')}
          </p>
          {error && (
            <p className={ui.notice} role="alert">
              {t(error)}
            </p>
          )}
        </div>

        {edge.loaded && edge.entries.length === 0 && <p className={ui.text}>{t('edge.empty')}</p>}

        <ul className={ui.list}>
          {edge.entries.map((entry) => (
            <EdgeRow
              key={entry.player.id}
              entry={entry}
              canHold={handsLeft > 0}
              onError={(key) => {
                setError(key);
              }}
            />
          ))}
        </ul>

        {edge.nextOffset !== null && (
          <button type="button" className={ui.secondary} onClick={() => void game.loadEdge(true)}>
            {t('edge.more')}
          </button>
        )}
      </main>
    </div>
  );
}

function EdgeRow({
  entry,
  canHold,
  onError,
}: {
  entry: EdgeEntry;
  canHold: boolean;
  onError: (key: string | null) => void;
}) {
  const { t } = useTranslation();
  const renderKeys = useGame((s) => s.renderKeys);
  const pending = useGame((s) => s.pending.has(entry.player.id));
  const standing = { received: entry.received, required: entry.required, immune: entry.immune };
  const tags = [entry.isFriend ? t('common.friend') : null, entry.immune ? t('common.firstNight') : null]
    .filter(Boolean)
    .join(' · ');

  return (
    <li className={ui.row}>
      <CharacterCanvas
        look={entry.player.appearance ? resolveLook(entry.player.appearance, renderKeys) : DEFAULT_LOOK}
        posture={postureOf(standing)}
        phase={phaseOf(entry.player.id)}
        size={64}
      />
      <div className={ui.rowBody}>
        <span className={ui.name}>{entry.player.pseudo}</span>
        <Standing standing={standing} {...(tags ? { extra: tags } : {})} />
      </div>
      <button
        type="button"
        className={ui.primary}
        style={{ height: 44 }}
        disabled={!canHold || pending}
        aria-label={`${t('edge.hold')} ${entry.player.pseudo}`}
        onClick={() => {
          onError(null);
          game.hold(entry.player.id).catch((e: unknown) => {
            onError(errorKey(e));
          });
        }}
      >
        {pending ? t('edge.holding') : t('edge.hold')}
      </button>
    </li>
  );
}
