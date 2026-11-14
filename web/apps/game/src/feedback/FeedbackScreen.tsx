import i18next from 'i18next';
import { useCallback, useEffect, useState, type KeyboardEvent, type SyntheticEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'wouter';
import { humanToken } from '../api/turnstile.ts';
import { errorKey, useGame } from '../core/store.ts';
import ui from '../ui/ui.module.css';
import {
  feedbackApi,
  IDEA_CATEGORIES,
  IDEA_STATUSES,
  REPORT_CATEGORIES,
  TICKET_CATEGORIES,
  type Idea,
  type IdeaCategory,
  type IdeaStatus,
  type ReportCategory,
  type Ticket,
  type TicketCategory,
  type TicketSummary,
} from './api.ts';
import styles from './feedback.module.css';
import en from './i18n/en.json';
import fr from './i18n/fr.json';

// The texts of this screen travel with it: the game's first load stays as light as before.
i18next.addResourceBundle('fr', 'translation', fr, true, false);
i18next.addResourceBundle('en', 'translation', en, true, false);

const TABS = ['ideas', 'contact', 'report'] as const;
type Tab = (typeof TABS)[number];

/** Runs one async action for a form: busy flag, translated error, success message. */
function useAction() {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState<string | null>(null);
  const run = (action: () => Promise<string | null>) => (event?: SyntheticEvent) => {
    event?.preventDefault();
    if (busy) {
      return;
    }
    setBusy(true);
    setError(null);
    setDone(null);
    action()
      .then(setDone)
      .catch((e: unknown) => {
        setError(errorKey(e));
      })
      .finally(() => {
        setBusy(false);
      });
  };
  return { busy, error, done, run };
}

function Messages({ error, done }: { error: string | null; done: string | null }) {
  const { t } = useTranslation();
  return (
    <>
      {error && (
        <p className={ui.notice} role="alert">
          {t(error)}
        </p>
      )}
      {done && (
        <p className={ui.notice} role="status">
          {done}
        </p>
      )}
    </>
  );
}

/**
 * "Ideas and contact" (GAME_DESIGN §8), from the profile: suggest and back ideas, write
 * to the team, report a player. A separate chunk, loaded only when opened.
 */
export function FeedbackScreen() {
  const { t } = useTranslation();
  const [tab, setTab] = useState<Tab>(() => {
    const asked = new URLSearchParams(window.location.search).get('tab');
    return TABS.find((name) => name === asked) ?? 'ideas';
  });

  const select = (next: Tab): void => {
    setTab(next);
    const url = new URL(window.location.href);
    url.searchParams.set('tab', next);
    window.history.replaceState(null, '', url);
  };

  // Tabs pattern: arrows move between tabs (WAI-ARIA).
  const onKey = (event: KeyboardEvent): void => {
    const index = TABS.indexOf(tab);
    const step = event.key === 'ArrowRight' ? 1 : event.key === 'ArrowLeft' ? -1 : 0;
    if (step !== 0) {
      const next = TABS[(index + step + TABS.length) % TABS.length] ?? 'ideas';
      select(next);
      document.getElementById(`feedback-tab-${next}`)?.focus();
      event.preventDefault();
    }
  };

  return (
    <main className={ui.page}>
      <div className={ui.section}>
        <Link href="/profile" className={`${ui.secondary} ${styles.back}`}>
          ← {t('feedback.back')}
        </Link>
        <h1 className={ui.heading}>{t('feedback.title')}</h1>
      </div>
      <div className={styles.tabs} role="tablist" aria-label={t('feedback.tabs.label')} onKeyDown={onKey}>
        {TABS.map((name) => (
          <button
            key={name}
            id={`feedback-tab-${name}`}
            type="button"
            role="tab"
            className={styles.tab}
            aria-selected={tab === name}
            aria-controls={`feedback-panel-${name}`}
            tabIndex={tab === name ? 0 : -1}
            onClick={() => {
              select(name);
            }}
          >
            {t(`feedback.tabs.${name}`)}
          </button>
        ))}
      </div>
      <section
        id={`feedback-panel-${tab}`}
        role="tabpanel"
        aria-labelledby={`feedback-tab-${tab}`}
        className={ui.section}
      >
        {tab === 'ideas' && <Ideas />}
        {tab === 'contact' && <Contact />}
        {tab === 'report' && <Report />}
      </section>
    </main>
  );
}

function Ideas() {
  const { t } = useTranslation();
  const [sort, setSort] = useState<'votes' | 'new'>('votes');
  const [status, setStatus] = useState<IdeaStatus | null>(null);
  const [ideas, setIdeas] = useState<Idea[]>([]);
  const [next, setNext] = useState<number | null>(null);
  const [loaded, setLoaded] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(
    (offset: number) => {
      feedbackApi
        .ideas(sort, status, offset)
        .then((page) => {
          setIdeas((current) => (offset === 0 ? page.items : [...current, ...page.items]));
          setNext(page.nextOffset);
          setLoaded(true);
        })
        .catch((e: unknown) => {
          setError(errorKey(e));
        });
    },
    [sort, status],
  );

  useEffect(() => {
    load(0);
  }, [load]);

  const vote = (idea: Idea): void => {
    setError(null);
    feedbackApi
      .vote(idea.id, !idea.voted)
      .then((result) => {
        setIdeas((current) =>
          current.map((i) =>
            i.id === result.ideaId ? { ...i, votes: result.votes, voted: result.voted } : i,
          ),
        );
      })
      .catch((e: unknown) => {
        setError(errorKey(e));
      });
  };

  return (
    <>
      <p className={ui.text}>{t('feedback.ideas.intro')}</p>
      <ProposeIdea
        onSent={() => {
          setSort('new');
          load(0);
        }}
      />
      <div className={styles.toolbar}>
        <div className={styles.choices} role="group">
          {(['votes', 'new'] as const).map((value) => (
            <button
              key={value}
              type="button"
              className={styles.choice}
              aria-pressed={sort === value}
              onClick={() => {
                setSort(value);
              }}
            >
              {t(value === 'votes' ? 'feedback.ideas.sortVotes' : 'feedback.ideas.sortNew')}
            </button>
          ))}
        </div>
        <label className={styles.inline}>
          <span className={ui.label}>{t('feedback.ideas.statusFilter')}</span>
          <select
            className={styles.select}
            value={status ?? ''}
            onChange={(e) => {
              setStatus((e.target.value || null) as IdeaStatus | null);
            }}
          >
            <option value="">{t('feedback.ideas.allStatuses')}</option>
            {IDEA_STATUSES.map((s) => (
              <option key={s} value={s}>
                {t(`feedback.ideas.status.${s}`)}
              </option>
            ))}
          </select>
        </label>
      </div>
      {error && (
        <p className={ui.notice} role="alert">
          {t(error)}
        </p>
      )}
      {loaded && ideas.length === 0 && <p className={ui.text}>{t('feedback.ideas.empty')}</p>}
      <ul className={ui.list}>
        {ideas.map((idea) => (
          <li key={idea.id} className={styles.idea}>
            <div className={styles.ideaHead}>
              <h3 className={styles.ideaTitle}>{idea.title}</h3>
              <span className={styles.status} data-status={idea.status}>
                {t(`feedback.ideas.status.${idea.status}`)}
              </span>
            </div>
            <p className={ui.text}>{idea.body}</p>
            <p className={styles.meta}>
              {t(`feedback.ideas.category.${idea.category}`)}
              {idea.authorPseudo ? ` · ${t('feedback.ideas.by', { name: idea.authorPseudo })}` : ''}
              {idea.hidden ? ` · ${t('feedback.ideas.hidden')}` : ''}
            </p>
            {idea.staffAnswer && (
              <blockquote className={styles.answer}>
                <p className={ui.label}>{t('feedback.ideas.teamAnswer')}</p>
                <p className={ui.text}>{idea.staffAnswer}</p>
              </blockquote>
            )}
            <div className={styles.voteRow}>
              <span className={styles.votes}>{t('feedback.ideas.votes', { count: idea.votes })}</span>
              {idea.mine ? (
                <span className={styles.meta}>{t('feedback.ideas.mine')}</span>
              ) : (
                <button
                  type="button"
                  className={styles.choice}
                  aria-pressed={idea.voted}
                  aria-label={`${t('feedback.ideas.vote')} : ${idea.title}`}
                  disabled={idea.status === 'shipped' || idea.status === 'declined'}
                  onClick={() => {
                    vote(idea);
                  }}
                >
                  {idea.voted ? t('feedback.ideas.voted') : t('feedback.ideas.vote')}
                </button>
              )}
            </div>
          </li>
        ))}
      </ul>
      {next !== null && (
        <button
          type="button"
          className={ui.secondary}
          onClick={() => {
            load(next);
          }}
        >
          {t('feedback.ideas.more')}
        </button>
      )}
    </>
  );
}

function ProposeIdea({ onSent }: { onSent: () => void }) {
  const { t } = useTranslation();
  const siteKey = useGame((s) => s.session?.turnstileSiteKey ?? '');
  const [title, setTitle] = useState('');
  const [body, setBody] = useState('');
  const [category, setCategory] = useState<IdeaCategory>('gameplay');
  const { busy, error, done, run } = useAction();

  return (
    <details className={styles.compose}>
      <summary className={styles.summary}>{t('feedback.ideas.newTitle')}</summary>
      <form
        className={ui.section}
        onSubmit={run(async () => {
          await feedbackApi.propose({ title, body, category, turnstileToken: await humanToken(siteKey) });
          setTitle('');
          setBody('');
          onSent();
          return t('feedback.ideas.sent');
        })}
      >
        <label className={styles.field}>
          <span className={ui.label}>{t('feedback.ideas.fieldTitle')}</span>
          <input
            className={ui.input}
            required
            minLength={5}
            maxLength={80}
            value={title}
            onChange={(e) => {
              setTitle(e.target.value);
            }}
          />
        </label>
        <label className={styles.field}>
          <span className={ui.label}>{t('feedback.ideas.fieldBody')}</span>
          <textarea
            className={styles.textarea}
            required
            minLength={10}
            maxLength={1000}
            rows={4}
            value={body}
            onChange={(e) => {
              setBody(e.target.value);
            }}
          />
        </label>
        <label className={styles.field}>
          <span className={ui.label}>{t('feedback.ideas.fieldCategory')}</span>
          <select
            className={styles.select}
            value={category}
            onChange={(e) => {
              setCategory(e.target.value as IdeaCategory);
            }}
          >
            {IDEA_CATEGORIES.map((c) => (
              <option key={c} value={c}>
                {t(`feedback.ideas.category.${c}`)}
              </option>
            ))}
          </select>
        </label>
        <Messages error={error} done={done} />
        <button type="submit" className={ui.primary} disabled={busy}>
          {t('feedback.ideas.submit')}
        </button>
      </form>
    </details>
  );
}

function Contact() {
  const { t, i18n } = useTranslation();
  const [tickets, setTickets] = useState<TicketSummary[] | null>(null);
  const [open, setOpen] = useState<number | null>(null);
  const [category, setCategory] = useState<TicketCategory>('support');
  const [subject, setSubject] = useState('');
  const [message, setMessage] = useState('');
  const { busy, error, done, run } = useAction();

  const refresh = useCallback(() => {
    void feedbackApi.tickets().then(setTickets);
  }, []);
  useEffect(refresh, [refresh]);

  return (
    <>
      <p className={ui.text}>{t('feedback.contact.intro')}</p>
      <details className={styles.compose}>
        <summary className={styles.summary}>{t('feedback.contact.newTitle')}</summary>
        <form
          className={ui.section}
          onSubmit={run(async () => {
            const created = await feedbackApi.openTicket({ category, subject, message });
            setSubject('');
            setMessage('');
            refresh();
            setOpen(created.id);
            return t('feedback.contact.sent');
          })}
        >
          <label className={styles.field}>
            <span className={ui.label}>{t('feedback.contact.fieldCategory')}</span>
            <select
              className={styles.select}
              value={category}
              onChange={(e) => {
                setCategory(e.target.value as TicketCategory);
              }}
            >
              {TICKET_CATEGORIES.map((c) => (
                <option key={c} value={c}>
                  {t(`feedback.contact.category.${c}`)}
                </option>
              ))}
            </select>
          </label>
          <label className={styles.field}>
            <span className={ui.label}>{t('feedback.contact.fieldSubject')}</span>
            <input
              className={ui.input}
              required
              minLength={3}
              maxLength={120}
              value={subject}
              onChange={(e) => {
                setSubject(e.target.value);
              }}
            />
          </label>
          <label className={styles.field}>
            <span className={ui.label}>{t('feedback.contact.fieldMessage')}</span>
            <textarea
              className={styles.textarea}
              required
              maxLength={2000}
              rows={5}
              value={message}
              onChange={(e) => {
                setMessage(e.target.value);
              }}
            />
          </label>
          <Messages error={error} done={done} />
          <button type="submit" className={ui.primary} disabled={busy}>
            {t('feedback.contact.submit')}
          </button>
        </form>
      </details>

      <h2 className={ui.subheading}>{t('feedback.contact.mine')}</h2>
      {tickets?.length === 0 && <p className={ui.text}>{t('feedback.contact.empty')}</p>}
      <ul className={ui.list}>
        {tickets?.map((ticket) => (
          <li key={ticket.id} className={styles.ticket}>
            <button
              type="button"
              className={styles.ticketHead}
              aria-expanded={open === ticket.id}
              onClick={() => {
                setOpen(open === ticket.id ? null : ticket.id);
              }}
            >
              <span className={ui.name}>{ticket.subject}</span>
              <span className={styles.meta}>
                {t(`feedback.contact.category.${ticket.category}`)} ·{' '}
                {new Intl.DateTimeFormat(i18n.language, { dateStyle: 'medium' }).format(
                  new Date(ticket.updatedAt),
                )}
              </span>
              <span className={styles.status} data-status={ticket.answerWaiting ? 'answer' : ticket.status}>
                {t(`feedback.contact.status.${ticket.status}`)}
              </span>
            </button>
            {open === ticket.id && <Thread id={ticket.id} onChanged={refresh} />}
          </li>
        ))}
      </ul>
    </>
  );
}

function Thread({ id, onChanged }: { id: number; onChanged: () => void }) {
  const { t, i18n } = useTranslation();
  const [ticket, setTicket] = useState<Ticket | null>(null);
  const [reply, setReply] = useState('');
  const { busy, error, run } = useAction();

  const load = useCallback(() => {
    void feedbackApi.ticket(id).then(setTicket);
  }, [id]);
  useEffect(load, [load]);

  if (!ticket) {
    return null;
  }
  const closed = ticket.status === 'closed';
  return (
    <div className={styles.thread}>
      <ol className={styles.messages}>
        {ticket.messages.map((m) => (
          <li key={m.id} className={styles.message} data-from={m.from}>
            <p className={ui.label}>
              {m.from === 'team' ? t('feedback.contact.team') : t('feedback.contact.you')} ·{' '}
              {new Intl.DateTimeFormat(i18n.language, { dateStyle: 'medium', timeStyle: 'short' }).format(
                new Date(m.at),
              )}
            </p>
            <p className={styles.messageBody}>{m.body}</p>
          </li>
        ))}
      </ol>
      {closed ? (
        <p className={ui.text}>{t('feedback.contact.closedHint')}</p>
      ) : (
        <form
          className={ui.section}
          onSubmit={run(async () => {
            await feedbackApi.reply(id, reply);
            setReply('');
            load();
            onChanged();
            return null;
          })}
        >
          <label className={styles.field}>
            <span className={ui.label}>{t('feedback.contact.replyLabel')}</span>
            <textarea
              className={styles.textarea}
              required
              maxLength={2000}
              rows={3}
              value={reply}
              onChange={(e) => {
                setReply(e.target.value);
              }}
            />
          </label>
          <Messages error={error} done={null} />
          <button type="submit" className={ui.primary} disabled={busy}>
            {t('feedback.contact.reply')}
          </button>
        </form>
      )}
    </div>
  );
}

function Report() {
  const { t } = useTranslation();
  const siteKey = useGame((s) => s.session?.turnstileSiteKey ?? '');
  const [pseudo, setPseudo] = useState(() => new URLSearchParams(window.location.search).get('pseudo') ?? '');
  const [category, setCategory] = useState<ReportCategory>('pseudo');
  const [details, setDetails] = useState('');
  const { busy, error, done, run } = useAction();

  return (
    <form
      className={ui.section}
      onSubmit={run(async () => {
        await feedbackApi.report({ pseudo, category, details, turnstileToken: await humanToken(siteKey) });
        setPseudo('');
        setDetails('');
        return t('feedback.report.sent');
      })}
    >
      <p className={ui.text}>{t('feedback.report.intro')}</p>
      <label className={styles.field}>
        <span className={ui.label}>{t('feedback.report.fieldPseudo')}</span>
        <input
          className={ui.input}
          required
          autoComplete="off"
          value={pseudo}
          onChange={(e) => {
            setPseudo(e.target.value);
          }}
        />
      </label>
      <label className={styles.field}>
        <span className={ui.label}>{t('feedback.report.fieldCategory')}</span>
        <select
          className={styles.select}
          value={category}
          onChange={(e) => {
            setCategory(e.target.value as ReportCategory);
          }}
        >
          {REPORT_CATEGORIES.map((c) => (
            <option key={c} value={c}>
              {t(`feedback.report.category.${c}`)}
            </option>
          ))}
        </select>
      </label>
      <label className={styles.field}>
        <span className={ui.label}>{t('feedback.report.fieldDetails')}</span>
        <textarea
          className={styles.textarea}
          maxLength={1000}
          rows={4}
          value={details}
          onChange={(e) => {
            setDetails(e.target.value);
          }}
        />
      </label>
      <Messages error={error} done={done} />
      <button type="submit" className={ui.primary} disabled={busy}>
        {t('feedback.report.submit')}
      </button>
    </form>
  );
}
