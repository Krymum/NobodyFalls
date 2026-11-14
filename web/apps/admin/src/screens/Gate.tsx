import { useTranslation } from 'react-i18next';
import { useAdmin } from '../store.ts';
import styles from '../styles/admin.module.css';
import { Card } from '../ui.tsx';

export function Loading() {
  const { t } = useTranslation();
  return (
    <main className={styles.card} aria-busy="true">
      <p className={styles.muted}>{t('gate.loading')}</p>
    </main>
  );
}

/** The admin rides on the player session: sign in to the game first (ADR 0018). */
export function SignedOut() {
  const { t } = useTranslation();
  return (
    <Card title={t('gate.signedOut.title')}>
      <p className={styles.muted}>{t('gate.signedOut.body')}</p>
      <a className={styles.primary} href="/">
        {t('gate.signedOut.action')}
      </a>
    </Card>
  );
}

export function NoAccess() {
  const { t } = useTranslation();
  return (
    <Card title={t('gate.noAccess.title')}>
      <p className={styles.muted}>{t('gate.noAccess.body')}</p>
      <a className={styles.secondary} href="/">
        {t('gate.noAccess.action')}
      </a>
    </Card>
  );
}

export function LoadError({ code }: { code: string }) {
  const { t } = useTranslation();
  const load = useAdmin((s) => s.load);
  return (
    <Card title={t('gate.error.title')}>
      <p className={styles.error} role="alert">
        {t(`errors.${code}`, { defaultValue: t('errors.request.failed') })}
      </p>
      <button
        type="button"
        className={styles.primary}
        onClick={() => {
          void load();
        }}
      >
        {t('gate.error.retry')}
      </button>
    </Card>
  );
}
