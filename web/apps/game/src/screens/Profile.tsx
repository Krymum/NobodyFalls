import { DEFAULT_LOOK, resolveLook } from '@nobodyfalls/render';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useLocation } from 'wouter';
import { api } from '../api/client.ts';
import type {
  LifeDebt,
  Locale,
  MapNameVisibility,
  NotificationPreferences,
  PlayerRef,
} from '../api/types.ts';
import { inviteLink } from '../core/invite.ts';
import { disablePush, enablePush, pushEnabledHere, pushSupport, type PushSupport } from '../core/push.ts';
import { phaseOf } from '../core/standing.ts';
import { errorKey, game, useGame } from '../core/store.ts';
import { changeLocale, formatDay, formatNumber, LOCALES } from '../i18n/index.ts';
import { CharacterCanvas } from '../ui/CharacterCanvas.tsx';
import { Header } from '../ui/Chrome.tsx';
import ui from '../ui/ui.module.css';
import styles from './screens.module.css';

/** Profile (GAME_DESIGN §10.5): score, life debts, invitation link, settings. */
export function Profile() {
  const { t, i18n } = useTranslation();
  const profile = useGame((s) => s.profile);
  const renderKeys = useGame((s) => s.renderKeys);
  const tonight = useGame((s) => s.tonight);
  const [, navigate] = useLocation();

  useEffect(() => {
    void game.loadProfile();
  }, []);

  if (!profile) {
    return <main className={ui.page} aria-busy="true" />;
  }

  const look = profile.me.appearance ? resolveLook(profile.me.appearance, renderKeys) : DEFAULT_LOOK;
  return (
    <div>
      <Header
        night={tonight?.nightNumber ?? null}
        midnightAt={tonight?.seasonOpen ? tonight.midnightAt : null}
      />
      <main className={ui.page}>
        <div className={styles.identity}>
          <CharacterCanvas
            look={look}
            posture={profile.status === 'fallen' ? 'fallen' : 'held'}
            phase={phaseOf(profile.me.id)}
            size={150}
            animated
            label={profile.me.pseudo}
          />
          <div className={ui.section}>
            <h1 className={ui.heading}>{profile.me.pseudo}</h1>
          </div>
        </div>

        <dl className={styles.figures}>
          <div className={styles.figure}>
            <dt className={ui.subheading}>{t('profile.score')}</dt>
            <dd className={styles.number}>{formatNumber(profile.guardianScore, i18n.language)}</dd>
          </div>
          <div className={styles.figure}>
            <dt className={ui.subheading}>{t('profile.falls')}</dt>
            <dd className={styles.number}>{formatNumber(profile.fallsCount, i18n.language)}</dd>
          </div>
        </dl>

        <section className={ui.section}>
          <h2 className={ui.subheading}>{t('profile.titles')}</h2>
          <p className={ui.text}>{t('profile.titlesSoon')}</p>
        </section>

        <Debts title={t('profile.savedBy')} debts={profile.savedBy} />
        <Debts title={t('profile.saved')} debts={profile.saved} />

        <section className={ui.section}>
          <h2 className={ui.subheading}>{t('friends.invite')}</h2>
          <p className={ui.text} style={{ wordBreak: 'break-all' }}>
            {inviteLink(profile.inviteCode, i18n.language)}
          </p>
        </section>

        <section className={ui.section}>
          <h2 className={ui.heading} style={{ fontSize: 28 }}>
            {t('profile.settings')}
          </h2>

          <h3 className={ui.subheading}>{t('profile.language')}</h3>
          <div className={styles.choices}>
            {LOCALES.map((locale) => (
              <button
                key={locale}
                type="button"
                className={styles.choice}
                aria-pressed={i18n.language === locale}
                onClick={() => {
                  void switchLanguage(locale, navigate);
                }}
              >
                {locale === 'fr' ? 'Français' : 'English'}
              </button>
            ))}
          </div>

          <Notifications />

          <h3 className={ui.subheading}>{t('profile.map')}</h3>
          <div className={styles.choices}>
            {(['friends', 'everyone', 'nobody'] as const).map((value) => (
              <MapChoice key={value} value={value} current={profile.mapNameVisibility} />
            ))}
          </div>

          <Blocked />

          <h3 className={ui.subheading}>{t('profile.feedbackTitle')}</h3>
          <Link href="/profile/feedback" className={ui.secondary}>
            {t('profile.feedback')}
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
            {t('profile.logout')}
          </button>
        </section>
      </main>
    </div>
  );
}

