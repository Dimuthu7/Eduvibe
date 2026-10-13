/** Makes the browser save a file the app already holds in memory (an Excel report or a receipt image). */
export function saveBlob(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  document.body.append(link);
  link.click();
  link.remove();
  // Revoked a little later so the download has started on slow phones.
  setTimeout(() => URL.revokeObjectURL(url), 10_000);
}

/** A file name safe on every system: letters and digits of any language, spaces turned to hyphens, no slashes or colons. */
export function fileNamePart(text: string): string {
  return text.trim().replace(/[^\p{L}\p{M}\p{N}]+/gu, '-').replace(/^-+|-+$/g, '') || 'file';
}
