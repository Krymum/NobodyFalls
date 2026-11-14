import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useLocation } from 'wouter';
import { adminApi, type AdminSession, type PlayerRow, type PlayerSheet, type Sanction } from '../api.ts';
import { formatDateTime } from '../i18n/index.ts';
import { useAdmin } from '../store.ts';
import styles from '../styles/admin.module.css';
import { ErrorText, Notice, useSubmit } from '../ui.tsx';
import { allowedKinds, SanctionFields, useSanctionDraft } from './SanctionForm.tsx';

const FILTERS = ['all', 'reported', 'banned'] as const;

function isFuture(iso: string | null): boolean {
  return iso !== null && new Date(iso).getTime() > Date.now();
}

/** Search players by name or id; filter the reported and the banned. */
export function Players() {
  const { t, i18n } = useTranslation();
  const [q, setQ] = useState('');
  const [filter, setFilter] = useState<(typeof FILTERS)[number]>('all');
  const [rows, setRows] = useState<PlayerRow[] | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [, navigate] = useLocation();

  const search = useCallback(() => {
    adminApi
      .players(q.trim(), filter === 'all' ? '' : filter)
      .then(setRows)
      .catch(setError);
  }, [q, filter]);

  useEffect(() => {
    const timer = setTimeout(search, 250);
    return () => {
      clearTimeout(timer);
    };
  }, [search]);

  return (
    <section aria-labelledby="players-title">
      <h1 id="players-title" className={styles.pageTitle}>
        {t('players.title')}
      </h1>
      <div className={styles.toolbar}>
        <label className={styles.field}>
          <span className={styles.label}>{t('players.search')}</span>
          <input
            className={styles.input}
            type="search"
            value={q}
            onChange={(e) => {
              setQ(e.target.value);
            }}
          />
        </label>
        <div className={styles.row} role="group">
          {FILTERS.map((f) => (
            <button
              key={f}
              type="button"
              className={styles.chip}
              aria-pressed={filter === f}
              onClick={() => {
                setFilter(f);
              }}
            >
              {t(`players.filters.${f}`)}
            </button>
          ))}
        </div>
      </div>
      <ErrorText error={error} />
      {rows?.length === 0 && <p className={styles.muted}>{t('common.noResults')}</p>}
      {rows && rows.length > 0 && (
        <div className={styles.tableWrap}>
          <table className={styles.table}>
            <thead>
              <tr>
                <th scope="col">{t('players.columns.pseudo')}</th>
                <th scope="col">{t('players.columns.status')}</th>
                <th scope="col">{t('players.columns.reports')}</th>
                <th scope="col">{t('players.columns.since')}</th>
                <th scope="col">{t('players.columns.seen')}</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((p) => (
                <tr
                  key={p.id}
                  className={styles.clickable}
                  onClick={() => {
                    navigate(`/players/${p.id}`);
                  }}
                >
                  <th scope="row">
                    <Link href={`/players/${p.id}`}>{p.pseudo}</Link>
                  </th>
                  <td>
                    {t(`players.statuses.${p.status}`, { defaultValue: p.status })}
                    {isFuture(p.bannedUntil) && (
                      <span className={styles.danger}> · {t('players.banned')}</span>
                    )}
                    {isFuture(p.scoreExcludedUntil) && (
                      <span className={styles.attention}> · {t('players.excluded')}</span>
                    )}
                  </td>
                  <td>{p.openReports || t('common.none')}</td>
                  <td className={styles.nowrap}>{formatDateTime(p.createdAt, i18n.language)}</td>
                  <td className={styles.nowrap}>
                    {p.lastSeenAt ? formatDateTime(p.lastSeenAt, i18n.language) : t('common.none')}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}

function SanctionLine({
  sanction,
  canRevoke,
  onRevoked,
}: {
  sanction: Sanction;
  canRevoke: boolean;
  onRevoked: () => void;
}) {
  const { t, i18n } = useTranslation();
  const sensitive = useAdmin((s) => s.sensitive);
  const revoke = useSubmit(async () => {
    const reason = window.prompt(t('sanction.revokeReason')) ?? '';
    await sensitive(() => adminApi.revoke(sanction.id, reason));
    onRevoked();
  });
  const period = sanction.endsAt
    ? t('sanction.until', { date: formatDateTime(sanction.endsAt, i18n.language) })
    : sanction.kind === 'ban' || sanction.kind === 'score_exclusion'
      ? t('sanction.forever')
      : '';
  return (
    <tr>
      <th scope="row">
        {t(`sanction.kinds.${sanction.kind}`)}
        {sanction.active && <span className={styles.danger}> · {t('sanction.active')}</span>}
      </th>
      <td className={styles.prewrap}>
        {sanction.reason}
        {sanction.publicMessage && <span className={styles.muted}> — « {sanction.publicMessage} »</span>}
      </td>
      <td className={styles.nowrap}>
        {formatDateTime(sanction.startsAt, i18n.language)} {period}
        {sanction.issuedBy && (
          <span className={styles.muted}> · {t('sanction.by', { name: sanction.issuedBy })}</span>
        )}
      </td>
      <td>
        {sanction.revokedAt ? (
          <span className={styles.muted}>
            {t('sanction.revoked')} {sanction.revokedBy ? t('sanction.by', { name: sanction.revokedBy }) : ''}
          </span>
        ) : (
          canRevoke &&
          (sanction.kind === 'ban' || sanction.kind === 'score_exclusion') && (
            <button
              type="button"
              className={styles.smallSecondary}
              disabled={revoke.busy}
              onClick={() => {
                revoke.onSubmit();
              }}
            >
              {t('sanction.revoke')}
            </button>
          )
        )}
        <ErrorText error={revoke.error} />
      </td>
    </tr>
  );
}

/** A player's sheet: facts, sanctions (and new ones), reports about and by them. */
export function PlayerScreen({ id, session }: { id: string; session: AdminSession }) {
  const { t, i18n } = useTranslation();
  const sensitive = useAdmin((s) => s.sensitive);
  const [sheet, setSheet] = useState<PlayerSheet | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [message, setMessage] = useState<string | null>(null);
  const kinds = allowedKinds(session.permissions);
  const [draft, setDraft] = useSanctionDraft(kinds);

  const load = useCallback(() => {
    adminApi.player(id).then(setSheet).catch(setError);
  }, [id]);
  useEffect(load, [load]);

  const issue = useSubmit(async () => {
    await sensitive(() => adminApi.sanction(id, draft));
    setMessage(t('sanction.issued'));
    load();
  });

  if (!sheet) {
    return <ErrorText error={error} />;
  }
  const canRevoke = session.permissions.includes('sanctions.revoke');
  return (
    <section aria-labelledby="player-title">
      <Link href="/players" className={styles.link}>
        ← {t('common.back')}
      </Link>
      <h1 id="player-title" className={styles.pageTitle}>
        {sheet.pseudo}
      </h1>
      <Notice>{message}</Notice>
      <dl className={styles.facts}>
        <dt className={styles.label}>{t('players.columns.status')}</dt>
        <dd>
          {t(`players.statuses.${sheet.status}`, { defaultValue: sheet.status })}
          {isFuture(sheet.bannedUntil) && <span className={styles.danger}> · {t('players.banned')}</span>}
          {isFuture(sheet.scoreExcludedUntil) && (
            <span className={styles.attention}> · {t('players.excluded')}</span>
          )}
          {sheet.pseudoResetPending && (
            <span className={styles.attention}> · {t('players.sheet.pseudoPending')}</span>
          )}
          {sheet.staffRoles.length > 0 && <span> · {t('players.sheet.staff')}</span>}
        </dd>
        <dt className={styles.label}>{t('players.sheet.score')}</dt>
        <dd>{sheet.guardianScore}</dd>
        <dt className={styles.label}>{t('players.sheet.falls')}</dt>
        <dd>{sheet.fallsCount}</dd>
        <dt className={styles.label}>{t('players.sheet.friends')}</dt>
        <dd>{sheet.friends}</dd>
        <dt className={styles.label}>{t('players.sheet.given')}</dt>
        <dd>{sheet.handsGiven7}</dd>
        <dt className={styles.label}>{t('players.sheet.received')}</dt>
        <dd>{sheet.handsReceived7}</dd>
        <dt className={styles.label}>{t('players.columns.since')}</dt>
        <dd>{formatDateTime(sheet.createdAt, i18n.language)}</dd>
      </dl>

      <h2 className={styles.sectionTitle}>{t('players.sheet.sanctions')}</h2>
      {sheet.sanctions.length === 0 ? (
        <p className={styles.muted}>{t('common.none')}</p>
      ) : (
        <div className={styles.tableWrap}>
          <table className={styles.table}>
            <tbody>
              {sheet.sanctions.map((s) => (
                <SanctionLine key={s.id} sanction={s} canRevoke={canRevoke} onRevoked={load} />
              ))}
            </tbody>
          </table>
        </div>
      )}

      {kinds.length > 0 && sheet.staffRoles.length === 0 && sheet.id !== session.playerId && (
        <form className={styles.inlineForm} onSubmit={issue.onSubmit} aria-labelledby="sanction-title">
          <h2 id="sanction-title" className={styles.sectionTitle}>
            {t('sanction.title')}
          </h2>
          <SanctionFields value={draft} onChange={setDraft} kinds={kinds} />
          <button type="submit" className={styles.primary} disabled={issue.busy}>
            {t('sanction.submit')}
          </button>
          <ErrorText error={issue.error} />
        </form>
      )}

      {(
        [
          ['players.sheet.reportsAbout', sheet.reportsAbout],
          ['players.sheet.reportsMade', sheet.reportsMade],
        ] as const
      ).map(([title, reports]) => (
        <div key={title}>
          <h2 className={styles.sectionTitle}>{t(title)}</h2>
          {reports.length === 0 ? (
            <p className={styles.muted}>{t('common.none')}</p>
          ) : (
            <ul className={styles.plainList}>
              {reports.map((r) => (
                <li key={r.id}>
                  <Link href={`/reports/${r.id}`}>
                    {t(`reports.categories.${r.category}`)} · {r.targetLabel}
                  </Link>{' '}
                  <span className={styles.badge} data-status={r.status}>
                    {t(`reports.statuses.${r.status}`)}
                  </span>{' '}
                  <span className={styles.muted}>{formatDateTime(r.createdAt, i18n.language)}</span>
                </li>
              ))}
            </ul>
          )}
        </div>
      ))}
    </section>
  );
}
