import { PdfPage, pdfFromPages } from './pdf';

export type ReportStatus = 'Present' | 'Late' | 'Absent';

/** Everything the report shows, already in the person's language and format. */
export interface AttendanceReportData {
  teacher: string;
  student: string;
  className: string;
  period: string;
  /** The date the file was made, shown in the footer. */
  madeOn: string;
  sessions: string;
  present: string;
  late: string;
  absent: string;
  percent: string;
  rows: AttendanceReportRow[];
}

export interface AttendanceReportRow {
  date: string;
  time: string;
  status: ReportStatus;
  statusText: string;
  extra: boolean;
}

/** The words on the report, from the translation files. */
export interface AttendanceReportLabels {
  title: string;
  student: string;
  className: string;
  period: string;
  sessions: string;
  present: string;
  late: string;
  absent: string;
  rate: string;
  number: string;
  date: string;
  time: string;
  status: string;
  extra: string;
  none: string;
  page: string;
  of: string;
  madeOn: string;
}

// A report is shared outside the app, so it always has a light background whatever theme the app is in.
const INK = '#1b1b1f';
const MUTED = '#5f6368';
const ACCENT = '#0b57d0';
const LINE = '#dadce0';
const BAND = '#f3f4f7';
const STATUS_COLOUR: Record<ReportStatus, string> = { Present: '#1e7d32', Late: '#a15c00', Absent: '#b3261e' };
const FONT = 'system-ui, -apple-system, "Segoe UI", Roboto, "Noto Sans", "Noto Sans Sinhala", "Noto Sans Tamil", sans-serif';

// Layout in A4 points; the canvas is drawn larger so the text stays sharp when zoomed.
const W = 595;
const H = 842;
const SCALE = 2.5;
const PAD = 40;
const ROW = 26;
const FIRST_TOP = 330;
const NEXT_TOP = 80;
const FOOTER = 56;

/** How many table rows go on each page: the first page also holds the heading and the totals. At least one page. */
export function rowsPerPage(count: number): number[] {
  const first = Math.floor((H - FIRST_TOP - FOOTER) / ROW);
  const next = Math.floor((H - NEXT_TOP - FOOTER) / ROW);
  const pages = [Math.min(count, first)];
  let left = count - pages[0];
  while (left > 0) {
    pages.push(Math.min(left, next));
    left -= next;
  }
  return pages;
}

/** Draws the report and wraps it as a PDF file, one A4 page for about 20 classes. */
export async function drawAttendanceReport(data: AttendanceReportData, labels: AttendanceReportLabels): Promise<Blob> {
  const counts = rowsPerPage(data.rows.length);
  const pages: PdfPage[] = [];
  let start = 0;
  for (let i = 0; i < counts.length; i++) {
    const rows = data.rows.slice(start, start + counts[i]);
    pages.push(await drawPage(data, labels, rows, start, i, counts.length));
    start += counts[i];
  }
  return pdfFromPages(pages);
}

