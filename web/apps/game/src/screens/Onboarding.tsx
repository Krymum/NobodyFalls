import { resolveLook, type Appearance } from '@nobodyfalls/render';
import { useEffect, useMemo, useState, type SyntheticEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { useLocation } from 'wouter';
import { api } from '../api/client.ts';
import { humanToken } from '../api/turnstile.ts';
import type { Catalog, InvitePreview, Locale, Slot } from '../api/types.ts';
import { forgetInvite, pendingInvite } from '../core/invite.ts';
import { enablePush, pushSupport } from '../core/push.ts';
import { canPromptInstall, isInstalled, isIos, promptInstall } from '../core/pwa.ts';
import { errorKey, game, useGame } from '../core/store.ts';
import {
  defaultAppearance,
  EDITABLE_SLOTS,
  isRequired,
  itemsFor,
  paint,
  wear,
  withSkinTone,
} from '../core/wardrobe.ts';
import { CharacterCanvas } from '../ui/CharacterCanvas.tsx';
import ui from '../ui/ui.module.css';
import styles from './screens.module.css';

type Step =
  { kind: 'rule'; index: number } | { kind: 'signIn' } | { kind: 'character' } | { kind: 'install' };

const RULES = 3;

/**
 * Onboarding (GAME_DESIGN §10.1): the rule in three screens, sign-in, the character,
 * then installing the PWA (needed for push on iOS). Signing in leaves the app for
 * the provider and comes back here, at the character step.
 */
export function Onboarding({ afterRegistration = false }: { afterRegistration?: boolean }) {
  const session = useGame((s) => s.session);
  const [step, setStep] = useState<Step>(() =>
    afterRegistration
      ? { kind: 'install' }
      : session?.state === 'pendingRegistration'
        ? { kind: 'character' }
        : { kind: 'rule', index: 0 },
  );

  return (
    <main className={styles.onboarding}>
      {step.kind === 'rule' && (
        <Rule
          index={step.index}
          onNext={() => {
            setStep(step.index + 1 < RULES ? { kind: 'rule', index: step.index + 1 } : { kind: 'signIn' });
          }}
          onSkip={() => {
            setStep({ kind: 'signIn' });
          }}
        />
      )}
      {step.kind === 'signIn' && <SignIn />}
      {step.kind === 'character' && (
        <Character
          onDone={() => {
            setStep({ kind: 'install' });
          }}
        />
      )}
      {step.kind === 'install' && <Install />}
    </main>
  );
}

function Rule({ index, onNext, onSkip }: { index: number; onNext: () => void; onSkip: () => void }) {
  const { t } = useTranslation();
  return (
    <>
      <div
        className={styles.dots}
        role="img"
        aria-label={t('onboarding.step', { n: index + 1, total: RULES })}
      >
        {Array.from({ length: RULES }, (_, i) => (
          <span key={i} className={styles.dot} aria-current={i === index ? 'step' : undefined} />
        ))}
      </div>
      <h1 className={ui.heading} style={{ fontSize: 48 }}>
        {t(`onboarding.rules.${index}.title`)}
      </h1>
      <p className={ui.text}>{t(`onboarding.rules.${index}.body`)}</p>
      <button type="button" className={ui.primary} onClick={onNext}>
        {t('onboarding.next')}
      </button>
      {index < RULES - 1 && (
        <button type="button" className={ui.secondary} onClick={onSkip}>
          {t('onboarding.skip')}
        </button>
      )}
    </>
  );
}

function SignIn() {
  const { t, i18n } = useTranslation();
  const session = useGame((s) => s.session);
  const [devName, setDevName] = useState('');
  const back = encodeURIComponent(`/${i18n.language}/onboarding`);
  const providers = (session?.providers ?? []).filter((p) => p !== 'dev');

  const devLogin = (event: SyntheticEvent): void => {
    event.preventDefault();
    if (devName.trim()) {
      window.location.assign(`/auth/login/dev?name=${encodeURIComponent(devName.trim())}&returnUrl=${back}`);
    }
  };

  return (
    <>
      <h1 className={ui.heading}>{t('onboarding.signInTitle')}</h1>
      <p className={ui.text}>{t('onboarding.signInBody')}</p>
      {providers.map((provider) => (
        <a
          key={provider}
          className={ui.primary}
          style={{ display: 'grid', placeItems: 'center', textDecoration: 'none' }}
          href={`/auth/login/${provider}?returnUrl=${back}`}
        >
          {t(`onboarding.${provider}`)}
        </a>
      ))}
      {session?.devTools && (
        <form className={ui.field} onSubmit={devLogin}>
          <input
            className={ui.input}
            value={devName}
            aria-label={t('onboarding.devName')}
            placeholder={t('onboarding.devName')}
            onChange={(e) => {
              setDevName(e.target.value);
            }}
          />
          <button type="submit" className={ui.secondary}>
            {t('onboarding.devLogin')}
          </button>
        </form>
      )}
      {providers.length === 0 && !session?.devTools && (
        <p className={ui.text}>{t('onboarding.noProvider')}</p>
      )}
    </>
  );
}

function Character({ onDone }: { onDone: () => void }) {
  const { t, i18n } = useTranslation();
  const session = useGame((s) => s.session);
  const [catalog, setCatalog] = useState<Catalog | null>(null);
  const [appearance, setAppearance] = useState<Appearance | null>(null);
  const [pseudo, setPseudo] = useState('');
  const [invite, setInvite] = useState<InvitePreview | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    void api.catalog().then((c) => {
      setCatalog(c);
      setAppearance(defaultAppearance(c));
    });
    const code = pendingInvite();
    if (code) {
      api
        .invite(code)
        .then(setInvite)
        .catch(() => undefined);
    }
  }, []);

  const keys = useMemo(() => new Map(catalog?.items.map((i) => [i.id, i.renderKey]) ?? []), [catalog]);
  if (!catalog || !appearance) {
    return <p className={ui.text}>{t('common.loading')}</p>;
  }

  const submit = async (event: SyntheticEvent): Promise<void> => {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await api.register({
        pseudo: pseudo.trim(),
        appearance,
        locale: i18n.language as Locale,
        inviteCode: invite?.code ?? null,
        turnstileToken: await humanToken(session?.turnstileSiteKey ?? ''),
      });
      forgetInvite();
      await game.loadSession();
      onDone();
    } catch (e) {
      setError(errorKey(e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <form className={ui.section} onSubmit={(e) => void submit(e)}>
      <h1 className={ui.heading}>{t('onboarding.characterTitle')}</h1>
      {invite && <p className={ui.text}>{t('onboarding.invitedBy', { name: invite.inviter.pseudo })}</p>}
      <div className={styles.preview}>
        <CharacterCanvas
          look={resolveLook(appearance, keys)}
          posture="held"
          size={190}
          animated
          label={pseudo || t('onboarding.characterTitle')}
        />
      </div>

      <label htmlFor="pseudo" className={ui.subheading}>
        {t('onboarding.pseudo')}
      </label>
      <input
        id="pseudo"
        className={ui.input}
        value={pseudo}
        required
        minLength={3}
        maxLength={20}
        autoComplete="nickname"
        autoCapitalize="none"
        aria-describedby="pseudo-hint"
        onChange={(e) => {
          setPseudo(e.target.value);
        }}
      />
      <p id="pseudo-hint" className={ui.text}>
        {t('onboarding.pseudoHint')}
      </p>

      <fieldset className={ui.section} style={{ border: 0, padding: 0, margin: 0 }}>
        <legend className={ui.subheading}>{t('onboarding.slot.skinTone')}</legend>
        <div className={styles.choices}>
          {catalog.skinTones.map((tone, index) => (
            <button
              key={tone}
              type="button"
              className={styles.swatch}
              style={{ background: tone }}
              aria-pressed={appearance.skinTone === tone}
              aria-label={t('onboarding.swatch', { n: index + 1 })}
              onClick={() => {
                setAppearance(withSkinTone(appearance, catalog, tone));
              }}
            />
          ))}
        </div>
      </fieldset>

      {EDITABLE_SLOTS.map((slot) => (
        <SlotPicker
          key={slot}
          slot={slot}
          catalog={catalog}
          appearance={appearance}
          onChange={setAppearance}
        />
      ))}

      {error && (
        <p className={ui.notice} role="alert">
          {t(error)}
        </p>
      )}
      <button type="submit" className={ui.primary} disabled={busy}>
        {t('onboarding.create')}
      </button>
    </form>
  );
}

function SlotPicker({
  slot,
  catalog,
  appearance,
  onChange,
}: {
  slot: Slot;
  catalog: Catalog;
  appearance: Appearance;
  onChange: (appearance: Appearance) => void;
}) {
  const { t } = useTranslation();
  const items = itemsFor(catalog, slot);
  const current = appearance.slots[slot];
  const item = items.find((i) => i.id === current?.itemId);
  return (
    <fieldset className={ui.section} style={{ border: 0, padding: 0, margin: 0 }}>
      <legend className={ui.subheading}>{t(`onboarding.slot.${slot}`)}</legend>
      <div className={styles.choices}>
        {!isRequired(slot) && (
          <button
            type="button"
            className={styles.choice}
            aria-pressed={!current}
            onClick={() => {
              onChange(wear(appearance, catalog, slot, null));
            }}
          >
            {t('onboarding.none')}
          </button>
        )}
        {items.map((i) => (
          <button
            key={i.id}
            type="button"
            className={styles.choice}
            aria-pressed={current?.itemId === i.id}
            onClick={() => {
              onChange(wear(appearance, catalog, slot, i.id));
            }}
          >
            {t(`wardrobe.${i.renderKey}`)}
          </button>
        ))}
      </div>
      {item && item.colors.length > 1 && (
        <div className={styles.choices} aria-label={t('onboarding.colour')} role="group">
          {item.colors.map((color, index) => (
            <button
              key={color}
              type="button"
              className={styles.swatch}
              style={{ background: color }}
              aria-pressed={current?.color === color}
              aria-label={t('onboarding.swatch', { n: index + 1 })}
              onClick={() => {
                onChange(paint(appearance, catalog, slot, color));
              }}
            />
          ))}
        </div>
      )}
    </fieldset>
  );
}

function Install() {
  const { t } = useTranslation();
  const [, navigate] = useLocation();
  const finish = (): void => {
    navigate('/', { replace: true });
  };

  return (
    <>
      <h1 className={ui.heading}>{t('onboarding.installTitle')}</h1>
      <p className={ui.text}>{t('onboarding.installBody')}</p>
      {!isInstalled() && isIos() && <p className={ui.text}>{t('onboarding.iosHint')}</p>}
      {!isInstalled() && canPromptInstall() && (
        <button type="button" className={ui.primary} onClick={() => void promptInstall()}>
          {t('onboarding.install')}
        </button>
      )}
      {pushSupport() === 'supported' && (
        <button
          type="button"
          className={ui.primary}
          onClick={() => {
            void enablePush().finally(finish);
          }}
        >
          {t('profile.enablePush')}
        </button>
      )}
      <button type="button" className={ui.secondary} onClick={finish}>
        {pushSupport() === 'supported' ? t('onboarding.later') : t('onboarding.done')}
      </button>
    </>
  );
}
