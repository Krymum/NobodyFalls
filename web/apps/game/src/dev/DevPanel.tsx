import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { devApi } from '../api/client.ts';
import { game } from '../core/store.ts';
import ui from '../ui/ui.module.css';
import styles from '../screens/screens.module.css';

/**
 * Development tools (GAME_DESIGN §14): move the simulated clock, add fake players, let
 * bots play, run midnight. Only rendered when the server says dev tools are on.
 */
export function DevPanel() {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const [status, setStatus] = useState<string | null>(null);

  const run = (action: () => Promise<unknown>) => () => {
    setStatus('…');
    action()
      .then(() => game.refreshAll())
      .then(() => {
        setStatus(t('dev.done'));
      })
      .catch((e: unknown) => {
        setStatus(String(e));
      });
  };

  if (!open) {
    return (
      <button
        type="button"
        className={styles.devToggle}
        onClick={() => {
          setOpen(true);
        }}
      >
        DEV
      </button>
    );
  }

  return (
    <div className={styles.devPanel} role="dialog" aria-label={t('dev.panel')}>
      <button
        type="button"
        className={ui.secondary}
        onClick={run(() => devApi.clock({ toMidnightMinusSeconds: 10 }))}
      >
        {t('dev.toMidnight')}
      </button>
      <button
        type="button"
        className={ui.secondary}
        onClick={run(() => devApi.clock({ advanceMinutes: 60 }))}
      >
        {t('dev.plusHour')}
      </button>
      <button type="button" className={ui.secondary} onClick={run(() => devApi.clock({ reset: true }))}>
        {t('dev.resetClock')}
      </button>
      <button type="button" className={ui.secondary} onClick={run(() => devApi.seed(20))}>
        {t('dev.seed')}
      </button>
      <button type="button" className={ui.secondary} onClick={run(() => devApi.bots(0.7))}>
        {t('dev.bots')}
      </button>
      <button type="button" className={ui.secondary} onClick={run(() => devApi.tick())}>
        {t('dev.tick')}
      </button>
      <button
        type="button"
        className={ui.secondary}
        onClick={run(async () => {
          await devApi.staffMe();
          window.location.assign('/admin/');
        })}
      >
        {t('dev.admin')}
      </button>
      {status && (
        <span className={ui.text} role="status">
          {status}
        </span>
      )}
      <button
        type="button"
        className={ui.secondary}
        onClick={() => {
          setOpen(false);
        }}
      >
        {t('common.close')}
      </button>
    </div>
  );
}
