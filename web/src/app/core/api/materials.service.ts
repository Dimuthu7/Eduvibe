import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { catchError, from, map, Observable, switchMap, throwError } from 'rxjs';
import { AppConfig } from '../config/app-config';
import { quiet } from './quiet';

export const MATERIAL_KINDS = ['Notes', 'PastPaper', 'Other'] as const;
export type MaterialKind = (typeof MATERIAL_KINDS)[number];

/** The biggest file the server takes, and the types it accepts (what a phone can open). The server checks again. */
export const MATERIAL_MAX_BYTES = 25 * 1024 * 1024;
export const MATERIAL_EXTENSIONS = ['pdf', 'doc', 'docx', 'ppt', 'pptx', 'xls', 'xlsx', 'txt', 'jpg', 'jpeg', 'png', 'webp'] as const;
export const MATERIAL_ACCEPT = MATERIAL_EXTENSIONS.map((e) => `.${e}`).join(',');

/** Configured: the server has a Google client. Connected: this teacher linked their Drive. NeedsReconnect: Google stopped accepting the link. */
export interface DriveStatus {
  configured: boolean;
  connected: boolean;
  email: string | null;
  needsReconnect: boolean;
}

/** A teacher's folder on one shelf (Notes, Past paper or Other) of a class. parentId is null for a folder directly on the shelf. */
export interface MaterialFolder {
  id: string;
  classId: string;
  kind: MaterialKind;
  parentId: string | null;
  name: string;
}

/** Folders can be nested this deep; the server checks again. */
export const FOLDER_MAX_DEPTH = 4;
export const FOLDER_MAX_NAME = 60;

export interface Material {
  id: string;
  classId: string;
  /** The folder the file is in, or null when it is directly on its shelf. */
  folderId: string | null;
  title: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  kind: MaterialKind;
  topic: string | null;
  year: number | null;
  addedAt: string;
  /** Types a browser can show in place; the rest are downloaded. */
  canPreview: boolean;
}

export interface StudentMaterial {
  material: Material;
  classTitle: string;
  teacherName: string | null;
  /** Names of the folders the file is in, outermost first; empty when it is directly on its shelf. */
  folderPath: string[];
}

export interface MaterialDetails {
  title: string;
  kind: MaterialKind;
  topic: string | null;
  year: number | null;
  folderId: string | null;
}

/** A teacher's Google Drive link and class files, and the student's read-only view. Files are streamed through the API; Drive is never shared. */
@Injectable({ providedIn: 'root' })
export class MaterialsService {
  private readonly http = inject(HttpClient);
  private readonly base = `${inject(AppConfig).apiBaseUrl}/api/materials`;

  /** Quiet: a teacher without the feature gets a "locked" answer the page shows itself, not a toast. */
  driveStatus() {
    return this.http.get<DriveStatus>(`${this.base}/drive`, { context: quiet() });
  }

  /** The Google page where the teacher agrees; the browser goes there. */
  connect() {
    return this.http.post<{ url: string }>(`${this.base}/drive/connect`, {});
  }

  disconnect() {
    return this.http.delete<void>(`${this.base}/drive`);
  }

  list(classId?: string) {
    return this.http.get<Material[]>(this.base, { params: classId ? { classId } : {} });
  }

  add(form: FormData) {
    return this.http.post<Material>(this.base, form);
  }

  update(id: string, details: MaterialDetails) {
    return this.http.put<Material>(`${this.base}/${id}`, details);
  }

  remove(id: string) {
    return this.http.delete<void>(`${this.base}/${id}`);
  }

  folders() {
    return this.http.get<MaterialFolder[]>(`${this.base}/folders`);
  }

  createFolder(folder: { classId: string; kind: MaterialKind; parentId: string | null; name: string }) {
    return this.http.post<MaterialFolder>(`${this.base}/folders`, folder);
  }

  renameFolder(id: string, name: string) {
    return this.http.put<MaterialFolder>(`${this.base}/folders/${id}`, { name });
  }

  /** Moves a folder, with what is in it, into another folder (parentId) or onto a shelf (kind, with parentId null). */
  moveFolder(id: string, to: { kind: MaterialKind; parentId: string | null }) {
    return this.http.put<MaterialFolder>(`${this.base}/folders/${id}/move`, to);
  }

  removeFolder(id: string) {
    return this.http.delete<void>(`${this.base}/folders/${id}`);
  }

  file(id: string, download: boolean) {
    return this.blob(`${this.base}/${id}/file`, download);
  }

  mine() {
    return this.http.get<StudentMaterial[]>(`${this.base}/mine`);
  }

  /** Only to tell the welcome page whether there is anything to link to: a failure there must not get in the way. */
  mineQuietly() {
    return this.http.get<StudentMaterial[]>(`${this.base}/mine`, { context: quiet() });
  }

  myFile(id: string, download: boolean) {
    return this.blob(`${this.base}/mine/${id}/file`, download);
  }

  /**
   * A file's bytes. Quiet, because the screen shows a message of its own; and a failure arrives with its error code readable,
   * since a failed download comes back as a blob that Angular would not parse.
   */
  private blob(url: string, download: boolean): Observable<Blob> {
    return this.http.get(url, { params: download ? { download: true } : {}, responseType: 'blob', context: quiet() }).pipe(
      catchError((error: unknown) =>
        error instanceof HttpErrorResponse && error.error instanceof Blob
          ? from(error.error.text()).pipe(
              map((text) => {
                try {
                  return JSON.parse(text) as unknown;
                } catch {
                  return null;
                }
              }),
              switchMap((body) => throwError(() => new HttpErrorResponse({ status: error.status, statusText: error.statusText, url: error.url ?? undefined, error: body }))),
            )
          : throwError(() => error),
      ),
    );
  }
}