async function drawPage(
  data: AttendanceReportData,
  labels: AttendanceReportLabels,
  rows: AttendanceReportRow[],
  firstNumber: number,
  index: number,
  total: number,
): Promise<PdfPage> {
  const canvas = document.createElement('canvas');
  canvas.width = Math.round(W * SCALE);
  canvas.height = Math.round(H * SCALE);
  const ctx = canvas.getContext('2d');
  if (!ctx) throw new Error('Canvas is not available');
  ctx.scale(SCALE, SCALE);
  ctx.textBaseline = 'alphabetic';

  ctx.fillStyle = '#ffffff';
  ctx.fillRect(0, 0, W, H);
  ctx.fillStyle = ACCENT;
  ctx.fillRect(0, 0, W, 8);

  let y: number;
  if (index === 0) {
    text(ctx, labels.title, PAD, 62, `700 24px ${FONT}`, ACCENT);
    text(ctx, data.teacher, PAD, 86, `400 13px ${FONT}`, MUTED);

    const facts = [
      [labels.student, data.student],
      [labels.className, data.className],
      [labels.period, data.period],
    ];
    facts.forEach(([label, value], n) => {
      const row = 120 + n * 28;
      text(ctx, label, PAD, row, `400 13px ${FONT}`, MUTED);
      text(ctx, fit(ctx, value, W - PAD * 2 - 110, `600 15px ${FONT}`), PAD + 110, row, `600 15px ${FONT}`, INK);
    });

    const boxes = [
      [labels.sessions, data.sessions, INK],
      [labels.present, data.present, STATUS_COLOUR.Present],
      [labels.late, data.late, STATUS_COLOUR.Late],
      [labels.absent, data.absent, STATUS_COLOUR.Absent],
      [labels.rate, data.percent, ACCENT],
    ];
    const gap = 8;
    const boxWidth = (W - PAD * 2 - gap * (boxes.length - 1)) / boxes.length;
    boxes.forEach(([label, value, colour], n) => {
      const x = PAD + n * (boxWidth + gap);
      ctx.fillStyle = BAND;
      ctx.beginPath();
      ctx.roundRect(x, 224, boxWidth, 66, 8);
      ctx.fill();
      text(ctx, value, x + 10, 258, `700 24px ${FONT}`, colour);
      text(ctx, fit(ctx, label, boxWidth - 16, `400 11px ${FONT}`), x + 10, 278, `400 11px ${FONT}`, MUTED);
    });
    y = FIRST_TOP - 14;
  } else {
    text(ctx, `${data.student} · ${data.className}`, PAD, 48, `600 13px ${FONT}`, MUTED);
    y = NEXT_TOP - 14;
  }

  // Table heading.
  const col = { number: PAD, date: PAD + 36, time: PAD + 250, status: W - PAD };
  ctx.fillStyle = BAND;
  ctx.fillRect(PAD, y - 4, W - PAD * 2, ROW);
  text(ctx, labels.number, col.number + 6, y + 14, `600 12px ${FONT}`, MUTED);
  text(ctx, labels.date, col.date, y + 14, `600 12px ${FONT}`, MUTED);
  text(ctx, labels.time, col.time, y + 14, `600 12px ${FONT}`, MUTED);
  text(ctx, labels.status, col.status - 6, y + 14, `600 12px ${FONT}`, MUTED, 'right');
  y += ROW;

  if (data.rows.length === 0) {
    text(ctx, labels.none, PAD + 6, y + 24, `400 14px ${FONT}`, MUTED);
  }

  rows.forEach((row, n) => {
    text(ctx, String(firstNumber + n + 1), col.number + 6, y + 14, `400 12px ${FONT}`, MUTED);
    text(ctx, row.date, col.date, y + 14, `500 13px ${FONT}`, INK);
    if (row.extra) {
      const dateWidth = ctx.measureText(row.date).width;
      text(ctx, labels.extra, col.date + dateWidth + 8, y + 14, `400 11px ${FONT}`, MUTED);
    }
    text(ctx, row.time, col.time, y + 14, `400 13px ${FONT}`, INK);
    text(ctx, row.statusText, col.status - 6, y + 14, `600 13px ${FONT}`, STATUS_COLOUR[row.status], 'right');
    ctx.strokeStyle = LINE;
    ctx.lineWidth = 0.5;
    ctx.beginPath();
    ctx.moveTo(PAD, y + ROW - 4);
    ctx.lineTo(W - PAD, y + ROW - 4);
    ctx.stroke();
    y += ROW;
  });

  text(ctx, `${labels.madeOn} ${data.madeOn}`, PAD, H - 28, `400 11px ${FONT}`, MUTED);
  text(ctx, `${labels.page} ${index + 1} ${labels.of} ${total}`, W - PAD, H - 28, `400 11px ${FONT}`, MUTED, 'right');

  const jpeg = await new Promise<Blob>((resolve, reject) =>
    canvas.toBlob((blob) => (blob ? resolve(blob) : reject(new Error('Could not make the page'))), 'image/jpeg', 0.92),
  );
  return { jpeg: new Uint8Array(await jpeg.arrayBuffer()), width: canvas.width, height: canvas.height };
}

function text(ctx: CanvasRenderingContext2D, value: string, x: number, y: number, font: string, colour: string, align: CanvasTextAlign = 'left'): void {
  ctx.font = font;
  ctx.fillStyle = colour;
  ctx.textAlign = align;
  ctx.fillText(value, x, y);
  ctx.textAlign = 'left';
}

/** Shortens text with an ellipsis so it fits the width. */
function fit(ctx: CanvasRenderingContext2D, value: string, width: number, font: string): string {
  ctx.font = font;
  if (ctx.measureText(value).width <= width) return value;
  let shorter = value;
  while (shorter.length > 1 && ctx.measureText(`${shorter}…`).width > width) shorter = shorter.slice(0, -1);
  return `${shorter.trimEnd()}…`;
}