async function switchLanguage(
  locale: Locale,
  navigate: (to: string, options?: { replace?: boolean }) => void,
): Promise<void> {
  await api.updateSettings({ locale });
  await changeLocale(locale);
  // The language is part of the URL: move to the same screen in the other language.
  navigate(`~/${locale}/profile`, { replace: true });
}

function MapChoice({ value, current }: { value: MapNameVisibility; current: MapNameVisibility }) {
  const { t } = useTranslation();
  return (
    <button
      type="button"
      className={styles.choice}
      aria-pressed={value === current}
      onClick={() => {
        void api.updateSettings({ mapNameVisibility: value }).then(() => game.loadProfile());
      }}
    >
      {t(`profile.mapValue.${value}`)}
    </button>
  );
}

function Debts({ title, debts }: { title: string; debts: readonly LifeDebt[] }) {
  const { t, i18n } = useTranslation();
  return (
    <section className={ui.section}>
      <h2 className={ui.subheading}>{title}</h2>
      {debts.length === 0 ? (
        <p className={ui.text}>{t('profile.nobody')}</p>
      ) : (
        <ul className={ui.list}>
          {debts.map((debt) => (
            <li key={`${debt.player.id}-${debt.day}`} className={ui.text}>
              <strong className={ui.name} style={{ fontSize: 15 }}>
                {debt.player.pseudo}
              </strong>{' '}
              · {formatDay(debt.day, i18n.language)}
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

const PREFERENCES: readonly (keyof NotificationPreferences)[] = [
  'fell',
  'friendFell',
  'savedMe',
  'friendBelow',
  'storm',
];

function Notifications() {
  const { t } = useTranslation();
  const [support, setSupport] = useState<PushSupport>(() => pushSupport());
  const [subscribed, setSubscribed] = useState(pushEnabledHere);
  const [preferences, setPreferences] = useState<NotificationPreferences | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    void api.notificationPreferences().then(setPreferences);
  }, []);

  const toggle = async (key: keyof NotificationPreferences): Promise<void> => {
    if (!preferences) {
      return;
    }
    const next = { ...preferences, [key]: !preferences[key] };
    setPreferences(next);
    try {
      await api.saveNotificationPreferences(next);
    } catch (e) {
      setError(errorKey(e));
      setPreferences(preferences);
    }
  };

  return (
    <>
      <h3 className={ui.subheading}>{t('profile.notifications')}</h3>
      {support === 'unsupported' && <p className={ui.text}>{t('profile.pushUnsupported')}</p>}
      {support === 'denied' && <p className={ui.text}>{t('profile.pushDenied')}</p>}
      {support === 'supported' &&
        (subscribed ? (
          <>
            <p className={ui.text}>{t('profile.pushOn')}</p>
            <button
              type="button"
              className={ui.secondary}
              onClick={() => {
                void disablePush().then(() => {
                  setSubscribed(false);
                });
              }}
            >
              {t('profile.pushOff')}
            </button>
          </>
        ) : (
          <button
            type="button"
            className={ui.primary}
            onClick={() => {
              void enablePush()
                .then((ok) => {
                  setSubscribed(ok);
                  setSupport(pushSupport());
                })
                .catch((e: unknown) => {
                  setError(errorKey(e));
                });
            }}
          >
            {t('profile.enablePush')}
          </button>
        ))}
      {preferences &&
        PREFERENCES.map((key) => (
          <label key={key} className={styles.toggle}>
            {t(`profile.pref.${key}`)}
            <input type="checkbox" checked={preferences[key]} onChange={() => void toggle(key)} />
          </label>
        ))}
      {error && (
        <p className={ui.notice} role="alert">
          {t(error)}
        </p>
      )}
    </>
  );
}

function Blocked() {
  const { t } = useTranslation();
  const [blocked, setBlocked] = useState<readonly PlayerRef[]>([]);
  useEffect(() => {
    void api.blocked().then((list) => {
      setBlocked(list.items);
    });
  }, []);
  if (blocked.length === 0) {
    return null;
  }
  return (
    <>
      <h3 className={ui.subheading}>{t('profile.blocked')}</h3>
      <ul className={ui.list}>
        {blocked.map((player) => (
          <li key={player.id} className={styles.toggle}>
            {player.pseudo}
            <button
              type="button"
              className={ui.secondary}
              onClick={() => {
                void api.unblock(player.id).then(() => {
                  setBlocked((list) => list.filter((p) => p.id !== player.id));
                });
              }}
            >
              {t('profile.unblock')}
            </button>
          </li>
        ))}
      </ul>
    </>
  );
}
