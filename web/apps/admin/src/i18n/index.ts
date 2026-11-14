import i18next from 'i18next';
import { initReactI18next } from 'react-i18next';
import type { Locale } from '../api.ts';
import en from './en.json';
import fr from './fr.json';

export const LOCALES: readonly Locale[] = ['fr', 'en'];

/** Same rule as the game (ADR 0013): the remembered choice (`nf-lang`), then the browser, then French. */
export function detectLocale(): Locale {
  const remembered = /(?:^|; )nf-lang=(fr|en)/.exec(document.cookie)?.[1];
  if (remembered === 'fr' || remembered === 'en') {
    return remembered;
  }
  return navigator.languages.some((l) => l.startsWith('en')) && !navigator.languages[0]?.startsWith('fr')
    ? 'en'
    : 'fr';
}

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
  document.cookie = `nf-lang=${locale}; path=/; max-age=31536000; samesite=lax`;
  document.documentElement.lang = locale;
}

export function formatDateTime(iso: string, locale: string): string {
  return new Intl.DateTimeFormat(locale, {
    dateStyle: 'medium',
    timeStyle: 'medium',
    timeZone: 'UTC',
  }).format(new Date(iso));
}
