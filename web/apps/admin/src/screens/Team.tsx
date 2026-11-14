import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { adminApi, ROLES, type Role, type TeamMember } from '../api.ts';
import { formatDateTime } from '../i18n/index.ts';
import { useAdmin } from '../store.ts';
import styles from '../styles/admin.module.css';
import { ErrorText, Notice, useSubmit } from '../ui.tsx';

function RolePicker({
  value,
  onChange,
  name,
  disabled = false,
}: {
  value: Role[];
  onChange: (roles: Role[]) => void;
  name: string;
  disabled?: boolean;
}) {
  const { t } = useTranslation();
  return (
    <div className={styles.row} role="group" aria-label={name}>
      {ROLES.map((role) => (
        <label key={role} className={styles.check}>
          <input
            type="checkbox"
            disabled={disabled}
            checked={value.includes(role)}
            onChange={(e) => {
              onChange(e.target.checked ? [...value, role] : value.filter((r) => r !== role));
            }}
          />
          {t(`roles.${role}`)}
        </label>
      ))}
    </div>
  );
}

function MemberRow({
  member,
  isMe,
  onChanged,
}: {
  member: TeamMember;
  isMe: boolean;
  onChanged: (message: string) => void;
}) {
  const { t, i18n } = useTranslation();
  const sensitive = useAdmin((s) => s.sensitive);
  const [roles, setRoles] = useState<Role[]>(member.roles);
  const dirty = [...roles].sort().join() !== [...member.roles].sort().join();

  const save = useSubmit(async () => {
    await sensitive(() => adminApi.changeRoles(member.playerId, roles));
    onChanged(t('team.saved', { pseudo: member.pseudo }));
  });
  const remove = useSubmit(async () => {
    if (window.confirm(t('team.removeConfirm', { pseudo: member.pseudo }))) {
      await sensitive(() => adminApi.removeMember(member.playerId));
      onChanged(t('team.removed', { pseudo: member.pseudo }));
    }
  });
  const reset = useSubmit(async () => {
    if (window.confirm(t('team.resetConfirm', { pseudo: member.pseudo }))) {
      await sensitive(() => adminApi.resetTotp(member.playerId));
      onChanged(t('team.resetDone', { pseudo: member.pseudo }));
    }
  });

  return (
    <tr>
      <th scope="row">
        {member.pseudo}
        {isMe ? <span className={styles.muted}> ({t('team.you')})</span> : null}
      </th>
      <td>
        <RolePicker
          value={roles}
          onChange={setRoles}
          name={`${t('team.roles')} — ${member.pseudo}`}
          disabled={isMe}
        />
      </td>
      <td>
        <span className={member.totpEnrolled ? undefined : styles.attention}>
          {member.totpEnrolled ? t('team.totpOn') : t('team.totpOff')}
        </span>
      </td>
      <td>{formatDateTime(member.grantedAt, i18n.language)}</td>
      <td>
        {isMe ? null : (
          <div className={styles.row}>
            <button
              type="button"
              className={styles.small}
              disabled={!dirty || save.busy}
              onClick={() => {
                save.onSubmit();
              }}
            >
              {t('team.save')}
            </button>
            <button
              type="button"
              className={styles.smallSecondary}
              disabled={reset.busy}
              onClick={() => {
                reset.onSubmit();
              }}
            >
              {t('team.resetTotp')}
            </button>
            <button
              type="button"
              className={styles.smallDanger}
              disabled={remove.busy}
              onClick={() => {
                remove.onSubmit();
              }}
            >
              {t('team.remove')}
            </button>
          </div>
        )}
        <ErrorText error={save.error ?? remove.error ?? reset.error} />
      </td>
    </tr>
  );
}

/** Who is in the team, with which roles (ADR 0017). Every change asks for a fresh code. */
export function Team({ me }: { me: string }) {
  const { t } = useTranslation();
  const sensitive = useAdmin((s) => s.sensitive);
  const [members, setMembers] = useState<TeamMember[] | null>(null);
  const [loadError, setLoadError] = useState<unknown>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [pseudo, setPseudo] = useState('');
  const [roles, setRoles] = useState<Role[]>(['support']);

  const refresh = useCallback(() => {
    adminApi
      .team()
      .then(setMembers)
      .catch((e: unknown) => {
        setLoadError(e);
      });
  }, []);
  useEffect(refresh, [refresh]);

  const changed = (text: string) => {
    setMessage(text);
    refresh();
  };

  const add = useSubmit(async () => {
    const member = await sensitive(() => adminApi.addMember(pseudo.trim(), roles));
    setPseudo('');
    changed(t('team.added', { pseudo: member.pseudo }));
  });

  return (
    <section aria-labelledby="team-title">
      <h1 id="team-title" className={styles.pageTitle}>
        {t('team.title')}
      </h1>
      <Notice>{message}</Notice>
      <ErrorText error={loadError} />

      <form className={styles.inlineForm} onSubmit={add.onSubmit} aria-labelledby="team-add">
        <h2 id="team-add" className={styles.sectionTitle}>
          {t('team.addTitle')}
        </h2>
        <label className={styles.field}>
          <span className={styles.label}>{t('team.pseudo')}</span>
          <input
            className={styles.input}
            required
            value={pseudo}
            onChange={(e) => {
              setPseudo(e.target.value);
            }}
          />
        </label>
        <div className={styles.field}>
          <span className={styles.label}>{t('team.roles')}</span>
          <RolePicker value={roles} onChange={setRoles} name={t('team.roles')} />
        </div>
        <button type="submit" className={styles.primary} disabled={add.busy || roles.length === 0}>
          {t('team.add')}
        </button>
        <ErrorText error={add.error} />
      </form>

      {members && members.length === 0 ? <p className={styles.muted}>{t('team.empty')}</p> : null}
      {members && members.length > 0 ? (
        <div className={styles.tableWrap}>
          <table className={styles.table}>
            <thead>
              <tr>
                <th scope="col">{t('team.columns.pseudo')}</th>
                <th scope="col">{t('team.columns.roles')}</th>
                <th scope="col">{t('team.columns.totp')}</th>
                <th scope="col">{t('team.columns.since')}</th>
                <th scope="col">{t('team.columns.actions')}</th>
              </tr>
            </thead>
            <tbody>
              {members.map((m) => (
                <MemberRow
                  key={`${m.playerId}:${m.roles.join()}`}
                  member={m}
                  isMe={m.playerId === me}
                  onChanged={changed}
                />
              ))}
            </tbody>
          </table>
        </div>
      ) : null}
    </section>
  );
}
