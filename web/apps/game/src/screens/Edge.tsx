import { isTypingOrModified } from '@nobodyfalls/layout';
import { DEFAULT_LOOK, resolveLook } from '@nobodyfalls/render';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { EdgeEntry } from '../api/types.ts';
import { phaseOf, postureOf } from '../core/standing.ts';
import { errorKey, game, useGame } from '../core/store.ts';
import { CharacterCanvas } from '../ui/CharacterCanvas.tsx';
import { Header, Standing } from '../ui/Chrome.tsx';
import ui from '../ui/ui.module.css';
import { useLayout } from '../ui/useLayout.ts';
import styles from './screens.module.css';

/**
 * The Edge of the void: players below their threshold, most urgent first (the
 * server shuffles ties per day so help spreads out). Live while the screen is open.
 * On desktops, the list sits next to a preview of the chosen player, and the arrow
 * keys and Enter work the list (ART_DIRECTION, "Responsive").
 */
export function Edge() {
  const { t } = useTranslation();
  const edge = useGame((s) => s.edge);
  const tonight = useGame((s) => s.tonight);
  const desktop = useLayout() === 'desktop';
  const [error, setError] = useState<string | null>(null);
  const [selectedId, setSelectedId] = useState<string | null>(null);

  useEffect(() => {
    void game.loadEdge();
    void game.loadTonight();
    game.watchEdge(true);
    return () => {
      game.watchEdge(false);
    };
  }, []);

  const handsLeft = tonight?.handsLeft ?? 0;
  const entries = edge.entries;
  const selected = entries.find((e) => e.player.id === selectedId) ?? entries[0] ?? null;

  const hold = (entry: EdgeEntry): void => {
    setError(null);
    game.hold(entry.player.id).catch((e: unknown) => {
      setError(errorKey(e));
    });
  };

  // Desktop keyboard: ↑ ↓ choose, Enter holds the chosen player.
  useEffect(() => {
    if (!desktop) {
      return undefined;
    }
    const onKey = (event: KeyboardEvent): void => {
      if (isTypingOrModified(event) || document.querySelector('dialog[open]') || !selected) {
        return;
      }
      const index = entries.indexOf(selected);
      if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
        const next =
          entries[Math.min(entries.length - 1, Math.max(0, index + (event.key === 'ArrowDown' ? 1 : -1)))];
        if (next) {
          setSelectedId(next.player.id);
          document.getElementById(`edge-${next.player.id}`)?.scrollIntoView({ block: 'nearest' });
        }
        event.preventDefault();
      } else if (
        event.key === 'Enter' &&
        !(event.target instanceof HTMLButtonElement || event.target instanceof HTMLAnchorElement)
      ) {
        if (handsLeft > 0) {
          hold(selected);
        }
        event.preventDefault();
      }
    };
    window.addEventListener('keydown', onKey);
    return () => {
      window.removeEventListener('keydown', onKey);
    };
  });

  return (
    <div>
      <Header
        night={tonight?.nightNumber ?? null}
        midnightAt={tonight?.seasonOpen ? tonight.midnightAt : null}
      />
      <main className={`${ui.page} ${styles.edgePage}`}>
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

        <div className={styles.edgeGrid}>
          <div>
            {edge.loaded && entries.length === 0 && <p className={ui.text}>{t('edge.empty')}</p>}
            <ul className={ui.list}>
              {entries.map((entry) => (
                <EdgeRow
                  key={entry.player.id}
                  entry={entry}
                  canHold={handsLeft > 0}
                  selected={desktop && selected === entry}
                  onSelect={() => {
                    setSelectedId(entry.player.id);
                  }}
                  onHold={() => {
                    hold(entry);
                  }}
                />
              ))}
            </ul>
            {edge.nextOffset !== null && (
              <button type="button" className={ui.secondary} onClick={() => void game.loadEdge(true)}>
                {t('edge.more')}
              </button>
            )}
          </div>
          {desktop && selected && <EdgePreview entry={selected} canHold={handsLeft > 0} onHold={hold} />}
        </div>
      </main>
    </div>
  );
}

function useLook(entry: EdgeEntry) {
  const renderKeys = useGame((s) => s.renderKeys);
  return entry.player.appearance ? resolveLook(entry.player.appearance, renderKeys) : DEFAULT_LOOK;
}

function tagsOf(t: ReturnType<typeof useTranslation>['t'], entry: EdgeEntry): string {
  return [entry.isFriend ? t('common.friend') : null, entry.immune ? t('common.firstNight') : null]
    .filter(Boolean)
    .join(' · ');
}

function EdgeRow({
  entry,
  canHold,
  selected,
  onSelect,
  onHold,
}: {
  entry: EdgeEntry;
  canHold: boolean;
  selected: boolean;
  onSelect: () => void;
  onHold: () => void;
}) {
  const { t } = useTranslation();
  const look = useLook(entry);
  const pending = useGame((s) => s.pending.has(entry.player.id));
  const standing = { received: entry.received, required: entry.required, immune: entry.immune };
  const tags = tagsOf(t, entry);

  return (
    // Choosing a row only changes the desktop preview (the keyboard uses ↑ ↓); the button is the action.
    <li id={`edge-${entry.player.id}`} className={ui.row} data-selected={selected} onClick={onSelect}>
      <CharacterCanvas look={look} posture={postureOf(standing)} phase={phaseOf(entry.player.id)} size={64} />
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
        onClick={(event) => {
          event.stopPropagation();
          onSelect();
          onHold();
        }}
      >
        {pending ? t('edge.holding') : t('edge.hold')}
      </button>
    </li>
  );
}

/** Desktop: the chosen player, large and swaying, with the same action as the list. */
function EdgePreview({
  entry,
  canHold,
  onHold,
}: {
  entry: EdgeEntry;
  canHold: boolean;
  onHold: (entry: EdgeEntry) => void;
}) {
  const { t } = useTranslation();
  const look = useLook(entry);
  const pending = useGame((s) => s.pending.has(entry.player.id));
  const standing = { received: entry.received, required: entry.required, immune: entry.immune };
  const tags = tagsOf(t, entry);
  return (
    <aside className={styles.edgePreview} aria-label={entry.player.pseudo} aria-live="polite">
      <CharacterCanvas
        look={look}
        posture={postureOf(standing)}
        phase={phaseOf(entry.player.id)}
        size={300}
        animated
        label={entry.player.pseudo}
      />
      <p className={ui.name}>{entry.player.pseudo}</p>
      <Standing standing={standing} {...(tags ? { extra: tags } : {})} />
      <button
        type="button"
        className={ui.primary}
        disabled={!canHold || pending}
        aria-keyshortcuts="Enter"
        onClick={() => {
          onHold(entry);
        }}
      >
        {pending ? t('edge.holding') : t('edge.hold')}{' '}
        <kbd className="nf-kbd" aria-hidden="true">
          ↵
        </kbd>
      </button>
    </aside>
  );
}
