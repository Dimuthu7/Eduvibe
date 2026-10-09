import { inject, Injectable, provideAppInitializer } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

export interface AppConfigValues {
  /** Base address of the EduVibe API, without a trailing slash. */
  apiBaseUrl: string;
}

/** Settings that change per environment, read from /config.json at start-up so one build runs anywhere. */
@Injectable({ providedIn: 'root' })
export class AppConfig {
  private values: AppConfigValues = { apiBaseUrl: '' };

  get apiBaseUrl(): string {
    return this.values.apiBaseUrl.replace(/\/$/, '');
  }

  async load(http: HttpClient): Promise<void> {
    this.values = await firstValueFrom(http.get<AppConfigValues>('config.json'));
  }
}

export const provideAppConfig = () =>
  provideAppInitializer(() => inject(AppConfig).load(inject(HttpClient)));
