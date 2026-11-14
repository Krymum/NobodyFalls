import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { encode } from 'uqr';
import { adminApi, type Enrollment } from '../api.ts';
import { useAdmin } from '../store.ts';
import styles from '../styles/admin.module.css';
import { Card, CodeField, ErrorText, Notice, useSubmit } from '../ui.tsx';

/** The QR code, drawn as SVG squares (no HTML injection, CSP-friendly). */
function QrCode({ text, label }: { text: string; label: string }) {
  const { path, size } = useMemo(() => {
    const { data, size: modules } = encode(text, { ecc: 'M', border: 2 });
    const squares = data
      .flatMap((row, y) => row.map((dark, x) => (dark ? `M${x} ${y}h1v1h-1z` : '')))
      .join('');
    return { path: squares, size: modules };
  }, [text]);
  return (
    <svg
      className={styles.qr}
      viewBox={`0 0 ${size} ${size}`}
      role="img"
      aria-label={label}
      shapeRendering="crispEdges"
    >
      <rect width={size} height={size} fill="#fff" />
      <path d={path} fill="#000" />
    </svg>
  );
}

/** First visit of a team member: set up an authenticator, then keep the recovery codes (ADR 0019). */
export function Enroll() {
  const { t } = useTranslation();
  const load = useAdmin((s) => s.load);
  const [enrollment, setEnrollment] = useState<Enrollment | null>(null);
  const [code, setCode] = useState('');
  const [recovery, setRecovery] = useState<string[] | null>(null);
  const [copied, setCopied] = useState(false);

  const start = useSubmit(async () => {
    setEnrollment(await adminApi.enroll());
  });
  const confirm = useSubmit(async () => {
    setRecovery((await adminApi.confirm(code)).recoveryCodes);
  });

  if (recovery) {
    return (
      <Card title={t('enroll.recoveryTitle')}>
        <p className={styles.muted}>{t('enroll.recoveryBody')}</p>
        <ol className={styles.recovery} data-testid="recovery-codes">
          {recovery.map((c) => (
            <li key={c}>
              <code>{c}</code>
            </li>
          ))}
        </ol>
        <div className={styles.row}>
          <button
            type="button"
            className={styles.secondary}
            onClick={() => {
              void navigator.clipboard.writeText(recovery.join('\n')).then(() => {
                setCopied(true);
              });
            }}
          >
            {t('enroll.copy')}
          </button>
          <Notice>{copied ? t('enroll.copied') : null}</Notice>
        </div>
        <button
          type="button"
          className={styles.primary}
          onClick={() => {
            void load();
          }}
        >
          {t('enroll.done')}
        </button>
      </Card>
    );
  }

  if (!enrollment) {
    return (
      <Card title={t('enroll.title')}>
        <p className={styles.muted}>{t('enroll.intro')}</p>
        <ErrorText error={start.error} />
        <button
          type="button"
          className={styles.primary}
          disabled={start.busy}
          onClick={() => {
            start.onSubmit();
          }}
        >
          {t('enroll.start')}
        </button>
      </Card>
    );
  }

  return (
    <Card title={t('enroll.title')}>
      <p>{t('enroll.scan')}</p>
      <QrCode text={enrollment.uri} label={t('enroll.qr')} />
      <p className={styles.muted}>{t('enroll.manual')}</p>
      <code className={styles.secret} data-testid="totp-secret">
        {enrollment.secret.match(/.{1,4}/g)?.join(' ')}
      </code>
      <form className={styles.form} onSubmit={confirm.onSubmit}>
        <p>{t('enroll.enter')}</p>
        <CodeField label={t('enroll.code')} value={code} onChange={setCode} />
        <ErrorText error={confirm.error} />
        <button type="submit" className={styles.primary} disabled={confirm.busy}>
          {t('enroll.confirm')}
        </button>
      </form>
    </Card>
  );
}
