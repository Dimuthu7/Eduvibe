import { saveBlob } from './download';

/** Everything a receipt shows, already in the person's language and format. */
export interface ReceiptData {
  teacher: string;
  className: string;
  student: string;
  receiptNo: string;
  amount: string;
  method: string;
  paidOn: string;
  /** The months the money went to; empty when there are none to show. */
  months: string;
}

/** The words on the receipt, from the translation files. */
export interface ReceiptLabels {
  title: string;
  receiptNo: string;
  student: string;
  className: string;
  paidBy: string;
  date: string;
  months: string;
  amount: string;
  thanks: string;
}

export interface ReceiptRow {
  label: string;
  value: string;
}

/** The label and value lines of a receipt, top to bottom. The months line is left out when there are no months. */
export function receiptRows(data: ReceiptData, labels: ReceiptLabels): ReceiptRow[] {
  const rows: ReceiptRow[] = [
    { label: labels.receiptNo, value: data.receiptNo },
    { label: labels.student, value: data.student },
    { label: labels.className, value: data.className },
    { label: labels.date, value: data.paidOn },
    { label: labels.paidBy, value: data.method },
  ];
  if (data.months) rows.push({ label: labels.months, value: data.months });
  return rows;
}

// A receipt is shared outside the app (on WhatsApp, say), so it always has a light background whatever theme the app is in.
const INK = '#1b1b1f';
const MUTED = '#5f6368';
const ACCENT = '#0b57d0';
const LINE = '#dadce0';
const FONT = 'system-ui, -apple-system, "Segoe UI", Roboto, "Noto Sans", Inter, "Noto Sans Sinhala", "Noto Sans Tamil", sans-serif';
const WIDTH = 720;
const PAD = 48;
const SCALE = 2;

/** Draws the receipt as a PNG image. */
export function drawReceipt(data: ReceiptData, labels: ReceiptLabels): Promise<Blob> {
  const rows = receiptRows(data, labels);
  const measure = document.createElement('canvas').getContext('2d');
  if (!measure) return Promise.reject(new Error('Canvas is not available'));

  const valueWidth = WIDTH - PAD * 2 - 220;
  measure.font = `600 28px ${FONT}`;
  const wrapped = rows.map((row) => wrap(measure, row.value, valueWidth));
  const rowsHeight = wrapped.reduce((sum, lines) => sum + Math.max(1, lines.length) * 38 + 14, 0);
  const height = 150 + rowsHeight + 250;

  const canvas = document.createElement('canvas');
  canvas.width = WIDTH * SCALE;
  canvas.height = height * SCALE;
  const ctx = canvas.getContext('2d');
  if (!ctx) return Promise.reject(new Error('Canvas is not available'));
  ctx.scale(SCALE, SCALE);
  ctx.textBaseline = 'alphabetic';

  ctx.fillStyle = '#ffffff';
  ctx.fillRect(0, 0, WIDTH, height);
  ctx.fillStyle = ACCENT;
  ctx.fillRect(0, 0, WIDTH, 12);

  ctx.fillStyle = ACCENT;
  ctx.font = `700 34px ${FONT}`;
  ctx.fillText(labels.title, PAD, 84);
  ctx.fillStyle = MUTED;
  ctx.font = `400 24px ${FONT}`;
  ctx.fillText(data.teacher, PAD, 122);

  let y = 150;
  rows.forEach((row, i) => {
    y += 38;
    ctx.fillStyle = MUTED;
    ctx.font = `400 24px ${FONT}`;
    ctx.fillText(row.label, PAD, y);
    ctx.fillStyle = INK;
    ctx.font = `600 28px ${FONT}`;
    const lines = wrapped[i].length > 0 ? wrapped[i] : [''];
    lines.forEach((line, n) => ctx.fillText(line, PAD + 220, y + n * 38));
    y += (lines.length - 1) * 38 + 14;
    ctx.strokeStyle = LINE;
    ctx.beginPath();
    ctx.moveTo(PAD, y);
    ctx.lineTo(WIDTH - PAD, y);
    ctx.stroke();
  });

  y += 70;
  ctx.fillStyle = MUTED;
  ctx.font = `400 24px ${FONT}`;
  ctx.fillText(labels.amount, PAD, y);
  ctx.fillStyle = INK;
  ctx.font = `700 52px ${FONT}`;
  ctx.fillText(data.amount, PAD, y + 62);

  ctx.fillStyle = MUTED;
  ctx.font = `400 24px ${FONT}`;
  ctx.fillText(labels.thanks, PAD, height - 36);

  return new Promise((resolve, reject) => canvas.toBlob((blob) => (blob ? resolve(blob) : reject(new Error('Could not make the image'))), 'image/png'));
}

/** Breaks text into lines no wider than the given width, at spaces where it can. */
function wrap(ctx: CanvasRenderingContext2D, text: string, width: number): string[] {
  const lines: string[] = [];
  let line = '';
  for (const word of text.split(' ')) {
    const attempt = line ? `${line} ${word}` : word;
    if (line && ctx.measureText(attempt).width > width) {
      lines.push(line);
      line = word;
    } else {
      line = attempt;
    }
  }
  if (line) lines.push(line);
  return lines;
}

/** Opens the phone's share sheet with the image, or saves it when sharing files is not possible. Says what happened. */
export async function shareOrSave(blob: Blob, fileName: string, title: string): Promise<'shared' | 'saved' | 'cancelled'> {
  const file = new File([blob], fileName, { type: blob.type });
  if (typeof navigator.canShare === 'function' && navigator.canShare({ files: [file] })) {
    try {
      await navigator.share({ files: [file], title });
      return 'shared';
    } catch (error) {
      // Closing the share sheet is a choice, not a failure; anything else falls back to saving the file.
      if (error instanceof DOMException && error.name === 'AbortError') return 'cancelled';
    }
  }

  saveBlob(blob, fileName);
  return 'saved';
}
