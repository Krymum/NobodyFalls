import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, Route, Switch, useLocation } from 'wouter';
import type { AdminSession, Locale } from '../api.ts';
import { changeLocale, LOCALES } from '../i18n/index.ts';
import { useAdmin } from '../store.ts';
import styles from '../styles/admin.module.css';
import { Audit } from './Audit.tsx';
import { Home } from './Home.tsx';
import { Team } from './Team.tsx';

interface Section {
  path: string;
  label: string;
  permission: string | null;
}

/** Sections appear only when the member's permissions allow them (the server checks anyway). */
const SECTIONS: readonly Section[] = [
  { path: '/', label: 'nav.home', permission: null },
  { path: '/team', label: 'nav.team', permission: 'staff.manage' },
  { path: '/audit', label: 'nav.audit', permission: 'audit.view' },
];

export function Shell({ session }: { session: AdminSession }) {
  const { t, i18n } = useTranslation();
  const signOut = useAdmin((s) => s.signOut);
  const [location] = useLocation();
  const [menuOpen, setMenuOpen] = useState(false);
  const sections = SECTIONS.filter(
    (s) => s.permission === null || session.permissions.includes(s.permission),
  );
  const allowed = (permission: string) => session.permissions.includes(permission);

  return (
    <div className={styles.shell}>
      <a className={styles.skip} href="#content">
        {t('app.skip')}
      </a>
      <header className={styles.topbar}>
        <span className={styles.brand}>NobodyFalls · {t('app.title')}</span>
        <button
          type="button"
          className={styles.menuButton}
          aria-expanded={menuOpen}
          aria-controls="admin-nav"
          onClick={() => {
            setMenuOpen(!menuOpen);
          }}
        >
          {t('nav.menu')}
        </button>
      </header>
      <nav id="admin-nav" className={styles.sidebar} data-open={menuOpen} aria-label={t('nav.label')}>
        <p className={styles.brandLarge}>NobodyFalls</p>
        <p className={styles.label}>{t('app.title')}</p>
        <ul className={styles.navList}>
          {sections.map((s) => (
            <li key={s.path}>
              <Link
                href={s.path}
                className={styles.navLink}
                aria-current={location === s.path ? 'page' : undefined}
                onClick={() => {
                  setMenuOpen(false);
                }}
              >
                {t(s.label)}
              </Link>
            </li>
          ))}
        </ul>
        <div className={styles.navFooter}>
          <p className={styles.muted}>
            {session.pseudo} · {session.roles.map((r) => t(`roles.${r}`)).join(', ')}
          </p>
          <div className={styles.row} role="group" aria-label={t('nav.language')}>
            {LOCALES.map((locale: Locale) => (
              <button
                key={locale}
                type="button"
                className={styles.chip}
                aria-pressed={i18n.language === locale}
                onClick={() => {
                  void changeLocale(locale);
                }}
              >
                {locale.toUpperCase()}
              </button>
            ))}
          </div>
          <button
            type="button"
            className={styles.link}
            onClick={() => {
              void signOut();
            }}
          >
            {t('nav.signOut')}
          </button>
        </div>
      </nav>
      <main id="content" className={styles.content}>
        <Switch>
          <Route path="/team">
            {allowed('staff.manage') ? <Team me={session.playerId} /> : <Home session={session} />}
          </Route>
          <Route path="/audit">{allowed('audit.view') ? <Audit /> : <Home session={session} />}</Route>
          <Route>
            <Home session={session} />
          </Route>
        </Switch>
      </main>
    </div>
  );
}
