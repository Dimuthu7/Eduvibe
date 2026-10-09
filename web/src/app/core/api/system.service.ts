import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { AppConfig } from '../config/app-config';

export interface SystemInfo {
  name: string;
  version: string;
  environment: string;
  modules: string[];
}

@Injectable({ providedIn: 'root' })
export class SystemService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(AppConfig);

  info(): Observable<SystemInfo> {
    return this.http.get<SystemInfo>(`${this.config.apiBaseUrl}/api/system/info`);
  }
}
