import { useState, type ReactNode, type SyntheticEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { ApiError } from './api.ts';
import styles from './styles/admin.module.css';

/** The translation key of a failure: stable API codes become `errors.<code>`. */
export function errorKey(error: unknown): string {
  return `errors.${error instanceof ApiError ? error.code : 'request.failed'}`;
}

export function ErrorText({ error }: { error: unknown }) {
  const { t } = useTranslation();
  if (!error) {
    return null;
  }
  return (
    <p className={styles.error} role="alert">
      {t(errorKey(error), { defaultValue: t('errors.request.failed') })}
    </p>
  );
}

export function Notice({ children }: { children: ReactNode }) {
  return children ? (
    <p className={styles.notice} role="status">
      {children}
    </p>
  ) : null;
}

/** A centred, single-purpose page: signing in, enrolment, errors. */
export function Card({ title, children }: { title: string; children: ReactNode }) {
  return (
    <main className={styles.card}>
      <p className={styles.brand}>NobodyFalls</p>
      <h1 className={styles.cardTitle}>{title}</h1>
      {children}
    </main>
  );
}

/** A six-digit code field, with the attributes phones use to suggest the code. */
export function CodeField({
  label,
  value,
  onChange,
}: {
  label: string;
  value: string;
  onChange: (v: string) => void;
}) {
  return (
    <label className={styles.field}>
      <span className={styles.label}>{label}</span>
      <input
        className={styles.codeInput}
        inputMode="numeric"
        autoComplete="one-time-code"
        pattern="[0-9 ]*"
        maxLength={7}
        required
        value={value}
        onChange={(e) => {
          onChange(e.target.value);
        }}
      />
    </label>
  );
}

/**
 * A form that runs one async action, shows its error, and blocks double submits.
 * The action receives nothing: it reads the component's own state.
 */
export function useSubmit(action: () => Promise<void>) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const onSubmit = (event?: SyntheticEvent) => {
    event?.preventDefault();
    if (busy) {
      return;
    }
    setBusy(true);
    setError(null);
    action()
      .catch((e: unknown) => {
        setError(e);
      })
      .finally(() => {
        setBusy(false);
      });
  };
  return { busy, error, onSubmit };
}
