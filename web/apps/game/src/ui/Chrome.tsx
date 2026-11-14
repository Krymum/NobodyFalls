import { isTypingOrModified } from '@nobodyfalls/layout';
import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useLocation } from 'wouter';
import type { DayStanding } from '../api/types.ts';
import { toneOf, type Tone } from '../core/standing.ts';
import { useGame } from '../core/store.ts';
import { countdown } from '../core/time.ts';
import { useLayout } from './useLayout.ts';
import { useServerNow } from './useServerNow.ts';
import styles from './ui.module.css';

/** Brand on the left; night number and the countdown to midnight on the right (opens the World). */
export function Header({ night, midnightAt }: { night: number | null; midnightAt: string | null }) {
  const { t } = useTranslation();
  const now = useServerNow();

  return (
    <header className={styles.header}>
      <span className={styles.brand}>{t('common.brand')}</span>
      {night !== null && midnightAt && (
        <Link href="/world" className={styles.night}>
          {t('common.night', { n: night })} · {countdown(midnightAt, now)}
        </Link>
      )}
    </header>
  );
}

const TABS = [
  { href: '/', key: 'tonight', icon: 'M12 3v6M8 9h8M7 21l5-12 5 12' },
  { href: '/edge', key: 'edge', icon: 'M4 6h16M12 6v9M9 18l3-3 3 3' },
  { href: '/friends', key: 'friends', icon: 'M7 4v7M17 4v7M7 11q5 6 10 0M7 20v-3M17 20v-3' },
  { href: '/profile', key: 'profile', icon: 'M12 4a4 4 0 1 1 0 8 4 4 0 0 1 0-8M5 21c1-4 4-6 7-6s6 2 7 6' },
] as const;

/**
 * Main navigation (the map tab arrives in phase 3). A discreet, frameless tab bar at
 * the bottom on phones and tablets; a vertical rail on the left on desktops, with the
 * brand, the countdown to midnight and the keyboard shortcuts (ART_DIRECTION, "Responsive").
 */
export function Navigation() {
  const { t } = useTranslation();
  const [location, navigate] = useLocation();
  const kind = useLayout();
  const tonight = useGame((s) => s.tonight);
  const now = useServerNow();
  const [help, setHelp] = useState(false);

  // Desktop shortcuts: 1–4 for the sections, W for the world, ? for the list.
  useEffect(() => {
    if (kind !== 'desktop') {
      return undefined;
    }
    const onKey = (event: KeyboardEvent): void => {
      if (isTypingOrModified(event) || document.querySelector('dialog[open]')) {
        return;
      }
      const tab = TABS[Number(event.key) - 1];
      if (tab) {
        navigate(tab.href);
      } else if (event.key === 'w' || event.key === 'W') {
        navigate('/world');
      } else if (event.key === '?') {
        setHelp(true);
      } else {
        return;
      }
      event.preventDefault();
    };
    window.addEventListener('keydown', onKey);
    return () => {
      window.removeEventListener('keydown', onKey);
    };
  }, [kind, navigate]);

  return (
    <nav className={styles.tabs} aria-label={t('tabs.label')}>
      <div className={styles.railTop}>
        <span className={styles.brand}>{t('common.brand')}</span>
        {tonight?.seasonOpen && (
          <Link href="/world" className={styles.night}>
            <span>{t('common.night', { n: tonight.nightNumber })}</span>
            {/* Same name as the header link ("Nuit 3 · 02:14:09"), stacked in the rail. */}
            <span className="nf-sr-only"> · </span>
            <span>{countdown(tonight.midnightAt, now)}</span>
          </Link>
        )}
      </div>
      <ul className={styles.tabList}>
        {TABS.map((tab, index) => (
          <li key={tab.key}>
            <Link
              href={tab.href}
              className={styles.tab}
              aria-current={location === tab.href ? 'page' : undefined}
              aria-keyshortcuts={String(index + 1)}
            >
              <svg width="22" height="22" viewBox="0 0 24 24" aria-hidden="true">
                <path
                  d={tab.icon}
                  fill="none"
                  stroke="currentColor"
                  strokeWidth="1.5"
                  strokeLinecap="round"
                  strokeLinejoin="round"
                />
              </svg>
              {t(`tabs.${tab.key}`)}
              <kbd className="nf-kbd" aria-hidden="true">
                {index + 1}
              </kbd>
            </Link>
          </li>
        ))}
      </ul>
      <div className={styles.railBottom}>
        <button
          type="button"
          className={styles.secondary}
          aria-keyshortcuts="?"
          onClick={() => {
            setHelp(true);
          }}
        >
          <kbd className="nf-kbd" aria-hidden="true">
            ?
          </kbd>
          <span className="nf-sr-only">{t('shortcuts.title')}</span>
        </button>
      </div>
      {help && (
        <ShortcutsHelp
          onClose={() => {
            setHelp(false);
          }}
        />
      )}
    </nav>
  );
}

/** The keyboard shortcuts, listed (desktop). Escape closes it, like any panel. */
function ShortcutsHelp({ onClose }: { onClose: () => void }) {
  const { t } = useTranslation();
  const dialog = useRef<HTMLDialogElement>(null);
  useEffect(() => {
    dialog.current?.showModal();
  }, []);
  const rows: [string, string][] = [
    ['1–4', t('shortcuts.sections')],
    ['W', t('shortcuts.world')],
    ['↑ ↓', t('shortcuts.edgeMove')],
    ['Entrée / Enter', t('shortcuts.edgeHold')],
    ['Échap / Esc', t('shortcuts.close')],
  ];
  return (
    <dialog ref={dialog} className={styles.dialog} aria-labelledby="shortcuts-title" onClose={onClose}>
      <h2 id="shortcuts-title" className={styles.subheading}>
        {t('shortcuts.title')}
      </h2>
      <dl className={styles.shortcuts}>
        {rows.map(([keys, label]) => (
          <div key={keys}>
            <dt>
              <kbd>{keys}</kbd>
            </dt>
            <dd>{label}</dd>
          </div>
        ))}
      </dl>
      <button
        type="button"
        className={styles.secondary}
        onClick={() => {
          dialog.current?.close();
        }}
      >
        {t('common.close')}
      </button>
    </dialog>
  );
}

/** Status icon + text: readable without colour (ART_DIRECTION, "Palette"). */
export function StatusMark({ tone }: { tone: Tone }) {
  if (tone === 'calm') {
    return null;
  }
  return (
    <svg width="10" height="9" viewBox="0 0 12 11" aria-hidden="true">
      {tone === 'danger' && <path d="M6 0 L12 11 L0 11 Z" fill="currentColor" />}
      <path
        d="M6 1 L11 10 L1 10 Z"
        fill="none"
        stroke="currentColor"
        strokeWidth="1.6"
        strokeLinejoin="round"
      />
    </svg>
  );
}

export function toneClass(tone: Tone): string {
  return tone === 'calm' ? '' : (styles[tone] ?? '');
}

export function Standing({ standing, extra }: { standing: DayStanding | null; extra?: string }) {
  const tone = toneOf(standing);
  return (
    <span className={`${styles.status} ${toneClass(tone)}`}>
      <StatusMark tone={tone} />
      {standing ? `${standing.received}/${standing.required}` : ''}
      {extra ? ` · ${extra}` : ''}
    </span>
  );
}
