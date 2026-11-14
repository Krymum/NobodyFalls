import { useTranslation } from 'react-i18next';
import { Link, useLocation } from 'wouter';
import type { DayStanding } from '../api/types.ts';
import { toneOf, type Tone } from '../core/standing.ts';
import { countdown } from '../core/time.ts';
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

/** Bottom tab bar: discreet, frameless, labels in spaced capitals (the map tab arrives in phase 3). */
export function TabBar() {
  const { t } = useTranslation();
  const [location] = useLocation();
  return (
    <nav className={styles.tabs} aria-label={t('tabs.label')}>
      {TABS.map((tab) => (
        <Link
          key={tab.key}
          href={tab.href}
          className={styles.tab}
          aria-current={location === tab.href ? 'page' : undefined}
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
        </Link>
      ))}
    </nav>
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
