import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, Route, Switch, useLocation } from 'wouter';
import {
  adminApi,
  type AdminSession,
  type FlagState,
  type Json,
  type Rules,
  type RulesState,
  type RulesVersion,
  type ScheduledStorm,
  type SeasonView,
} from '../api.ts';
import { formatDateTime } from '../i18n/index.ts';
import { useAdmin } from '../store.ts';
import styles from '../styles/admin.module.css';
import { ErrorText, Notice, useSubmit } from '../ui.tsx';

/** Every rule the team can tune, in the order of the form. Clock fields are "hh:mm". */
const FIELDS: readonly { group: string; path: string; kind: 'int' | 'clock' }[] = [
  { group: 'threshold', path: 'handsPerDay', kind: 'int' },
  { group: 'threshold', path: 'baseRequired', kind: 'int' },
  { group: 'threshold', path: 'lowPopulationRequired', kind: 'int' },
  { group: 'threshold', path: 'lowPopulationBelow', kind: 'int' },
  { group: 'threshold', path: 'fragileExtra', kind: 'int' },
  { group: 'threshold', path: 'stormExtra', kind: 'int' },
  { group: 'watchers', path: 'watchers.maxCount', kind: 'int' },
  { group: 'watchers', path: 'watchers.disappearAtPopulation', kind: 'int' },
  { group: 'watchers', path: 'watchers.handsPerWatcher', kind: 'int' },
  { group: 'watchers', path: 'watchers.passAt', kind: 'clock' },
  { group: 'points', path: 'pointsFriend', kind: 'int' },
  { group: 'points', path: 'pointsStranger', kind: 'int' },
  { group: 'points', path: 'lastRampartBonus', kind: 'int' },
  { group: 'times', path: 'stormNotice', kind: 'clock' },
  { group: 'times', path: 'returnCutoff', kind: 'clock' },
  { group: 'times', path: 'lastRampartWindow', kind: 'clock' },
  { group: 'times', path: 'friendReminderAt', kind: 'clock' },
  { group: 'social', path: 'friendLimit', kind: 'int' },
  { group: 'social', path: 'suggestionMinNights', kind: 'int' },
];
const GROUPS = ['threshold', 'watchers', 'points', 'times', 'social'] as const;

function read(rules: Rules, path: string): Json {
  return path
    .split('.')
    .reduce<Json>(
      (node, key) => (node && typeof node === 'object' && !Array.isArray(node) ? (node[key] ?? null) : null),
      rules,
    );
}

function write(rules: Rules, path: string, value: Json): Rules {
  const [head = '', ...rest] = path.split('.');
  if (rest.length === 0) {
    return { ...rules, [head]: value };
  }
  const child = rules[head];
  const inner = child && typeof child === 'object' && !Array.isArray(child) ? child : {};
  return { ...rules, [head]: write(inner, rest.join('.'), value) };
}

/** "06:00:00" ↔ "06:00": the server's durations and times of day, shown to the minute. */
const toClock = (value: Json) => (typeof value === 'string' ? value.slice(0, 5) : '');
const display = (kind: 'int' | 'clock', value: Json) =>
  kind === 'clock' ? toClock(value) : typeof value === 'number' ? String(value) : '';

