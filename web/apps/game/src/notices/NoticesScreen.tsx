import i18next from 'i18next';
import { useState, type SyntheticEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'wouter';
import { request } from '../api/client.ts';
import { errorKey, game, useGame } from '../core/store.ts';
import ui from '../ui/ui.module.css';
import en from './i18n/en.json';
import fr from './i18n/fr.json';

// Rarely needed: these screens and their texts load only for a sanctioned player.
i18next.addResourceBundle('fr', 'translation', fr, true, false);
i18next.addResourceBundle('en', 'translation', en, true, false);

/**
 * What a sanctioned player sees before the game (GAME_DESIGN §9): a ban with its end and a
 * way to contest it, a warning to acknowledge, or a new pseudo to choose after a reset.
 */
export function NoticesScreen() {
  const notices = useGame((s) => s.notices);
  if (notices?.ban) {
    return <Ban until={notices.ban.until} message={notices.ban.message} />;
  }
  const warning = notices?.warnings[0];
  if (warning) {
    return <Warning id={warning.id} message={warning.message} />;
  }
  return notices?.pseudoResetPending ? <NewPseudo /> : null;
}

function Ban({ until, message }: { until: string | null; message: string | null }) {
  const { t, i18n } = useTranslation();
  return (
    <main className={ui.page}>
      <div className={ui.section}>
        <h1 className={ui.heading}>{t(until ? 'notices.ban.title' : 'notices.ban.titleForever')}</h1>
        {message && <p className={ui.text}>{message}</p>}
        {until && (
          <p className={ui.label}>
            {t('notices.ban.until', {
              date: new Intl.DateTimeFormat(i18n.language, { dateStyle: 'long', timeStyle: 'short' }).format(
                new Date(until),
              ),
            })}
          </p>
        )}
        <p className={ui.text}>{t('notices.ban.intro')}</p>
      </div>
      <Link href="/profile/feedback?tab=contact" className={ui.primary}>
        {t('notices.ban.contest')}
      </Link>
      <button
        type="button"
        className={ui.secondary}
        onClick={() => {
          void game.logout().then(() => {
            window.location.assign(`/${i18n.language}/`);
          });
        }}
      >
        {t('notices.ban.logout')}
      </button>
    </main>
  );
}

function useSubmit(action: () => Promise<void>) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const onSubmit = (event?: SyntheticEvent): void => {
    event?.preventDefault();
    setBusy(true);
    setError(null);
    action()
      .catch((e: unknown) => {
        setError(errorKey(e));
      })
      .finally(() => {
        setBusy(false);
      });
  };
  return { busy, error, onSubmit };
}

function Warning({ id, message }: { id: number; message: string }) {
  const { t } = useTranslation();
  const ack = useSubmit(async () => {
    await request('POST', `/api/v1/me/notices/warnings/${id}/ack`);
    await game.loadNotices();
  });
  return (
    <main className={ui.page}>
      <div className={ui.section} role="alertdialog" aria-labelledby="warning-title">
        <h1 id="warning-title" className={ui.heading}>
          {t('notices.warning.title')}
        </h1>
        <p className={ui.text}>{message}</p>
        {ack.error && (
          <p className={ui.notice} role="alert">
            {t(ack.error)}
          </p>
        )}
        <button
          type="button"
          className={ui.primary}
          disabled={ack.busy}
          onClick={() => {
            ack.onSubmit();
          }}
        >
          {t('notices.warning.ack')}
        </button>
      </div>
    </main>
  );
}

function NewPseudo() {
  const { t } = useTranslation();
  const [pseudo, setPseudo] = useState('');
  const submit = useSubmit(async () => {
    await request('PUT', '/api/v1/me/pseudo', { pseudo });
    await game.loadSession();
  });
  return (
    <main className={ui.page}>
      <form className={ui.section} onSubmit={submit.onSubmit}>
        <h1 className={ui.heading}>{t('notices.pseudo.title')}</h1>
        <p className={ui.text}>{t('notices.pseudo.intro')}</p>
        <label className={ui.section}>
          <span className={ui.label}>{t('notices.pseudo.label')}</span>
          <input
            className={ui.input}
            required
            minLength={3}
            maxLength={20}
            value={pseudo}
            onChange={(e) => {
              setPseudo(e.target.value);
            }}
          />
        </label>
        {submit.error && (
          <p className={ui.notice} role="alert">
            {t(submit.error)}
          </p>
        )}
        <button type="submit" className={ui.primary} disabled={submit.busy}>
          {t('notices.pseudo.submit')}
        </button>
      </form>
    </main>
  );
}
