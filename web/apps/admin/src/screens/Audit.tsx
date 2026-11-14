import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { adminApi, type AuditEntry, type AuditFilter, type AuditVerification } from '../api.ts';
import { changes } from '../core/audit.ts';
import { formatDateTime } from '../i18n/index.ts';
import styles from '../styles/admin.module.css';
import { ErrorText, Notice, useSubmit } from '../ui.tsx';

const FAMILIES = ['staff', 'admin', 'audit'] as const;

function Row({ entry }: { entry: AuditEntry }) {
  const { t, i18n } = useTranslation();
  const fields = changes(entry.before, entry.after);
  return (
    <tr>
      <td className={styles.nowrap}>{formatDateTime(entry.at, i18n.language)}</td>
      <td>
        {entry.actorPseudo ?? t('audit.system')}
        {entry.actorRoles.length > 0 ? (
          <span className={styles.muted}>
            {' '}
            · {entry.actorRoles.map((r) => t(`roles.${r}`, { defaultValue: r })).join(', ')}
          </span>
        ) : null}
        {entry.ip ? <span className={styles.mono}> · {entry.ip}</span> : null}
      </td>
      <td>
        {t(`audit.actions.${entry.action}`, { defaultValue: entry.action })}
        <span className={styles.mono}> {entry.action}</span>
      </td>
      <td className={styles.mono}>
        {entry.targetType}
        {entry.targetId ? ` ${entry.targetId}` : ''}
      </td>
      <td>
        {fields.length > 0 ? (
          <dl className={styles.changes}>
            {fields.map((f) => (
              <div key={f.field}>
                <dt>{f.field}</dt>
                <dd>
                  {f.before} → {f.after}
                </dd>
              </div>
            ))}
          </dl>
        ) : null}
      </td>
      <td>{entry.reason}</td>
    </tr>
  );
}

/** The audit log (ADR 0019): read-only, newest first, filtered by family of action or target. */
export function Audit() {
  const { t } = useTranslation();
  const [filter, setFilter] = useState<AuditFilter>({});
  const [draft, setDraft] = useState<AuditFilter>({});
  const [entries, setEntries] = useState<AuditEntry[]>([]);
  const [next, setNext] = useState<number | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [verification, setVerification] = useState<AuditVerification | null>(null);

  const loadPage = useCallback((query: AuditFilter, append: boolean) => {
    adminApi
      .audit(query)
      .then((page) => {
        setEntries((current) => (append ? [...current, ...page.entries] : page.entries));
        setNext(page.nextBeforeId);
        setError(null);
      })
      .catch((e: unknown) => {
        setError(e);
      });
  }, []);

  useEffect(() => {
    loadPage(filter, false);
  }, [filter, loadPage]);

  const verify = useSubmit(async () => {
    setVerification(await adminApi.verifyAudit());
  });

  return (
    <section aria-labelledby="audit-title">
      <h1 id="audit-title" className={styles.pageTitle}>
        {t('audit.title')}
      </h1>
      <p className={styles.muted}>
        {t('audit.intro')} {t('app.utc')}.
      </p>

      <div className={styles.toolbar}>
        <form
          className={styles.row}
          onSubmit={(e) => {
            e.preventDefault();
            setFilter({ ...draft });
          }}
        >
          <label className={styles.field}>
            <span className={styles.label}>{t('audit.action')}</span>
            <select
              className={styles.input}
              value={draft.action ?? ''}
              onChange={(e) => {
                setDraft({ ...draft, action: e.target.value || undefined });
              }}
            >
              <option value="">{t('audit.allActions')}</option>
              {FAMILIES.map((f) => (
                <option key={f} value={f}>
                  {t(`audit.families.${f}`)}
                </option>
              ))}
            </select>
          </label>
          <label className={styles.field}>
            <span className={styles.label}>{t('audit.target')}</span>
            <input
              className={styles.input}
              value={draft.targetId ?? ''}
              onChange={(e) => {
                setDraft({ ...draft, targetId: e.target.value.trim() || undefined });
              }}
            />
          </label>
          <button type="submit" className={styles.secondary}>
            {t('audit.apply')}
          </button>
        </form>
        <button
          type="button"
          className={styles.secondary}
          disabled={verify.busy}
          onClick={() => {
            verify.onSubmit();
          }}
        >
          {t('audit.verify')}
        </button>
      </div>

      {verification ? (
        verification.valid ? (
          <Notice>{t('audit.valid', { count: verification.checked })}</Notice>
        ) : (
          <p className={styles.error} role="alert">
            {t('audit.broken', { id: verification.firstBrokenId })}
          </p>
        )
      ) : null}
      <ErrorText error={error ?? verify.error} />

      {entries.length === 0 ? <p className={styles.muted}>{t('audit.empty')}</p> : null}
      {entries.length > 0 ? (
        <div className={styles.tableWrap}>
          <table className={styles.table}>
            <thead>
              <tr>
                <th scope="col">{t('audit.columns.at')}</th>
                <th scope="col">{t('audit.columns.actor')}</th>
                <th scope="col">{t('audit.columns.action')}</th>
                <th scope="col">{t('audit.columns.target')}</th>
                <th scope="col">{t('audit.columns.change')}</th>
                <th scope="col">{t('audit.columns.reason')}</th>
              </tr>
            </thead>
            <tbody>
              {entries.map((e) => (
                <Row key={e.id} entry={e} />
              ))}
            </tbody>
          </table>
        </div>
      ) : null}
      {next !== null ? (
        <button
          type="button"
          className={styles.secondary}
          onClick={() => {
            loadPage({ ...filter, before: next }, true);
          }}
        >
          {t('audit.more')}
        </button>
      ) : null}
    </section>
  );
}
