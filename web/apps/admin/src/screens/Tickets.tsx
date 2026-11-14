import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useLocation } from 'wouter';
import { adminApi, TICKET_STATUSES, type AdminTicket, type AdminTicketSummary } from '../api.ts';
import { formatDateTime } from '../i18n/index.ts';
import styles from '../styles/admin.module.css';
import { ErrorText, Notice, useSubmit } from '../ui.tsx';

const FILTERS = ['active', 'open', 'waiting_player', 'resolved', 'closed'] as const;

/** Requests the member's roles cover: support and deletions for support, appeals for moderators. */
export function Tickets() {
  const { t, i18n } = useTranslation();
  const [filter, setFilter] = useState<(typeof FILTERS)[number]>('active');
  const [rows, setRows] = useState<AdminTicketSummary[] | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [, navigate] = useLocation();

  useEffect(() => {
    const load = (): void => {
      adminApi.tickets(filter).then(setRows).catch(setError);
    };
    load();
    const timer = setInterval(load, 30_000);
    return () => {
      clearInterval(timer);
    };
  }, [filter]);

  return (
    <section aria-labelledby="tickets-title">
      <h1 id="tickets-title" className={styles.pageTitle}>
        {t('tickets.title')}
      </h1>
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
            {t(`tickets.filters.${f}`)}
          </button>
        ))}
      </div>
      <ErrorText error={error} />
      {rows?.length === 0 && <p className={styles.muted}>{t('common.noResults')}</p>}
      {rows && rows.length > 0 && (
        <div className={styles.tableWrap}>
          <table className={styles.table}>
            <thead>
              <tr>
                <th scope="col">{t('tickets.columns.subject')}</th>
                <th scope="col">{t('tickets.columns.player')}</th>
                <th scope="col">{t('tickets.columns.category')}</th>
                <th scope="col">{t('tickets.columns.status')}</th>
                <th scope="col">{t('tickets.columns.updated')}</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((ticket) => (
                <tr
                  key={ticket.id}
                  className={styles.clickable}
                  onClick={() => {
                    navigate(`/tickets/${ticket.id}`);
                  }}
                >
                  <th scope="row">
                    <Link href={`/tickets/${ticket.id}`}>{ticket.subject}</Link>
                  </th>
                  <td>{ticket.pseudo}</td>
                  <td>{t(`tickets.categories.${ticket.category}`)}</td>
                  <td>
                    <span className={styles.badge} data-status={ticket.status}>
                      {t(`tickets.statuses.${ticket.status}`)}
                    </span>
                  </td>
                  <td className={styles.nowrap}>{formatDateTime(ticket.updatedAt, i18n.language)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}

/** A conversation: public answers notify the player, internal notes stay in the team. */
export function TicketScreen({ id }: { id: number }) {
  const { t, i18n } = useTranslation();
  const [ticket, setTicket] = useState<AdminTicket | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [body, setBody] = useState('');
  const [internal, setInternal] = useState(false);

  const load = useCallback(() => {
    adminApi.ticket(id).then(setTicket).catch(setError);
  }, [id]);
  useEffect(load, [load]);

  const send = useSubmit(async () => {
    await adminApi.replyTicket(id, body, internal);
    setBody('');
    setMessage(t('tickets.sent'));
    load();
  });
  const status = useSubmit(async () => {
    if (ticket) {
      await adminApi.ticketStatus(id, ticket.summary.status);
      load();
    }
  });

  if (!ticket) {
    return <ErrorText error={error} />;
  }
  const s = ticket.summary;
  return (
    <section aria-labelledby="ticket-title">
      <Link href="/tickets" className={styles.link}>
        ← {t('common.back')}
      </Link>
      <h1 id="ticket-title" className={styles.pageTitle}>
        {s.subject}
      </h1>
      <p className={styles.muted}>
        <Link href={`/players/${s.playerId}`}>{s.pseudo}</Link> · {t(`tickets.categories.${s.category}`)}
        {s.sanctionId !== null && ` · ${t('tickets.sanction', { id: s.sanctionId })}`}
      </p>
      <Notice>{message}</Notice>
      <ol className={styles.thread}>
        {ticket.messages.map((m) => (
          <li key={m.id} className={styles.message} data-internal={m.internal} data-author={m.authorKind}>
            <p className={styles.label}>
              {m.authorKind === 'staff'
                ? `${t('tickets.team')} · ${m.authorPseudo ?? ''}`
                : (m.authorPseudo ?? s.pseudo)}
              {m.internal && ` · ${t('tickets.internalTag')}`} · {formatDateTime(m.at, i18n.language)}
            </p>
            <p className={styles.prewrap}>{m.body}</p>
          </li>
        ))}
      </ol>
      <form className={styles.form} onSubmit={send.onSubmit}>
        <label className={styles.field}>
          <span className={styles.label}>{internal ? t('tickets.internal') : t('tickets.reply')}</span>
          <textarea
            className={styles.input}
            rows={4}
            required
            maxLength={2000}
            value={body}
            onChange={(e) => {
              setBody(e.target.value);
            }}
          />
        </label>
        <label className={styles.check}>
          <input
            type="checkbox"
            checked={internal}
            onChange={(e) => {
              setInternal(e.target.checked);
            }}
          />
          {t('tickets.internal')}
        </label>
        <button type="submit" className={styles.primary} disabled={send.busy}>
          {t('tickets.send')}
        </button>
        <ErrorText error={send.error} />
      </form>
      <div className={styles.toolbar}>
        <label className={styles.field}>
          <span className={styles.label}>{t('tickets.status')}</span>
          <select
            className={styles.input}
            value={s.status}
            onChange={(e) => {
              setTicket({
                ...ticket,
                summary: { ...s, status: e.target.value as AdminTicketSummary['status'] },
              });
            }}
          >
            {TICKET_STATUSES.map((value) => (
              <option key={value} value={value}>
                {t(`tickets.statuses.${value}`)}
              </option>
            ))}
          </select>
        </label>
        <button
          type="button"
          className={styles.secondary}
          disabled={status.busy}
          onClick={() => {
            status.onSubmit();
          }}
        >
          {t('tickets.setStatus')}
        </button>
      </div>
      <ErrorText error={status.error} />
    </section>
  );
}
