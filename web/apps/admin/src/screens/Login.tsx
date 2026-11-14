import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { adminApi } from '../api.ts';
import { useAdmin } from '../store.ts';
import styles from '../styles/admin.module.css';
import { Card, CodeField, ErrorText, useSubmit } from '../ui.tsx';

/** Opens the admin session with a code from the authenticator, or a recovery code. */
export function Login() {
  const { t } = useTranslation();
  const load = useAdmin((s) => s.load);
  const [useRecovery, setUseRecovery] = useState(false);
  const [code, setCode] = useState('');
  const [recovery, setRecovery] = useState('');

  const submit = useSubmit(async () => {
    await (useRecovery ? adminApi.login(null, recovery) : adminApi.login(code, null));
    await load();
  });

  return (
    <Card title={t('login.title')}>
      <form className={styles.form} onSubmit={submit.onSubmit}>
        {useRecovery ? (
          <label className={styles.field}>
            <span className={styles.label}>{t('login.recovery')}</span>
            <input
              className={styles.input}
              autoComplete="off"
              required
              value={recovery}
              onChange={(e) => {
                setRecovery(e.target.value);
              }}
            />
            <span className={styles.hint}>{t('login.recoveryHint')}</span>
          </label>
        ) : (
          <>
            <p className={styles.muted}>{t('login.body')}</p>
            <CodeField label={t('login.code')} value={code} onChange={setCode} />
          </>
        )}
        <ErrorText error={submit.error} />
        <button type="submit" className={styles.primary} disabled={submit.busy}>
          {t('login.submit')}
        </button>
      </form>
      <button
        type="button"
        className={styles.link}
        onClick={() => {
          setUseRecovery(!useRecovery);
        }}
      >
        {useRecovery ? t('login.useCode') : t('login.useRecovery')}
      </button>
    </Card>
  );
}