function RulesPanel() {
  const { t, i18n } = useTranslation();
  const sensitive = useAdmin((s) => s.sensitive);
  const [state, setState] = useState<RulesState | null>(null);
  const [versions, setVersions] = useState<RulesVersion[]>([]);
  const [draft, setDraft] = useState<Rules | null>(null);
  const [comment, setComment] = useState('');
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<unknown>(null);

  const load = useCallback(() => {
    Promise.all([adminApi.rules(), adminApi.rulesVersions()])
      .then(([current, history]) => {
        setState(current);
        setVersions(history);
        setDraft(current.rules);
      })
      .catch((e: unknown) => {
        setError(e);
      });
  }, []);
  useEffect(load, [load]);

  const save = useSubmit(async () => {
    if (!draft) {
      return;
    }
    const saved = await sensitive(() => adminApi.saveRules(draft, comment));
    setMessage(t('config.rules.saved', { version: saved.version }));
    setComment('');
    load();
  });

  if (!state || !draft) {
    return <ErrorText error={error} />;
  }
  const changes = FIELDS.filter(
    (f) => display(f.kind, read(draft, f.path)) !== display(f.kind, read(state.rules, f.path)),
  );
  const label = (path: string) => t(`config.rules.fields.${path}`);

  return (
    <>
      <p className={styles.muted}>
        {t('config.rules.intro', { latest: state.latestVersion, today: state.todayVersion })}
      </p>
      <form onSubmit={save.onSubmit} aria-label={t('config.rules.title')}>
        {GROUPS.map((group) => (
          <fieldset key={group} className={styles.fieldset}>
            <legend className={styles.sectionTitle}>{t(`config.rules.groups.${group}`)}</legend>
            <div className={styles.configGrid}>
              {FIELDS.filter((f) => f.group === group).map((f) => (
                <label key={f.path} className={styles.field}>
                  <span className={styles.label}>{label(f.path)}</span>
                  {f.kind === 'int' ? (
                    <input
                      className={styles.input}
                      type="number"
                      inputMode="numeric"
                      step={1}
                      min={0}
                      required
                      value={display(f.kind, read(draft, f.path))}
                      onChange={(e) => {
                        setDraft(write(draft, f.path, e.target.value === '' ? null : Number(e.target.value)));
                      }}
                    />
                  ) : (
                    <input
                      className={styles.input}
                      inputMode="numeric"
                      pattern="[0-2][0-9]:[0-5][0-9]"
                      placeholder="hh:mm"
                      required
                      value={display(f.kind, read(draft, f.path))}
                      onChange={(e) => {
                        setDraft(write(draft, f.path, `${e.target.value}:00`));
                      }}
                    />
                  )}
                </label>
              ))}
            </div>
          </fieldset>
        ))}

        <h2 className={styles.sectionTitle}>{t('config.rules.review')}</h2>
        {changes.length === 0 ? (
          <p className={styles.muted}>{t('config.rules.noChange')}</p>
        ) : (
          <>
            <ul className={styles.plainList} aria-label={t('config.rules.review')}>
              {changes.map((f) => (
                <li key={f.path}>
                  {label(f.path)} :{' '}
                  <span className={styles.muted}>{display(f.kind, read(state.rules, f.path))}</span> →{' '}
                  <strong>{display(f.kind, read(draft, f.path))}</strong>
                </li>
              ))}
            </ul>
            <div className={styles.inlineForm}>
              <label className={`${styles.field} ${styles.grow}`}>
                <span className={styles.label}>{t('config.rules.comment')}</span>
                <input
                  className={styles.input}
                  required
                  minLength={3}
                  maxLength={200}
                  value={comment}
                  onChange={(e) => {
                    setComment(e.target.value);
                  }}
                />
              </label>
              <button type="submit" className={styles.primary} disabled={save.busy}>
                {t('config.rules.save')}
              </button>
              <button
                type="button"
                className={styles.secondary}
                onClick={() => {
                  setDraft(state.rules);
                }}
              >
                {t('config.rules.discard')}
              </button>
            </div>
          </>
        )}
        <Notice>{message}</Notice>
        <ErrorText error={save.error} />
      </form>

      <h2 className={styles.sectionTitle}>{t('config.rules.versions')}</h2>
      <table className={styles.table}>
        <thead>
          <tr>
            <th scope="col">{t('config.rules.version')}</th>
            <th scope="col">{t('config.rules.when')}</th>
            <th scope="col">{t('config.rules.author')}</th>
            <th scope="col">{t('config.rules.why')}</th>
            <th scope="col">
              <span className="nf-sr-only">{t('config.rules.actions')}</span>
            </th>
          </tr>
        </thead>
        <tbody>
          {versions.map((v) => (
            <tr key={v.version}>
              <th scope="row">
                {v.version}
                {v.version === state.todayVersion ? (
                  <span className={styles.muted}> · {t('config.rules.today')}</span>
                ) : null}
              </th>
              <td>{formatDateTime(v.createdAt, i18n.language)}</td>
              <td>{v.author ?? t('config.system')}</td>
              <td>{v.comment}</td>
              <td>
                {v.version === state.latestVersion ? null : (
                  <button
                    type="button"
                    className={styles.smallSecondary}
                    onClick={() => {
                      setDraft(v.rules);
                      setComment(t('config.rules.backTo', { version: v.version }));
                    }}
                  >
                    {t('config.rules.restore')}
                  </button>
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </>
  );
}

function StormsPanel() {
  const { t, i18n } = useTranslation();
  const sensitive = useAdmin((s) => s.sensitive);
  const [data, setData] = useState<{ today: string; storms: ScheduledStorm[] } | null>(null);
  const [day, setDay] = useState('');
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<unknown>(null);
  const load = useCallback(() => {
    adminApi
      .storms()
      .then(setData)
      .catch((e: unknown) => {
        setError(e);
      });
  }, []);
  useEffect(load, [load]);

  const schedule = useSubmit(async () => {
    await sensitive(() => adminApi.scheduleStorm(day));
    setMessage(t('config.storms.scheduled', { day: formatDay(day, i18n.language) }));
    setDay('');
    load();
  });
  const [cancelError, setCancelError] = useState<unknown>(null);
  const cancel = async (stormDay: string) => {
    if (!window.confirm(t('config.storms.cancelConfirm', { day: formatDay(stormDay, i18n.language) }))) {
      return;
    }
    try {
      setCancelError(null);
      await sensitive(() => adminApi.cancelStorm(stormDay));
      setMessage(t('config.storms.cancelled', { day: formatDay(stormDay, i18n.language) }));
      load();
    } catch (e) {
      setCancelError(e);
    }
  };

  if (!data) {
    return <ErrorText error={error} />;
  }
  const tomorrow = addDays(data.today, 1);
  return (
    <>
      <p className={styles.muted}>
        {t('config.storms.intro', { today: formatDay(data.today, i18n.language) })}
      </p>
      <form className={styles.inlineForm} onSubmit={schedule.onSubmit} aria-labelledby="storm-form">
        <h2 id="storm-form" className={styles.sectionTitle}>
          {t('config.storms.add')}
        </h2>
        <label className={styles.field}>
          <span className={styles.label}>{t('config.storms.day')}</span>
          <input
            className={styles.input}
            type="date"
            required
            min={tomorrow}
            value={day}
            onChange={(e) => {
              setDay(e.target.value);
            }}
          />
        </label>
        <button type="submit" className={styles.primary} disabled={schedule.busy}>
          {t('config.storms.schedule')}
        </button>
        <ErrorText error={schedule.error} />
      </form>
      <Notice>{message}</Notice>
      <ErrorText error={cancelError} />

      <h2 className={styles.sectionTitle}>{t('config.storms.list')}</h2>
      {data.storms.length === 0 ? (
        <p className={styles.muted}>{t('config.storms.none')}</p>
      ) : (
        <table className={styles.table}>
          <thead>
            <tr>
              <th scope="col">{t('config.storms.day')}</th>
              <th scope="col">{t('config.storms.state')}</th>
              <th scope="col">{t('config.rules.author')}</th>
              <th scope="col">
                <span className="nf-sr-only">{t('config.rules.actions')}</span>
              </th>
            </tr>
          </thead>
          <tbody>
            {data.storms.map((s) => {
              const state = s.day > data.today ? 'upcoming' : s.day === data.today ? 'today' : 'past';
              return (
                <tr key={s.day}>
                  <th scope="row">{formatDay(s.day, i18n.language)}</th>
                  <td className={state === 'today' ? styles.attention : undefined}>
                    {t(`config.storms.states.${state}`)}
                  </td>
                  <td>
                    {s.author ?? t('config.system')} · {formatDateTime(s.createdAt, i18n.language)}
                  </td>
                  <td>
                    {state === 'upcoming' ? (
                      <button
                        type="button"
                        className={styles.smallDanger}
                        onClick={() => {
                          void cancel(s.day);
                        }}
                      >
                        {t('config.storms.cancel')}
                      </button>
                    ) : null}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      )}
    </>
  );
}

function SeasonRow({
  season,
  today,
  onSaved,
}: {
  season: SeasonView;
  today: string;
  onSaved: (message: string) => void;
}) {
  const { t } = useTranslation();
  const sensitive = useAdmin((s) => s.sensitive);
  const [name, setName] = useState(season.name);
  const [opening, setOpening] = useState(season.openingDay);
  const [last, setLast] = useState(season.lastDay ?? '');
  const closed = season.lastDay !== null && season.lastDay < today;
  const opened = season.openingDay <= today;
  const save = useSubmit(async () => {
    await sensitive(() => adminApi.changeSeason(season.number, name, opening, last === '' ? null : last));
    onSaved(t('config.seasons.saved', { name }));
  });
  const state = closed ? 'closed' : opened ? 'open' : 'planned';
  return (
    <tr>
      <th scope="row">{season.number}</th>
      <td>
        <input
          className={styles.input}
          aria-label={`${t('config.seasons.name')} — ${season.number}`}
          disabled={closed}
          required
          maxLength={100}
          value={name}
          onChange={(e) => {
            setName(e.target.value);
          }}
        />
      </td>
      <td>
        <input
          className={styles.input}
          type="date"
          aria-label={`${t('config.seasons.opening')} — ${season.number}`}
          disabled={opened}
          required
          value={opening}
          onChange={(e) => {
            setOpening(e.target.value);
          }}
        />
      </td>
      <td>
        <input
          className={styles.input}
          type="date"
          aria-label={`${t('config.seasons.last')} — ${season.number}`}
          disabled={closed}
          min={today}
          value={last}
          onChange={(e) => {
            setLast(e.target.value);
          }}
        />
      </td>
      <td>{t(`config.seasons.states.${state}`)}</td>
      <td>
        {closed ? null : (
          <button
            type="button"
            className={styles.small}
            disabled={save.busy}
            onClick={() => {
              save.onSubmit();
            }}
          >
            {t('config.seasons.save')}
          </button>
        )}
        <ErrorText error={save.error} />
      </td>
    </tr>
  );
}

function SeasonsPanel() {
  const { t } = useTranslation();
  const sensitive = useAdmin((s) => s.sensitive);
  const [data, setData] = useState<{ today: string; seasons: SeasonView[] } | null>(null);
  const [version, setVersion] = useState(0);
  const [name, setName] = useState('');
  const [opening, setOpening] = useState('');
  const [last, setLast] = useState('');
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<unknown>(null);
  const load = useCallback(() => {
    adminApi
      .seasons()
      .then((seasons) => {
        setData(seasons);
        setVersion((v) => v + 1);
      })
      .catch((e: unknown) => {
        setError(e);
      });
  }, []);
  useEffect(load, [load]);
  const plan = useSubmit(async () => {
    await sensitive(() => adminApi.planSeason(name, opening, last === '' ? null : last));
    setMessage(t('config.seasons.planned', { name }));
    setName('');
    setOpening('');
    setLast('');
    load();
  });

  if (!data) {
    return <ErrorText error={error} />;
  }
  return (
    <>
      <p className={styles.muted}>{t('config.seasons.intro')}</p>
      <table className={styles.table}>
        <thead>
          <tr>
            <th scope="col">#</th>
            <th scope="col">{t('config.seasons.name')}</th>
            <th scope="col">{t('config.seasons.opening')}</th>
            <th scope="col">{t('config.seasons.last')}</th>
            <th scope="col">{t('config.storms.state')}</th>
            <th scope="col">
              <span className="nf-sr-only">{t('config.rules.actions')}</span>
            </th>
          </tr>
        </thead>
        <tbody>
          {data.seasons.map((s) => (
            <SeasonRow
              key={`${s.number}-${version}`}
              season={s}
              today={data.today}
              onSaved={(m) => {
                setMessage(m);
                load();
              }}
            />
          ))}
        </tbody>
      </table>
      <Notice>{message}</Notice>

      <form className={styles.inlineForm} onSubmit={plan.onSubmit} aria-labelledby="season-form">
        <h2 id="season-form" className={styles.sectionTitle}>
          {t('config.seasons.plan')}
        </h2>
        <label className={`${styles.field} ${styles.grow}`}>
          <span className={styles.label}>{t('config.seasons.name')}</span>
          <input
            className={styles.input}
            required
            maxLength={100}
            value={name}
            onChange={(e) => {
              setName(e.target.value);
            }}
          />
        </label>
        <label className={styles.field}>
          <span className={styles.label}>{t('config.seasons.opening')}</span>
          <input
            className={styles.input}
            type="date"
            required
            min={addDays(data.today, 1)}
            value={opening}
            onChange={(e) => {
              setOpening(e.target.value);
            }}
          />
        </label>
        <label className={styles.field}>
          <span className={styles.label}>{t('config.seasons.lastOptional')}</span>
          <input
            className={styles.input}
            type="date"
            min={opening || addDays(data.today, 1)}
            value={last}
            onChange={(e) => {
              setLast(e.target.value);
            }}
          />
        </label>
        <button type="submit" className={styles.primary} disabled={plan.busy}>
          {t('config.seasons.planButton')}
        </button>
        <ErrorText error={plan.error} />
      </form>
    </>
  );
}

function FlagsPanel() {
  const { t, i18n } = useTranslation();
  const sensitive = useAdmin((s) => s.sensitive);
  const [flags, setFlags] = useState<FlagState[] | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [message, setMessage] = useState<string | null>(null);
  const load = useCallback(() => {
    adminApi
      .flags()
      .then(setFlags)
      .catch((e: unknown) => {
        setError(e);
      });
  }, []);
  useEffect(load, [load]);
  const toggle = async (flag: FlagState) => {
    try {
      setError(null);
      await sensitive(() => adminApi.setFlag(flag.key, !flag.enabled));
      setMessage(
        t(flag.enabled ? 'config.flags.off' : 'config.flags.on', {
          name: t(`config.flags.names.${flag.key}`),
        }),
      );
      load();
    } catch (e) {
      setError(e);
    }
  };

  if (!flags) {
    return <ErrorText error={error} />;
  }
  return (
    <>
      <p className={styles.muted}>{t('config.flags.intro')}</p>
      <table className={styles.table}>
        <thead>
          <tr>
            <th scope="col">{t('config.flags.feature')}</th>
            <th scope="col">{t('config.flags.state')}</th>
            <th scope="col">{t('config.flags.changed')}</th>
          </tr>
        </thead>
        <tbody>
          {flags.map((f) => (
            <tr key={f.key}>
              <th scope="row">
                {t(`config.flags.names.${f.key}`)}
                <span className={styles.hint}> {t(`config.flags.descriptions.${f.key}`)}</span>
              </th>
              <td>
                <label className={styles.check}>
                  <input
                    type="checkbox"
                    checked={f.enabled}
                    onChange={() => {
                      void toggle(f);
                    }}
                  />
                  <span className={f.enabled ? undefined : styles.attention}>
                    {t(f.enabled ? 'config.flags.enabled' : 'config.flags.disabled')}
                  </span>
                </label>
              </td>
              <td className={styles.muted}>
                {f.updatedAt
                  ? `${f.updatedBy ?? t('config.system')} · ${formatDateTime(f.updatedAt, i18n.language)}`
                  : t('config.flags.default')}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      <Notice>{message}</Notice>
      <ErrorText error={error} />
    </>
  );
}

const SECTIONS = [
  { path: '/', key: 'rules', permission: 'config.rules' },
  { path: '/storms', key: 'storms', permission: 'config.storms' },
  { path: '/seasons', key: 'seasons', permission: 'config.seasons' },
  { path: '/flags', key: 'flags', permission: 'config.flags' },
] as const;

/**
 * Rules, storms, seasons and feature flags (GAME_DESIGN §9). Every change asks for a fresh code.
 * Mounted under a nested route: paths here are relative to /config.
 */
export function Config({ session }: { session: AdminSession }) {
  const { t } = useTranslation();
  const [location] = useLocation();
  const sections = SECTIONS.filter((s) => session.permissions.includes(s.permission));
  return (
    <section aria-labelledby="config-title">
      <h1 id="config-title" className={styles.pageTitle}>
        {t('config.title')}
      </h1>
      <nav className={styles.subnav} aria-label={t('config.title')}>
        {sections.map((s) => (
          <Link key={s.key} href={s.path} aria-current={location === s.path ? 'page' : undefined}>
            {t(`config.${s.key}.title`)}
          </Link>
        ))}
      </nav>
      <Switch>
        <Route path="/storms">
          <StormsPanel />
        </Route>
        <Route path="/seasons">
          <SeasonsPanel />
        </Route>
        <Route path="/flags">
          <FlagsPanel />
        </Route>
        <Route>
          <RulesPanel />
        </Route>
      </Switch>
    </section>
  );
}

function addDays(iso: string, days: number): string {
  const date = new Date(`${iso}T00:00:00Z`);
  date.setUTCDate(date.getUTCDate() + days);
  return date.toISOString().slice(0, 10);
}

function formatDay(iso: string, locale: string): string {
  return new Intl.DateTimeFormat(locale, {
    weekday: 'long',
    day: 'numeric',
    month: 'long',
    timeZone: 'UTC',
  }).format(new Date(`${iso}T00:00:00Z`));
}
