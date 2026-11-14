import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, Route, Switch, useLocation } from 'wouter';
import type { AdminSession, Locale } from '../api.ts';
import { changeLocale, LOCALES } from '../i18n/index.ts';
import { useAdmin } from '../store.ts';
import styles from '../styles/admin.module.css';
import { Audit } from './Audit.tsx';
import { Dashboard } from './Dashboard.tsx';
import { Home } from './Home.tsx';
import { Ideas } from './Ideas.tsx';
import { Players, PlayerScreen } from './Players.tsx';
import { Reports, ReportScreen } from './Reports.tsx';
import { Team } from './Team.tsx';
import { Tickets, TicketScreen } from './Tickets.tsx';

interface Section {
  path: string;
  label: string;
  /** Any of these permissions shows the section (none: everyone in the team). */
  permissions: readonly string[];
}

/** Sections appear only when the member's permissions allow them (the server checks anyway). */
const SECTIONS: readonly Section[] = [
  { path: '/', label: 'nav.home', permissions: [] },
  { path: '/reports', label: 'nav.reports', permissions: ['reports.view'] },
  { path: '/players', label: 'nav.players', permissions: ['players.view'] },
  { path: '/tickets', label: 'nav.tickets', permissions: ['tickets.support', 'tickets.appeal'] },
  { path: '/ideas', label: 'nav.ideas', permissions: ['ideas.moderate'] },
  { path: '/team', label: 'nav.team', permissions: ['staff.manage'] },
  { path: '/audit', label: 'nav.audit', permissions: ['audit.view'] },
];

/** The section a location belongs to (`/reports/12` → `/reports`), for the current link. */
function sectionOf(location: string): string {
  return `/${location.split('/')[1] ?? ''}`;
}

export function Shell({ session }: { session: AdminSession }) {
  const { t, i18n } = useTranslation();
  const signOut = useAdmin((s) => s.signOut);
  const [location] = useLocation();
  const [menuOpen, setMenuOpen] = useState(false);
  const allowed = (permission: string) => session.permissions.includes(permission);
  const sections = SECTIONS.filter(
    (section) => section.permissions.length === 0 || section.permissions.some(allowed),
  );

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
                aria-current={sectionOf(location) === s.path ? 'page' : undefined}
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
          <Route path="/reports/:id">
            {(params) => <ReportScreen id={Number(params.id)} session={session} />}
          </Route>
          <Route path="/reports">{allowed('reports.view') ? <Reports /> : <Home session={session} />}</Route>
          <Route path="/players/:id">{(params) => <PlayerScreen id={params.id} session={session} />}</Route>
          <Route path="/players">{allowed('players.view') ? <Players /> : <Home session={session} />}</Route>
          <Route path="/tickets/:id">{(params) => <TicketScreen id={Number(params.id)} />}</Route>
          <Route path="/tickets">
            {allowed('tickets.support') || allowed('tickets.appeal') ? (
              <Tickets />
            ) : (
              <Home session={session} />
            )}
          </Route>
          <Route path="/ideas">{allowed('ideas.moderate') ? <Ideas /> : <Home session={session} />}</Route>
          <Route path="/team">
            {allowed('staff.manage') ? <Team me={session.playerId} /> : <Home session={session} />}
          </Route>
          <Route path="/audit">{allowed('audit.view') ? <Audit /> : <Home session={session} />}</Route>
          <Route>{allowed('dashboard.view') ? <Dashboard /> : <Home session={session} />}</Route>
        </Switch>
      </main>
    </div>
  );
}
