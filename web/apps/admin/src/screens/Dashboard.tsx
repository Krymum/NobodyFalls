import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'wouter';
import { adminApi, type Dashboard as DashboardData } from '../api.ts';
import { formatDateTime } from '../i18n/index.ts';
import styles from '../styles/admin.module.css';
import { ErrorText } from '../ui.tsx';

function Tile({ label, value, hint }: { label: string; value: string; hint?: string }) {
  return (
    <div className={styles.tile}>
      <span className={styles.label}>{label}</span>
      <span className={styles.tileValue}>{value}</span>
      {hint && <span className={styles.hint}>{hint}</span>}
    </div>
  );
}

/**
 * Falls per night: one series, so one neutral hue and no legend (the title names it);
 * thin bars on a recessive baseline, a tooltip per bar, and the same data as a table.
 */
function FallsChart({ nights }: { nights: DashboardData['nights'] }) {
  const { t, i18n } = useTranslation();
  const [hover, setHover] = useState<number | null>(null);
  const width = 640;
  const height = 160;
  const max = Math.max(1, ...nights.map((n) => n.falls));
  const step = width / Math.max(1, nights.length);
  const bar = Math.max(2, Math.min(14, step - 2));
  const scale = (v: number) => (v / max) * (height - 20);
  const day = (iso: string) =>
    new Intl.DateTimeFormat(i18n.language, { day: 'numeric', month: 'short', timeZone: 'UTC' }).format(
      new Date(`${iso}T00:00:00Z`),
    );

  if (nights.length === 0) {
    return (
      <div className={styles.chart}>
        <h2 className={styles.sectionTitle}>{t('dashboard.falls')}</h2>
        <p className={styles.muted}>{t('dashboard.noNights')}</p>
      </div>
    );
  }
  const shown = hover !== null ? nights[hover] : undefined;
  return (
    <figure className={styles.chart}>
      <figcaption className={styles.sectionTitle}>{t('dashboard.falls')}</figcaption>
      <p className={styles.muted} aria-live="polite">
        {shown
          ? t('dashboard.fallsOn', {
              night: shown.nightNumber,
              day: day(shown.day),
              count: shown.falls,
              alive: shown.aliveAtStart,
            })
          : t('dashboard.fallsMax', { count: max })}
      </p>
      <svg
        viewBox={`0 0 ${width} ${height}`}
        role="img"
        aria-label={t('dashboard.falls')}
        className={styles.chartSvg}
      >
        <line x1="0" x2={width} y1={height - 0.5} y2={height - 0.5} className={styles.chartAxis} />
        {nights.map((n, i) => {
          // At least 4 px, so that the rounded end fits.
          const h = Math.max(4, scale(n.falls));
          const x = i * step + (step - bar) / 2;
          return (
            <g
              key={n.day}
              onMouseEnter={() => {
                setHover(i);
              }}
              onMouseLeave={() => {
                setHover(null);
              }}
            >
              {/* Hit target taller and wider than the bar. */}
              <rect x={i * step} y="0" width={step} height={height} fill="transparent" />
              {n.falls > 0 && (
                <path
                  className={hover === i ? styles.chartBarActive : styles.chartBar}
                  d={`M${x} ${height - 1}V${height - 1 - h + 4}a4 4 0 0 1 4 -4h${bar - 8}a4 4 0 0 1 4 4V${height - 1}Z`}
                />
              )}
              {n.storm && (
                <circle cx={x + bar / 2} cy={height - h - 10} r="2.5" className={styles.chartMark} />
              )}
              <title>
                {t('dashboard.fallsOn', {
                  night: n.nightNumber,
                  day: day(n.day),
                  count: n.falls,
                  alive: n.aliveAtStart,
                })}
              </title>
            </g>
          );
        })}
      </svg>
      <details>
        <summary className={styles.link}>{t('dashboard.table')}</summary>
        <table className={styles.table}>
          <thead>
            <tr>
              <th scope="col">{t('dashboard.night')}</th>
              <th scope="col">{t('dashboard.fallsColumn')}</th>
              <th scope="col">{t('dashboard.aliveColumn')}</th>
            </tr>
          </thead>
          <tbody>
            {nights.map((n) => (
              <tr key={n.day}>
                <th scope="row">
                  {n.nightNumber} · {day(n.day)}
                  {n.storm ? ` · ${t('dashboard.storm')}` : ''}
                </th>
                <td>{n.falls}</td>
                <td>{n.aliveAtStart}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </details>
    </figure>
  );
}

/** What the team watches (GAME_DESIGN §9): players, nights, retention, invitations, suspicious accounts. */
export function Dashboard() {
  const { t, i18n } = useTranslation();
  const [data, setData] = useState<DashboardData | null>(null);
  const [error, setError] = useState<unknown>(null);
  const number = (v: number) => new Intl.NumberFormat(i18n.language).format(v);
  const percent = (part: number, whole: number) =>
    whole === 0 ? '—' : new Intl.NumberFormat(i18n.language, { style: 'percent' }).format(part / whole);

  useEffect(() => {
    const load = (): void => {
      adminApi.dashboard().then(setData).catch(setError);
    };
    load();
    const timer = setInterval(load, 60_000);
    return () => {
      clearInterval(timer);
    };
  }, []);

  if (!data) {
    return <ErrorText error={error} />;
  }
  const invited = data.invitations.reduce((sum, p) => sum + p.joined, 0);
  return (
    <section aria-labelledby="dashboard-title">
      <h1 id="dashboard-title" className={styles.pageTitle}>
        {t('dashboard.title')}
      </h1>
      <div className={styles.tiles}>
        <Tile
          label={t('dashboard.alive')}
          value={number(data.alive)}
          hint={t('dashboard.ofPlayers', { count: data.players })}
        />
        <Tile label={t('dashboard.visitedToday')} value={number(data.visitedToday)} />
        <Tile label={t('dashboard.streak')} value={number(data.streak)} />
        <Tile label={t('dashboard.deaths')} value={number(data.deathsAllTime)} />
        <Tile label={t('dashboard.invited')} value={number(invited)} hint={t('dashboard.last30')} />
      </div>
      <div className={styles.tiles}>
        {data.retention.map((r) => (
          <Tile
            key={r.afterDays}
            label={t('dashboard.retention', { days: r.afterDays })}
            value={percent(r.returned, r.cohort)}
            hint={t('dashboard.cohort', { returned: r.returned, count: r.cohort })}
          />
        ))}
        <div className={styles.tile}>
          <span className={styles.label}>{t('dashboard.todo')}</span>
          <Link href="/reports" className={styles.link}>
            {t('dashboard.openReports', { count: data.openReports })}
          </Link>
          <Link href="/tickets" className={styles.link}>
            {t('dashboard.openTickets', { count: data.openTickets })}
          </Link>
        </div>
      </div>

      <FallsChart nights={data.nights} />

      <h2 className={styles.sectionTitle}>{t('dashboard.suspicious')}</h2>
      {data.sharedDevices.length === 0 && data.signUpBursts.length === 0 && (
        <p className={styles.muted}>{t('dashboard.nothingSuspicious')}</p>
      )}
      <ul className={styles.plainList}>
        {data.sharedDevices.map((d) => (
          <li key={d.deviceId}>
            <span className={styles.attention}>{t('dashboard.sharedDevice', { count: d.accounts })}</span>{' '}
            {d.pseudos.join(', ')} <span className={styles.mono}>{d.deviceId.slice(0, 8)}</span>
          </li>
        ))}
        {data.signUpBursts.map((b) => (
          <li key={b.ip}>
            <span className={styles.attention}>{t('dashboard.burst', { count: b.accounts })}</span>{' '}
            <span className={styles.mono}>{b.ip}</span> · {formatDateTime(b.since, i18n.language)}
          </li>
        ))}
      </ul>
    </section>
  );
}
