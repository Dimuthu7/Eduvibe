import { HttpClient } from '@angular/common/http';
import { inject, Injectable, provideAppInitializer, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export type Language = 'en' | 'si' | 'ta';

const STORAGE_KEY = 'eduvibe.language';

/**
 * Looks up UI text by key from public/i18n/{language}.json. Every string in the app goes through
 * here, so Sinhala and Tamil are added by dropping in translation files, with no code changes.
 * Missing keys fall back to English, then to the key itself.
 */
@Injectable({ providedIn: 'root' })
export class TranslateService {
  private readonly http = inject(HttpClient);
  private fallback: Record<string, string> = {};

  readonly language = signal<Language>('en');
  readonly messages = signal<Record<string, string>>({});

  async load(): Promise<void> {
    this.fallback = await this.fetch('en');
    await this.use(this.stored() ?? 'en');
  }

  async use(language: Language): Promise<void> {
    this.messages.set(language === 'en' ? this.fallback : await this.fetch(language));
    this.language.set(language);
    try {
      localStorage.setItem(STORAGE_KEY, language);
    } catch {
      // Private browsing can block storage; the language just won't be remembered.
    }
  }

  translate(key: string): string {
    return this.messages()[key] ?? this.fallback[key] ?? key;
  }

  private fetch(language: Language): Promise<Record<string, string>> {
    return firstValueFrom(this.http.get<Record<string, string>>(`i18n/${language}.json`));
  }

  private stored(): Language | null {
    try {
      const value = localStorage.getItem(STORAGE_KEY);
      return value === 'si' || value === 'ta' || value === 'en' ? value : null;
    } catch {
      return null;
    }
  }
}

export const provideTranslations = () =>
  provideAppInitializer(() => inject(TranslateService).load());
