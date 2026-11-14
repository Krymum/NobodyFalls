import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useAdmin } from '../store.ts';
import styles from '../styles/admin.module.css';
import { CodeField, ErrorText, useSubmit } from '../ui.tsx';

/** Asked before a sensitive action when the last code is more than five minutes old. */
export function ReauthDialog() {
  const { t } = useTranslation();
  const pending = useAdmin((s) => s.reauth);
  const confirm = useAdmin((s) => s.confirmReauth);
  const cancel = useAdmin((s) => s.cancelReauth);
  const [code, setCode] = useState('');
  const dialog = useRef<HTMLDialogElement>(null);
  const submit = useSubmit(async () => {
    await confirm(code);
    setCode('');
  });

  useEffect(() => {
    const element = dialog.current;
    if (pending && element && !element.open) {
      element.showModal();
    } else if (!pending && element?.open) {
      element.close();
    }
  }, [pending]);

  return (
    <dialog
      ref={dialog}
      className={styles.dialog}
      aria-labelledby="reauth-title"
      onCancel={(e) => {
        e.preventDefault();
        cancel();
      }}
    >
      {/* Rendered only while asked, so that its fields never mix with the page's. */}
      {pending ? (
        <form className={styles.form} onSubmit={submit.onSubmit}>
          <h2 id="reauth-title" className={styles.sectionTitle}>
            {t('reauth.title')}
          </h2>
          <p className={styles.muted}>{t('reauth.body')}</p>
          <CodeField label={t('reauth.code')} value={code} onChange={setCode} />
          <ErrorText error={submit.error} />
          <div className={styles.row}>
            <button type="submit" className={styles.primary} disabled={submit.busy}>
              {t('reauth.confirm')}
            </button>
            <button type="button" className={styles.secondary} onClick={cancel}>
              {t('reauth.cancel')}
            </button>
          </div>
        </form>
      ) : null}
    </dialog>
  );
}
