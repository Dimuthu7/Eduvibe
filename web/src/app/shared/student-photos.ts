import { computed, effect, inject, Injectable, signal, untracked } from '@angular/core';
import { StudentPhotoEntry, StudentsService } from '../core/api/students.service';
import { AuthService } from '../core/auth/auth.service';

const MAX_AT_ONCE = 6;

/**
 * Which students have a profile photo, and the pictures themselves. One small list says who has a photo; a picture is fetched only
 * for an avatar that is on screen, once per version, and kept as a blob URL for the rest of the session (the browser also keeps the
 * file for a week, because the version is part of the address). Students without a photo cost nothing. Everything is dropped when
 * a different person signs in.
 */
@Injectable({ providedIn: 'root' })
export class StudentPhotos {
  private readonly api = inject(StudentsService);
  private readonly auth = inject(AuthService);
  private readonly userId = computed(() => this.auth.user()?.id ?? null);

  // Keyed by student id and by each of the teacher's enrolment ids, so lists of attendance, fees and marks can use their own ids.
  private readonly index = signal<ReadonlyMap<string, StudentPhotoEntry>>(new Map());
  private readonly urls = signal<ReadonlyMap<string, string>>(new Map());
  private readonly asked = new Set<string>();
  private readonly waiting: StudentPhotoEntry[] = [];
  private active = 0;
  private indexRequested = false;
  private seenUser: string | null | undefined;

  /** The signed-in student's own photo, for their profile page. */
  readonly mine = signal<string | null>(null);

  constructor() {
    // Another person signing in must not see the last one's pictures. The first run only notes who is here.
    effect(() => {
      const id = this.userId();
      untracked(() => {
        if (this.seenUser !== undefined && id !== this.seenUser) this.reset();
        this.seenUser = id;
      });
    });
  }

  /** The photo entry for a student id or an enrolment id, or undefined when there is none (or the list has not arrived yet). */
  entryOf(key: string | null | undefined): StudentPhotoEntry | undefined {
    return key ? this.index().get(key) : undefined;
  }

  /** The picture to show for a student id or an enrolment id, or null. Asking for it does not fetch it: call `want` for that. */
  urlOf(key: string | null | undefined): string | null {
    const entry = this.entryOf(key);
    return entry ? (this.urls().get(this.cacheKey(entry)) ?? null) : null;
  }

  /** Loads the list of who has a photo, the first time it is needed. */
  ensureIndex(): void {
    if (!this.indexRequested) this.refresh();
  }

  /** Loads the list again, for a screen that wants the latest (the students list, a student's page). */
  refresh(): void {
    this.indexRequested = true;
    const user = this.userId();
    this.api.photos().subscribe({
      next: (rows) => {
        if (user === this.userId()) this.setIndex(rows);
      },
      // Initials keep showing. The next refresh from a screen tries again.
      error: () => undefined,
    });
  }

  /** Fetches the picture for this key unless it is already here or on its way. */
  want(entry: StudentPhotoEntry | undefined): void {
    if (!entry) return;
    const key = this.cacheKey(entry);
    if (this.urls().has(key) || this.asked.has(key)) return;
    this.asked.add(key);
    this.waiting.push(entry);
    this.pump();
  }

  /** Saves a new photo for a student the teacher teaches, and shows it straight away from the bytes just sent. */
  upload(studentId: string, image: Blob) {
    const user = this.userId();
    return new Promise<void>((resolve, reject) => {
      this.api.setPhoto(studentId, image).subscribe({
        next: (entry) => {
          if (user === this.userId()) {
            this.put(entry, URL.createObjectURL(image));
          }
          resolve();
        },
        error: reject,
      });
    });
  }

  remove(studentId: string) {
    return new Promise<void>((resolve, reject) => {
      this.api.removePhoto(studentId).subscribe({
        next: () => {
          this.drop(studentId);
          resolve();
        },
        error: reject,
      });
    });
  }

  /** Loads the signed-in student's own photo. Quiet: no photo is a normal answer. */
  loadMine(): void {
    const user = this.userId();
    this.api.myPhoto().subscribe({
      next: (blob) => {
        if (user !== this.userId()) return;
        const old = this.mine();
        this.mine.set(blob && blob.size > 0 ? URL.createObjectURL(blob) : null);
        if (old) URL.revokeObjectURL(old);
      },
      error: () => undefined,
    });
  }

  private cacheKey(entry: StudentPhotoEntry): string {
    return `${entry.studentId}:${entry.version}`;
  }

  private pump(): void {
    while (this.active < MAX_AT_ONCE && this.waiting.length > 0) {
      const entry = this.waiting.shift()!;
      const user = this.userId();
      this.active++;
      this.api.photo(entry.studentId, entry.version).subscribe({
        next: (blob) => {
          this.active--;
          if (user === this.userId() && blob && blob.size > 0) this.store(entry, URL.createObjectURL(blob));
          this.pump();
        },
        error: () => {
          this.active--;
          this.pump();
        },
      });
    }
  }

  private setIndex(rows: StudentPhotoEntry[]): void {
    const next = new Map<string, StudentPhotoEntry>();
    for (const row of rows) {
      next.set(row.studentId, row);
      for (const id of row.enrolmentIds) next.set(id, row);
    }
    // Pictures of versions that are gone are released; the ones still current stay.
    const current = new Set(rows.map((r) => this.cacheKey(r)));
    const urls = new Map(this.urls());
    for (const [key, url] of urls) {
      if (!current.has(key)) {
        URL.revokeObjectURL(url);
        urls.delete(key);
      }
    }
    this.urls.set(urls);
    this.index.set(next);
  }

  private put(entry: StudentPhotoEntry, url: string): void {
    const rows = new Map<string, StudentPhotoEntry>();
    for (const row of this.index().values()) if (row.studentId !== entry.studentId) rows.set(row.studentId, row);
    rows.set(entry.studentId, entry);
    this.setIndex([...rows.values()]);
    this.store(entry, url);
  }

  private store(entry: StudentPhotoEntry, url: string): void {
    const urls = new Map(this.urls());
    const key = this.cacheKey(entry);
    const old = urls.get(key);
    urls.set(key, url);
    this.urls.set(urls);
    if (old && old !== url) URL.revokeObjectURL(old);
  }

  private drop(studentId: string): void {
    this.setIndex([...new Set(this.index().values())].filter((row) => row.studentId !== studentId));
  }

  private reset(): void {
    for (const url of this.urls().values()) URL.revokeObjectURL(url);
    const mine = this.mine();
    if (mine) URL.revokeObjectURL(mine);
    this.mine.set(null);
    this.urls.set(new Map());
    this.index.set(new Map());
    this.asked.clear();
    this.waiting.length = 0;
    this.indexRequested = false;
  }
}
