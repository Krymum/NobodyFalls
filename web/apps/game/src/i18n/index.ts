import i18next from 'i18next';
import { initReactI18next } from 'react-i18next';
import type { Locale } from '../api/types.ts';
import en from './en.json';
import fr from './fr.json';

export const LOCALES: readonly Locale[] = ['fr', 'en'];
const LANGUAGE_COOKIE = 'nf-lang';

export function isLocale(value: string | undefined): value is Locale {
  return value === 'fr' || value === 'en';
}

/**
 * The language comes from the URL (`/fr`, `/en`, GAME_DESIGN §11); `/` picks the
 * remembered one, then the browser's (the server does the same).
 */
export function detectLocale(): Locale {
  const remembered = /(?:^|; )nf-lang=(fr|en)/.exec(document.cookie)?.[1];
  if (isLocale(remembered)) {
    return remembered;
  }
  return navigator.languages.some((l) => l.startsWith('en')) && !navigator.languages[0]?.startsWith('fr')
    ? 'en'
    : 'fr';
}

export function rememberLocale(locale: Locale): void {
  document.cookie = `${LANGUAGE_COOKIE}=${locale}; path=/; max-age=31536000; samesite=lax`;
  document.documentElement.lang = locale;
}

/** Both catalogues are small and bundled: switching language never waits on the network. */
export async function initI18n(locale: Locale): Promise<void> {
  await i18next.use(initReactI18next).init({
    resources: { fr: { translation: fr }, en: { translation: en } },
    lng: locale,
    fallbackLng: 'fr',
    interpolation: { escapeValue: false },
    returnNull: false,
  });
  document.documentElement.lang = locale;
}

export async function changeLocale(locale: Locale): Promise<void> {
  await i18next.changeLanguage(locale);
  rememberLocale(locale);
}

/** Numbers and dates go through Intl, in the current language. */
export function formatNumber(value: number, locale: string): string {
  return new Intl.NumberFormat(locale).format(value);
}

export function formatDay(isoDay: string, locale: string): string {
  return new Intl.DateTimeFormat(locale, { day: 'numeric', month: 'long', timeZone: 'UTC' }).format(
    new Date(`${isoDay}T00:00:00Z`),
  );
}
