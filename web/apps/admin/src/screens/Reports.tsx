import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useLocation } from 'wouter';
import { adminApi, type AdminSession, type ReportDetail, type ReportSummary } from '../api.ts';
import { changes } from '../core/audit.ts';
import { formatDateTime } from '../i18n/index.ts';
import { useAdmin } from '../store.ts';
import styles from '../styles/admin.module.css';
import { ErrorText, Notice, useSubmit } from '../ui.tsx';
import { allowedKinds, SanctionFields, useSanctionDraft } from './SanctionForm.tsx';

const STATUSES = ['open', 'in_review', 'actioned', 'dismissed'] as const;

/** The moderation queue (GAME_DESIGN §9): newest first, "to handle" by default. */
export function Reports() {
  const { t, i18n } = useTranslation();
  const [status, setStatus] = useState<string>('open');
  const [items, setItems] = useState<ReportSummary[] | null>(null);
  const [next, setNext] = useState<number | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [, navigate] = useLocation();

  const load = useCallback(
    (before?: number) => {
      adminApi
        .reports(status, before)
        .then((page) => {
          setItems((current) => (before ? [...(current ?? []), ...page.items] : page.items));
          setNext(page.nextBeforeId);
        })
        .catch(setError);
    },
    [status],
  );

  useEffect(() => {
    load();
    // The queue moves while one works: refresh every 30 seconds.
    const timer = setInterval(() => {
      load();
    }, 30_000);
    return () => {
      clearInterval(timer);
    };
  }, [load]);

  return (
    <section aria-labelledby="reports-title">
      <h1 id="reports-title" className={styles.pageTitle}>
        {t('reports.title')}
      </h1>
      <div className={styles.toolbar}>
        <label className={styles.field}>
          <span className={styles.label}>{t('reports.filter')}</span>
          <select
            className={styles.input}
            value={status}
            onChange={(e) => {
              setStatus(e.target.value);
            }}
          >
            {STATUSES.filter((s) => s !== 'in_review').map((s) => (
              <option key={s} value={s}>
                {t(`reports.statuses.${s}`)}
              </option>
            ))}
          </select>
        </label>
      </div>
      <ErrorText error={error} />
      {items?.length === 0 && <p className={styles.muted}>{t('common.noResults')}</p>}
      {items && items.length > 0 && (
        <div className={styles.tableWrap}>
          <table className={styles.table}>
            <thead>
              <tr>
                <th scope="col">{t('reports.columns.target')}</th>
                <th scope="col">{t('reports.columns.category')}</th>
                <th scope="col">{t('reports.columns.reporter')}</th>
                <th scope="col">{t('reports.columns.assignee')}</th>
                <th scope="col">{t('reports.columns.status')}</th>
                <th scope="col">{t('reports.columns.at')}</th>
              </tr>
            </thead>
            <tbody>
              {items.map((r) => (
                <tr
                  key={r.id}
                  className={styles.clickable}
                  onClick={() => {
                    navigate(`/reports/${r.id}`);
                  }}
                >
                  <th scope="row">
                    <Link href={`/reports/${r.id}`}>{r.targetLabel}</Link>
                  </th>
                  <td>{t(`reports.categories.${r.category}`)}</td>
                  <td>{r.reporterPseudo ?? t('common.none')}</td>
                  <td>{r.assigneePseudo ?? t('common.none')}</td>
                  <td>
                    <span className={styles.badge} data-status={r.status}>
                      {t(`reports.statuses.${r.status}`)}
                    </span>
                  </td>
                  <td className={styles.nowrap}>{formatDateTime(r.createdAt, i18n.language)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {next !== null && (
        <button
          type="button"
          className={styles.secondary}
          onClick={() => {
            load(next);
          }}
        >
          {t('common.more')}
        </button>
      )}
    </section>
  );
}

/** One report: what was said, the target, and the decision (with a sanction if needed). */
export function ReportScreen({ id, session }: { id: number; session: AdminSession }) {
  const { t, i18n } = useTranslation();
  const sensitive = useAdmin((s) => s.sensitive);
  const [report, setReport] = useState<ReportDetail | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [outcome, setOutcome] = useState<'actioned' | 'dismissed'>('actioned');
  const [note, setNote] = useState('');
  const [withSanction, setWithSanction] = useState(false);
  const kinds = allowedKinds(session.permissions);
  const [sanction, setSanction] = useSanctionDraft(kinds);

  const load = useCallback(() => {
    adminApi.report(id).then(setReport).catch(setError);
  }, [id]);
  useEffect(load, [load]);

  const assign = useSubmit(async () => {
    await adminApi.assignReport(id);
    setMessage(t('reports.assigned'));
    load();
  });
  const resolve = useSubmit(async () => {
    await sensitive(() => adminApi.resolveReport(id, outcome, note, withSanction ? sanction : null));
    setMessage(t('reports.resolved'));
    load();
  });

  if (!report) {
    return <ErrorText error={error} />;
  }
  const s = report.summary;
  const handled = s.status === 'actioned' || s.status === 'dismissed';
  const snapshot = changes(null, report.targetSnapshot);
  return (
    <section aria-labelledby="report-title">
      <Link href="/reports" className={styles.link}>
        ← {t('common.back')}
      </Link>
      <h1 id="report-title" className={styles.pageTitle}>
        {t('reports.detailTitle', { id: s.id })}
      </h1>
      <Notice>{message}</Notice>
      <dl className={styles.facts}>
        <dt className={styles.label}>{t('reports.columns.target')}</dt>
        <dd>
          {s.targetLabel}
          {s.targetPlayerId && (
            <>
              {' · '}
              <Link href={`/players/${s.targetPlayerId}`}>{t('reports.openPlayer')}</Link>
            </>
          )}
        </dd>
        <dt className={styles.label}>{t('reports.columns.category')}</dt>
        <dd>{t(`reports.categories.${s.category}`)}</dd>
        <dt className={styles.label}>{t('reports.columns.reporter')}</dt>
        <dd>
          {s.reporterPseudo ?? t('common.none')} · {formatDateTime(s.createdAt, i18n.language)}
        </dd>
        <dt className={styles.label}>{t('reports.columns.status')}</dt>
        <dd>
          <span className={styles.badge} data-status={s.status}>
            {t(`reports.statuses.${s.status}`)}
          </span>
          {s.assigneePseudo ? ` · ${s.assigneePseudo}` : ''}
        </dd>
        <dt className={styles.label}>{t('reports.details')}</dt>
        <dd className={styles.prewrap}>{report.details || t('common.none')}</dd>
        <dt className={styles.label}>{t('reports.snapshot')}</dt>
        <dd>{snapshot.map((f) => `${f.field}: ${f.after}`).join(' · ')}</dd>
      </dl>
      {report.otherReportsOnTarget > 0 && (
        <p className={styles.attention}>{t('reports.others', { count: report.otherReportsOnTarget })}</p>
      )}
      {report.resolutionNote && <p className={styles.muted}>{report.resolutionNote}</p>}

      {!handled && (
        <>
          {s.status === 'open' && (
            <button
              type="button"
              className={styles.secondary}
              disabled={assign.busy}
              onClick={() => {
                assign.onSubmit();
              }}
            >
              {t('reports.assign')}
            </button>
          )}
          <ErrorText error={assign.error} />
          <form className={styles.inlineForm} onSubmit={resolve.onSubmit} aria-labelledby="decision">
            <h2 id="decision" className={styles.sectionTitle}>
              {t('reports.resolveTitle')}
            </h2>
            <div className={styles.row} role="radiogroup" aria-label={t('reports.outcome')}>
              {(['actioned', 'dismissed'] as const).map((o) => (
                <label key={o} className={styles.check}>
                  <input
                    type="radio"
                    name="outcome"
                    checked={outcome === o}
                    onChange={() => {
                      setOutcome(o);
                      if (o === 'dismissed') {
                        setWithSanction(false);
                      }
                    }}
                  />
                  {t(`reports.outcomes.${o}`)}
                </label>
              ))}
            </div>
            <label className={styles.field}>
              <span className={styles.label}>{t('reports.note')}</span>
              <textarea
                className={styles.input}
                rows={2}
                maxLength={2000}
                value={note}
                onChange={(e) => {
                  setNote(e.target.value);
                }}
              />
            </label>
            {outcome === 'actioned' && s.targetPlayerId && kinds.length > 0 && (
              <label className={styles.check}>
                <input
                  type="checkbox"
                  checked={withSanction}
                  onChange={(e) => {
                    setWithSanction(e.target.checked);
                  }}
                />
                {t('reports.withSanction')}
              </label>
            )}
            {withSanction && <SanctionFields value={sanction} onChange={setSanction} kinds={kinds} />}
            <button type="submit" className={styles.primary} disabled={resolve.busy}>
              {t('reports.resolve')}
            </button>
            <ErrorText error={resolve.error} />
          </form>
        </>
      )}
    </section>
  );
}
