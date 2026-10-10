import { HttpClient } from '@angular/common/http';
import { computed, inject, Injectable, signal } from '@angular/core';
import { forkJoin } from 'rxjs';
import type { SelectOption } from '../../shared/search-select';
import { AppConfig } from '../config/app-config';

/** A choice an admin can extend later, such as an education stream or a subject. */
export interface CatalogItem {
  id: string;
  name: string;
  sortOrder: number;
  isActive: boolean;
}

export type CatalogKind = 'streams' | 'subjects';

/** Lists the screens pick from: districts (fixed), education streams and subjects (kept in the database). */
@Injectable({ providedIn: 'root' })
export class CatalogService {
  private readonly http = inject(HttpClient);
  private readonly base = inject(AppConfig).apiBaseUrl;

  districts() {
    return this.http.get<string[]>(`${this.base}/api/system/districts`);
  }

  items(kind: CatalogKind, includeInactive = false) {
    return this.http.get<CatalogItem[]>(`${this.base}/api/catalog/${kind}`, {
      params: includeInactive ? { includeInactive: true } : {},
    });
  }

  add(kind: CatalogKind, name: string) {
    return this.http.post<CatalogItem>(`${this.base}/api/catalog/${kind}`, { name });
  }

  update(kind: CatalogKind, id: string, change: { name?: string; isActive?: boolean }) {
    return this.http.put<CatalogItem>(`${this.base}/api/catalog/${kind}/${id}`, change);
  }
}

/**
 * The lists in use, loaded once and shared by every form and list. Teachers and institutes show names
 * from here, so a stream or subject added in the catalog appears everywhere after a reload.
 */
@Injectable({ providedIn: 'root' })
export class CatalogStore {
  private readonly api = inject(CatalogService);

  readonly districts = signal<string[]>([]);
  /** Every stream and subject, including hidden ones, so old records still show their names. */
  readonly allStreams = signal<CatalogItem[]>([]);
  readonly allSubjects = signal<CatalogItem[]>([]);
  readonly streams = computed(() => this.allStreams().filter((i) => i.isActive));
  readonly subjects = computed(() => this.allSubjects().filter((i) => i.isActive));
  readonly districtOptions = computed<SelectOption[]>(() => this.districts().map((d) => ({ value: d, label: d })));
  readonly streamOptions = computed<SelectOption[]>(() => this.streams().map((i) => ({ value: i.id, label: i.name })));
  readonly subjectOptions = computed<SelectOption[]>(() => this.subjects().map((i) => ({ value: i.id, label: i.name })));
  readonly loaded = signal(false);

  private started = false;

  /** Loads the lists the first time it is called. Pass force to refresh after a change. */
  ensureLoaded(force = false): void {
    if (this.started && !force) return;
    this.started = true;
    forkJoin({
      districts: this.api.districts(),
      streams: this.api.items('streams', true),
      subjects: this.api.items('subjects', true),
    }).subscribe({
      next: ({ districts, streams, subjects }) => {
        this.districts.set(districts);
        this.allStreams.set(streams);
        this.allSubjects.set(subjects);
        this.loaded.set(true);
      },
      error: () => (this.started = false),
    });
  }

  streamName(id: string | null): string {
    return this.allStreams().find((s) => s.id === id)?.name ?? '';
  }

  subjectNames(ids: readonly string[]): string[] {
    const all = this.allSubjects();
    return ids.map((id) => all.find((s) => s.id === id)?.name).filter((n): n is string => !!n);
  }
}
