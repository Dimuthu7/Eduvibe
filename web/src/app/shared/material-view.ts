import { Observable } from 'rxjs';
import { saveBlob } from './download';

/** A readable file size: "340 KB", "2.4 MB". */
export function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${Math.max(1, Math.round(bytes / 1024))} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

/** The icon for a file: by its type, so a teacher can tell a paper from a picture at a glance. */
export function materialIcon(contentType: string): string {
  if (contentType === 'application/pdf') return 'picture_as_pdf';
  if (contentType.startsWith('image/')) return 'image';
  if (contentType.includes('presentation') || contentType.includes('powerpoint')) return 'slideshow';
  if (contentType.includes('sheet') || contentType.includes('excel')) return 'table_chart';
  return 'description';
}

/**
 * Shows a file the app fetched with its sign-in: a tab for PDFs, pictures and text, a download for the rest.
 * The tab is opened before the request, while the tap still counts, because phones block a tab opened afterwards.
 * If the tab is blocked anyway the file is downloaded instead.
 */
export function openMaterial(source: (download: boolean) => Observable<Blob>, fileName: string, preview: boolean, finished: (error?: unknown) => void): void {
  const tab = preview ? window.open('', '_blank') : null;
  source(!preview).subscribe({
    next: (blob) => {
      if (preview && tab) {
        const url = URL.createObjectURL(blob);
        tab.location.href = url;
        // Revoked later so the tab has loaded it.
        setTimeout(() => URL.revokeObjectURL(url), 60_000);
      } else {
        saveBlob(blob, fileName);
      }
      finished();
    },
    error: (e) => {
      tab?.close();
      finished(e);
    },
  });
}
