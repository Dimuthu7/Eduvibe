import { signal } from '@angular/core';

/** The languages the app has text for. To add one: its file in public/i18n, an entry here, and the server's list of languages. */
export type Language = 'en' | 'si' | 'ta';

/** What the language menu lists. Each name is written in its own language so a person can find theirs whatever the app is showing. */
export const LANGUAGES: readonly { code: Language; name: string }[] = [
  { code: 'en', name: 'English' },
  { code: 'si', name: 'සිංහල' },
  { code: 'ta', name: 'தமிழ்' },
];

/** A language code from storage or the server, or English when it is not one the app has. */
export const asLanguage = (code: string | null | undefined): Language => (LANGUAGES.some((l) => l.code === code) ? (code as Language) : 'en');

/**
 * The language on screen. It lives outside the translate service so plain functions (dates, money, times) can read it: a template or a computed
 * value that calls them is then redrawn when the language changes.
 */
export const currentLanguage = signal<Language>('en');

/** True when this browser knows Sinhala dates, times and money. Phones do; a browser without it shows English-style dates instead of wrong ones. */
export const knowsSinhala = Intl.DateTimeFormat.supportedLocalesOf(['si-LK']).length > 0;

/** True when this browser knows Tamil dates, times and money as written in Sri Lanka. */
export const knowsTamil = Intl.DateTimeFormat.supportedLocalesOf(['ta-LK']).length > 0;

/** The browser locale that writes dates, times and money in this language, or null when it is English or the browser does not have it. */
export const nativeLocale = (language: Language): 'si-LK' | 'ta-LK' | null =>
  language === 'si' && knowsSinhala ? 'si-LK' : language === 'ta' && knowsTamil ? 'ta-LK' : null;

/** The locale that spells dates and money the way people read them in each language. */
export const localeOf = (language: Language): string => nativeLocale(language) ?? 'en-GB';
