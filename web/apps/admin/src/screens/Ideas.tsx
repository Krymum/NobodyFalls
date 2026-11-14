import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { adminApi, IDEA_STATUSES, type AdminIdea } from '../api.ts';
import { formatDateTime } from '../i18n/index.ts';
import styles from '../styles/admin.module.css';
import { ErrorText, Notice, useSubmit } from '../ui.tsx';

function IdeaRow({ idea, onChanged }: { idea: AdminIdea; onChanged: (message: string) => void }) {
  const { t, i18n } = useTranslation();
  const [status, setStatus] = useState(idea.status);
  const [answer, setAnswer] = useState(idea.staffAnswer ?? '');
  const [into, setInto] = useState('');

  const save = useSubmit(async () => {
    await adminApi.updateIdea(idea.id, { status, staffAnswer: answer });
    onChanged(t('ideas.saved'));
  });
  const toggleHidden = useSubmit(async () => {
    const hiddenReason = idea.hidden ? undefined : (window.prompt(t('ideas.hiddenReason')) ?? '');
    await adminApi.updateIdea(idea.id, {
      hidden: !idea.hidden,
      ...(hiddenReason !== undefined ? { hiddenReason } : {}),
    });
    onChanged(t('ideas.saved'));
  });
  const merge = useSubmit(async () => {
    await adminApi.mergeIdea(idea.id, Number(into));
    onChanged(t('ideas.saved'));
  });

  return (
    <li className={styles.ideaItem} data-hidden={idea.hidden}>
      <div className={styles.row}>
        <span className={styles.mono}>#{idea.id}</span>
        <h2 className={styles.sectionTitle}>{idea.title}</h2>
        <span className={styles.muted}>
          {t('ideas.votes', { count: idea.votes })} ·{' '}
          {idea.authorPseudo ? t('ideas.by', { name: idea.authorPseudo }) : ''} · {idea.locale.toUpperCase()}{' '}
          · {formatDateTime(idea.createdAt, i18n.language)}
        </span>
        {idea.hidden && <span className={styles.attention}>{t('ideas.hidden')}</span>}
        {idea.mergedInto !== null && (
          <span className={styles.muted}>{t('ideas.merged', { id: idea.mergedInto })}</span>
        )}
      </div>
      <p className={styles.prewrap}>{idea.body}</p>
      {idea.mergedInto === null && (
        <form className={styles.inlineForm} onSubmit={save.onSubmit}>
          <label className={styles.field}>
            <span className={styles.label}>{t('ideas.status')}</span>
            <select
              className={styles.input}
              value={status}
              onChange={(e) => {
                setStatus(e.target.value as AdminIdea['status']);
              }}
            >
              {IDEA_STATUSES.map((s) => (
                <option key={s} value={s}>
                  {t(`ideas.statuses.${s}`)}
                </option>
              ))}
            </select>
          </label>
          <label className={`${styles.field} ${styles.grow}`}>
            <span className={styles.label}>{t('ideas.answer')}</span>
            <textarea
              className={styles.input}
              rows={2}
              maxLength={2000}
              value={answer}
              onChange={(e) => {
                setAnswer(e.target.value);
              }}
            />
          </label>
          <button type="submit" className={styles.primary} disabled={save.busy}>
            {t('common.save')}
          </button>
          <button
            type="button"
            className={styles.secondary}
            disabled={toggleHidden.busy}
            onClick={() => {
              toggleHidden.onSubmit();
            }}
          >
            {idea.hidden ? t('ideas.unhide') : t('ideas.hide')}
          </button>
          <label className={styles.field}>
            <span className={styles.label}>{t('ideas.mergeInto')}</span>
            <input
              className={styles.input}
              inputMode="numeric"
              size={6}
              value={into}
              onChange={(e) => {
                setInto(e.target.value.replace(/\D/g, ''));
              }}
            />
          </label>
          <button
            type="button"
            className={styles.secondary}
            disabled={merge.busy || !into}
            onClick={() => {
              merge.onSubmit();
            }}
          >
            {t('ideas.merge')}
          </button>
          <ErrorText error={save.error ?? toggleHidden.error ?? merge.error} />
        </form>
      )}
    </li>
  );
}

/** Ideas: public status and answer, hiding, merging duplicates (GAME_DESIGN §9). */
export function Ideas() {
  const { t } = useTranslation();
  const [status, setStatus] = useState('');
  const [showHidden, setShowHidden] = useState(false);
  const [ideas, setIdeas] = useState<AdminIdea[] | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [message, setMessage] = useState<string | null>(null);

  const load = useCallback(() => {
    adminApi.ideas(status, showHidden).then(setIdeas).catch(setError);
  }, [status, showHidden]);
  useEffect(load, [load]);

  return (
    <section aria-labelledby="ideas-title">
      <h1 id="ideas-title" className={styles.pageTitle}>
        {t('ideas.title')}
      </h1>
      <div className={styles.toolbar}>
        <label className={styles.field}>
          <span className={styles.label}>{t('ideas.filter')}</span>
          <select
            className={styles.input}
            value={status}
            onChange={(e) => {
              setStatus(e.target.value);
            }}
          >
            <option value="">{t('ideas.all')}</option>
            {IDEA_STATUSES.map((s) => (
              <option key={s} value={s}>
                {t(`ideas.statuses.${s}`)}
              </option>
            ))}
          </select>
        </label>
        <label className={styles.check}>
          <input
            type="checkbox"
            checked={showHidden}
            onChange={(e) => {
              setShowHidden(e.target.checked);
            }}
          />
          {t('ideas.showHidden')}
        </label>
      </div>
      <Notice>{message}</Notice>
      <ErrorText error={error} />
      {ideas?.length === 0 && <p className={styles.muted}>{t('common.noResults')}</p>}
      <ul className={styles.plainList}>
        {ideas?.map((idea) => (
          <IdeaRow
            key={`${idea.id}:${idea.status}:${idea.hidden}:${idea.mergedInto ?? ''}`}
            idea={idea}
            onChanged={(text) => {
              setMessage(text);
              load();
            }}
          />
        ))}
      </ul>
    </section>
  );
}
