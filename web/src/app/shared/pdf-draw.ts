import { PdfPage } from './pdf';

// A PDF is shared outside the app, so it always has a light background whatever theme the app is in.
export const INK = '#1b1b1f';
export const MUTED = '#5f6368';
export const ACCENT = '#0b57d0';
export const LINE = '#dadce0';
export const BAND = '#f3f4f7';
export const FONT = 'system-ui, -apple-system, "Segoe UI", Roboto, "Noto Sans", Inter, "Noto Sans Sinhala", "Noto Sans Tamil", sans-serif';

/** The canvas is drawn larger than the page so the text stays sharp when zoomed. */
export const SCALE = 2.5;

/** A white canvas for a page of this size in PDF points, with the context already scaled so drawing uses points. */
export function newCanvas(width: number, height: number): { canvas: HTMLCanvasElement; ctx: CanvasRenderingContext2D } {
  const canvas = document.createElement('canvas');
  canvas.width = Math.round(width * SCALE);
  canvas.height = Math.round(height * SCALE);
  const ctx = canvas.getContext('2d');
  if (!ctx) throw new Error('Canvas is not available');
  ctx.scale(SCALE, SCALE);
  ctx.textBaseline = 'alphabetic';
  ctx.fillStyle = '#ffffff';
  ctx.fillRect(0, 0, width, height);
  return { canvas, ctx };
}

/** The finished canvas as a page picture. Pass the page size when it is not portrait A4. */
export async function canvasToPage(canvas: HTMLCanvasElement, size?: { width: number; height: number }): Promise<PdfPage> {
  const jpeg = await new Promise<Blob>((resolve, reject) =>
    canvas.toBlob((blob) => (blob ? resolve(blob) : reject(new Error('Could not make the page'))), 'image/jpeg', 0.92),
  );
  return {
    jpeg: new Uint8Array(await jpeg.arrayBuffer()),
    width: canvas.width,
    height: canvas.height,
    ...(size ? { pageWidth: size.width, pageHeight: size.height } : {}),
  };
}

export function text(ctx: CanvasRenderingContext2D, value: string, x: number, y: number, font: string, colour: string, align: CanvasTextAlign = 'left'): void {
  ctx.font = font;
  ctx.fillStyle = colour;
  ctx.textAlign = align;
  ctx.fillText(value, x, y);
  ctx.textAlign = 'left';
}

/** Shortens text with an ellipsis so it fits the width. */
export function fit(ctx: CanvasRenderingContext2D, value: string, width: number, font: string): string {
  ctx.font = font;
  if (ctx.measureText(value).width <= width) return value;
  let shorter = value;
  while (shorter.length > 1 && ctx.measureText(`${shorter}…`).width > width) shorter = shorter.slice(0, -1);
  return `${shorter.trimEnd()}…`;
}
