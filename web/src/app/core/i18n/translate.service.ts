import { HttpClient } from '@angular/common/http';
import { inject, Injectable, provideAppInitializer, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { fill } from './fill';
import { asLanguage, currentLanguage, Language } from './language';

export type { Language } from './language';

const STORAGE_KEY = 'gurubook.language';

/**
 * Looks up UI text by key from public/i18n/{language}.json. Every string in the app goes through
 * here, so a new language is added by dropping in a translation file and listing it in language.ts.
 * Only English is loaded at start; another language's file is fetched when it is first used.
 * Missing keys fall back to English, then to the key itself.
 */
@Injectable({ providedIn: 'root' })
export class TranslateService {
  private readonly http = inject(HttpClient);
  private fallback: Record<string, string> = {};
  private readonly files = new Map<Language, Record<string, string>>();
  private latest = 0;
  private pickedBeforeSignIn: Language | null = null;

  readonly language = currentLanguage;
  readonly messages = signal<Record<string, string>>({});

  async load(): Promise<void> {
    this.fallback = await this.fetch('en');
    await this.use(this.stored() ?? 'en', false);
  }

  /**
   * Shows the app in a language. Remembered on this device unless told not to, which is how the app applies someone's own account
   * language without it becoming the device's choice.
   */
  async use(language: Language, remember = true): Promise<void> {
    const turn = ++this.latest;
    const messages = language === 'en' ? this.fallback : await this.fetch(language);
    // Two quick taps: the last one wins, however long each file takes.
    if (turn !== this.latest) return;
    this.messages.set(messages);
    this.language.set(language);
    document.documentElement.lang = language;
    if (remember) this.remember(language);
  }

  /** What a person picked on a screen they were not signed in on (the sign-in page, a sign-up link). It goes to their account when they sign in. */
  pickBeforeSignIn(language: Language): Promise<void> {
    this.pickedBeforeSignIn = language;
    return this.use(language);
  }

  /** Takes what was picked before signing in, once. */
  takePickedBeforeSignIn(): Language | null {
    const picked = this.pickedBeforeSignIn;
    this.pickedBeforeSignIn = null;
    return picked;
  }

  translate(key: string): string {
    return this.messages()[key] ?? this.fallback[key] ?? key;
  }

  /** Text with its {values} filled in, for example "{count} students". */
  format(key: string, values: Record<string, string | number>): string {
    return fill(this.translate(key), values);
  }

  private async fetch(language: Language): Promise<Record<string, string>> {
    const known = this.files.get(language);
    if (known) return known;
    const messages = await firstValueFrom(this.http.get<Record<string, string>>(`i18n/${language}.json`));
    this.files.set(language, messages);
    return messages;
  }

  private remember(language: Language): void {
    try {
      localStorage.setItem(STORAGE_KEY, language);
    } catch {
      // Private browsing can block storage; the language just won't be remembered.
    }
  }

  private stored(): Language | null {
    try {
      const value = localStorage.getItem(STORAGE_KEY);
      return value === null ? null : asLanguage(value);
    } catch {
      return null;
    }
  }
}

export const provideTranslations = () =>
  provideAppInitializer(() => inject(TranslateService).load());
