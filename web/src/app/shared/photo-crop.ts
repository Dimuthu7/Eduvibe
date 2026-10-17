/** The picture shown through a round window: how far it is zoomed and which point of it sits in the middle. */
export interface Framing {
  /** 1 fills the window with the shorter side of the picture; larger zooms in. */
  zoom: number;
  /** The point of the picture in the middle of the window, in picture pixels. */
  x: number;
  y: number;
}

export const MAX_ZOOM = 4;

/** Pixels of the picture per pixel of the window at zoom 1: the shorter side just fills the window. */
export const baseScale = (width: number, height: number, window: number): number => window / Math.min(width, height);

/** Side, in picture pixels, of the square the window shows. */
export const visibleSide = (width: number, height: number, window: number, zoom: number): number =>
  window / (baseScale(width, height, window) * zoom);

/** Keeps the window inside the picture, so no empty corner is ever saved. */
export function clampFraming(width: number, height: number, window: number, framing: Framing): Framing {
  const zoom = Math.min(MAX_ZOOM, Math.max(1, framing.zoom));
  const half = visibleSide(width, height, window, zoom) / 2;
  return {
    zoom,
    x: Math.min(width - half, Math.max(half, framing.x)),
    y: Math.min(height - half, Math.max(half, framing.y)),
  };
}

/** The first framing: the whole shorter side, centred. */
export const centred = (width: number, height: number): Framing => ({ zoom: 1, x: width / 2, y: height / 2 });

/** The square of the picture the window shows, as the source rectangle for drawing it. */
export function sourceSquare(width: number, height: number, window: number, framing: Framing): { x: number; y: number; side: number } {
  const side = visibleSide(width, height, window, framing.zoom);
  return { x: framing.x - side / 2, y: framing.y - side / 2, side };
}

/** Moves the picture with a finger: dragging right by dx window pixels shows what was further left. */
export function panned(width: number, height: number, window: number, framing: Framing, dx: number, dy: number): Framing {
  const perPixel = 1 / (baseScale(width, height, window) * framing.zoom);
  return clampFraming(width, height, window, { ...framing, x: framing.x - dx * perPixel, y: framing.y - dy * perPixel });
}

/** Changes the zoom and keeps the middle of the window where it was. */
export const zoomed = (width: number, height: number, window: number, framing: Framing, zoom: number): Framing =>
  clampFraming(width, height, window, { ...framing, zoom });

/** The picture as a square canvas, ready to upload as a small JPEG. Returns null when the browser cannot make one. */
export async function cropToJpeg(
  source: CanvasImageSource & { width: number; height: number },
  window: number,
  framing: Framing,
  size = 320,
): Promise<Blob | null> {
  const canvas = document.createElement('canvas');
  canvas.width = size;
  canvas.height = size;
  const context = canvas.getContext('2d');
  if (!context) return null;
  const square = sourceSquare(source.width, source.height, window, framing);
  // JPEG has no transparency; a flat colour keeps a transparent PNG from turning black.
  context.fillStyle = '#ffffff';
  context.fillRect(0, 0, size, size);
  context.imageSmoothingQuality = 'high';
  context.drawImage(source, square.x, square.y, square.side, square.side, 0, 0, size, size);
  // 320 px at this quality is about 20 to 40 kB. Step down if a very detailed picture still comes out large.
  let blob: Blob | null = null;
  for (const quality of [0.85, 0.7, 0.55]) {
    blob = await new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, 'image/jpeg', quality));
    if (blob && blob.size <= 120 * 1024) break;
  }
  return blob;
}
