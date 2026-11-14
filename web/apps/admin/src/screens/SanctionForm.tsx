import { useId, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { SANCTION_KINDS, type SanctionInput, type SanctionKind } from '../api.ts';
import styles from '../styles/admin.module.css';

/** Which kinds the member may give (their permissions); the server checks again. */
export function allowedKinds(permissions: readonly string[]): SanctionKind[] {
  return SANCTION_KINDS.filter((kind) =>
    permissions.includes(
      {
        warning: 'sanctions.warn',
        pseudo_reset: 'sanctions.reset_pseudo',
        score_exclusion: 'sanctions.exclude_score',
        ban: 'sanctions.ban_temporary',
      }[kind],
    ),
  );
}

/** The fields of a sanction, as a controlled value (used alone or inside a report decision). */
export function SanctionFields({
  value,
  onChange,
  kinds,
}: {
  value: SanctionInput;
  onChange: (value: SanctionInput) => void;
  kinds: readonly SanctionKind[];
}) {
  const { t } = useTranslation();
  const hintId = useId();
  const withEnd = value.kind === 'ban' || value.kind === 'score_exclusion';
  const needsMessage = value.kind === 'warning' || value.kind === 'ban';
  return (
    <div className={styles.form}>
      <label className={styles.field}>
        <span className={styles.label}>{t('sanction.kind')}</span>
        <select
          className={styles.input}
          value={value.kind}
          onChange={(e) => {
            const kind = e.target.value as SanctionKind;
            onChange({
              ...value,
              kind,
              endsAt: kind === 'ban' || kind === 'score_exclusion' ? value.endsAt : null,
            });
          }}
        >
          {kinds.map((kind) => (
            <option key={kind} value={kind}>
              {t(`sanction.kinds.${kind}`)}
            </option>
          ))}
        </select>
      </label>
      <label className={styles.field}>
        <span className={styles.label}>{t('sanction.reason')}</span>
        <textarea
          className={styles.input}
          rows={2}
          required
          minLength={3}
          maxLength={500}
          value={value.reason}
          onChange={(e) => {
            onChange({ ...value, reason: e.target.value });
          }}
        />
      </label>
      {(needsMessage || value.kind === 'score_exclusion') && (
        <label className={styles.field}>
          <span className={styles.label}>{t('sanction.publicMessage')}</span>
          <textarea
            className={styles.input}
            rows={2}
            required={needsMessage}
            maxLength={500}
            value={value.publicMessage ?? ''}
            onChange={(e) => {
              onChange({ ...value, publicMessage: e.target.value || null });
            }}
          />
        </label>
      )}
      {withEnd && (
        <div className={styles.field}>
          <label className={styles.field}>
            <span className={styles.label}>{t('sanction.endsAt')}</span>
            <input
              className={styles.input}
              type="datetime-local"
              aria-describedby={hintId}
              value={value.endsAt ? value.endsAt.slice(0, 16) : ''}
              onChange={(e) => {
                onChange({
                  ...value,
                  endsAt: e.target.value ? new Date(e.target.value).toISOString() : null,
                });
              }}
            />
          </label>
          <span id={hintId} className={styles.hint}>
            {t('sanction.endsAtHint')}
          </span>
        </div>
      )}
    </div>
  );
}

export function useSanctionDraft(kinds: readonly SanctionKind[]) {
  return useState<SanctionInput>({
    kind: kinds[0] ?? 'warning',
    reason: '',
    publicMessage: null,
    endsAt: null,
  });
}
