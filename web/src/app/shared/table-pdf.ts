import { A4, PdfPage, pdfFromPages } from './pdf';
import { ACCENT, BAND, canvasToPage, FONT, INK, LINE, MUTED, fit, newCanvas, text } from './pdf-draw';

/** A cell as the report endpoint sends it. */
export type Cell = string | number | null;

/** Everything a table PDF shows. Titles and facts are already in the person's language; headers and cells come from the report itself. */
export interface TablePdfData {
  title: string;
  /** Short lines under the title, such as "Class: Maths" or "Month: October 2026". */
  facts: string[];
  teacher: string;
  headers: string[];
  rows: Cell[][];
  /** The date the file was made, shown in the footer. */
  madeOn: string;
}

export interface TablePdfLabels {
  page: string;
  of: string;
  madeOn: string;
  none: string;
}

export type Measure = (text: string, font: string) => number;

export interface PlannedColumn {
  header: string;
  align: 'left' | 'right' | 'center';
  /** A column of one-letter marks (P, L, A): narrow, with its heading turned on its side. */
  narrow: boolean;
  width: number;
}

export interface PageSlice {
  /** Which group of columns this page shows (a wide table is printed in groups, side by side across pages). */
  band: number;
  from: number;
  to: number;
  first: boolean;
}

export interface TableLayout {
  landscape: boolean;
  width: number;
  height: number;
  columns: PlannedColumn[];
  /** Column numbers of each group. When there is more than one, the first columns are repeated in every group. */
  bands: number[][];
  headerHeight: number;
  pages: PageSlice[];
}

// Layout in A4 points.
const MARGIN = 30;
const CELL_PAD = 6;
const ROW = 22;
const BODY_FONT = `400 11px ${FONT}`;
const BOLD_FONT = `600 11px ${FONT}`;
const MAX_TEXT_WIDTH = 170;
const MIN_TEXT_WIDTH = 56;
const NARROW_WIDTH = 24;
const FOOTER = 40;
const RUNNING_TOP = 52;
const STATUS_COLOUR: Record<string, string> = { P: '#1e7d32', L: '#a15c00', A: '#b3261e' };

const PORTRAIT = { width: A4.width, height: A4.height };
const LANDSCAPE = { width: A4.height, height: A4.width };

const isoDate = /^\d{4}-\d{2}-\d{2}$/;
const number = new Intl.NumberFormat('en-LK', { maximumFractionDigits: 2 });

/** A cell as text: numbers with thousands separators, dates as "12 Oct 2026". */
export function formatCell(value: Cell): string {
  if (value === null || value === '') return '';
  if (typeof value === 'number') return number.format(value);
  if (isoDate.test(value)) {
    return new Date(`${value}T00:00:00Z`).toLocaleDateString('en-GB', { timeZone: 'UTC', day: 'numeric', month: 'short', year: 'numeric' });
  }
  return value;
}

/** A row with no index but a name ("Total") is a totals row. */
const isTotalRow = (row: Cell[]): boolean => (row[0] === '' || row[0] === null) && typeof row[1] === 'string' && row[1] !== '';

/**
 * Decides how the table fits on paper: portrait if the columns fit, landscape if they fit that way (squeezing the text columns a
 * little), otherwise landscape with the columns split into groups that repeat the index and name. Pure, so it is easy to test.
 */
export function planTable(measure: Measure, headers: string[], rows: Cell[][], factCount: number): TableLayout {
  const cells = rows.map((row) => headers.map((_, c) => formatCell(row[c] ?? null)));

  const marks = headers.map((header, c) => header.length > 3 && cells.some((r) => r[c] !== '') && cells.every((r) => r[c].length <= 2) && rows.every((r) => typeof r[c] !== 'number'));
  const useNarrow = marks.filter(Boolean).length >= 4; // a register of sessions, not a table that happens to have a short column

  const columns: PlannedColumn[] = headers.map((header, c) => {
    const narrow = useNarrow && marks[c];
    const numeric = rows.length > 0 && rows.some((r) => r[c] !== null && r[c] !== '') && rows.every((r) => r[c] === null || r[c] === '' || typeof r[c] === 'number');
    const natural = narrow
      ? NARROW_WIDTH
      : Math.min(MAX_TEXT_WIDTH, Math.max(measure(header, BOLD_FONT), ...cells.map((r) => measure(r[c], BODY_FONT)), 0) + CELL_PAD * 2);
    return { header, narrow, align: narrow ? 'center' : numeric ? 'right' : 'left', width: Math.ceil(natural) };
  });

  const headerHeight = useNarrow ? Math.min(84, Math.max(...columns.filter((c) => c.narrow).map((c) => measure(c.header, BOLD_FONT))) + 14) : ROW + 4;
  const total = (list: PlannedColumn[]) => list.reduce((sum, c) => sum + c.width, 0);
  const availableFor = (page: { width: number }) => page.width - MARGIN * 2;

  let page = PORTRAIT;
  let bands: number[][] = [columns.map((_, i) => i)];
  if (total(columns) > availableFor(PORTRAIT)) {
    page = LANDSCAPE;
    const room = availableFor(LANDSCAPE);
    if (total(columns) > room) {
      // Squeeze the text columns, but not below 60% of what their text needs.
      const squeezable = columns.filter((c) => !c.narrow && c.align === 'left');
      const floors = new Map(squeezable.map((c) => [c, Math.max(MIN_TEXT_WIDTH, Math.ceil(c.width * 0.6))]));
      const fixed = total(columns) - total(squeezable);
      const minimum = fixed + [...floors.values()].reduce((sum, f) => sum + f, 0);
      if (squeezable.length > 0 && minimum <= room) {
        const ratio = (room - fixed) / total(squeezable);
        for (const c of squeezable) c.width = Math.max(floors.get(c)!, Math.floor(c.width * Math.min(1, ratio)));
        // Keeping each column at its floor can leave a little too much: take it from the widest that can still give.
        while (total(columns) > room) {
          const donor = squeezable.filter((c) => c.width > floors.get(c)!).sort((a, b) => b.width - a.width)[0];
          if (!donor) break;
          donor.width -= 1;
        }
      } else {
        bands = splitIntoBands(columns, room);
      }
    }
  }

  // Spare room goes to the text columns, so a short table fills the width instead of huddling on the left.
  if (bands.length === 1) {
    const spare = availableFor(page) - total(columns);
    const growable = columns.filter((c) => !c.narrow && c.align === 'left');
    if (spare > 0 && growable.length > 0) {
      const share = Math.floor(spare / growable.length);
      for (const c of growable) c.width += share;
    }
  }

  const slices: PageSlice[] = [];
  const topFirst = 108 + factCount * 18 + 12;
  bands.forEach((_, band) => {
    let from = 0;
    let first = band === 0;
    do {
      const top = first ? topFirst : RUNNING_TOP;
      const capacity = Math.max(1, Math.floor((page.height - top - headerHeight - FOOTER) / ROW));
      const to = Math.min(rows.length, from + capacity);
      slices.push({ band, from, to, first });
      from = to;
      first = false;
    } while (from < rows.length);
  });

  return { landscape: page === LANDSCAPE, width: page.width, height: page.height, columns, bands, headerHeight, pages: slices };
}

/** Groups of columns that each fit the room, every group starting with the index and the name when there are several groups. */
function splitIntoBands(columns: PlannedColumn[], room: number): number[][] {
  const sticky = columns.length > 3 ? [0, 1] : [0];
  const stickyWidth = sticky.reduce((sum, c) => sum + columns[c].width, 0);
  const bands: number[][] = [];
  let current: number[] = [];
  let used = stickyWidth;
  for (let c = sticky.length; c < columns.length; c++) {
    if (current.length > 0 && used + columns[c].width > room) {
      bands.push([...sticky, ...current]);
      current = [];
      used = stickyWidth;
    }
    current.push(c);
    used += columns[c].width;
  }
  bands.push([...sticky, ...current]);
  return bands;
}

/** Draws the table as a PDF: a title block, a heading row repeated on every page, page numbers, landscape when the table is wide. */
export async function drawTablePdf(data: TablePdfData, labels: TablePdfLabels): Promise<Blob> {
  const probe = newCanvas(10, 10).ctx;
  const layout = planTable(
    (value, font) => {
      probe.font = font;
      return probe.measureText(value).width;
    },
    data.headers,
    data.rows,
    data.facts.length,
  );
  const pages: PdfPage[] = [];
  for (let i = 0; i < layout.pages.length; i++) {
    pages.push(await drawPage(data, labels, layout, i));
  }
  return pdfFromPages(pages);
}

async function drawPage(data: TablePdfData, labels: TablePdfLabels, layout: TableLayout, index: number): Promise<PdfPage> {
  const slice = layout.pages[index];
  const { width: W, height: H } = layout;
  const { canvas, ctx } = newCanvas(W, H);
  ctx.fillStyle = ACCENT;
  ctx.fillRect(0, 0, W, 8);

  let y: number;
  if (slice.first) {
    text(ctx, fit(ctx, data.title, W - MARGIN * 2, `700 22px ${FONT}`), MARGIN, 58, `700 22px ${FONT}`, ACCENT);
    text(ctx, data.teacher, MARGIN, 80, `400 13px ${FONT}`, MUTED);
    data.facts.forEach((fact, n) => text(ctx, fit(ctx, fact, W - MARGIN * 2, `500 13px ${FONT}`), MARGIN, 108 + n * 18, `500 13px ${FONT}`, INK));
    y = 108 + data.facts.length * 18 + 12;
  } else {
    text(ctx, fit(ctx, data.title, W - MARGIN * 2, `600 12px ${FONT}`), MARGIN, 34, `600 12px ${FONT}`, MUTED);
    y = RUNNING_TOP;
  }

  const band = layout.bands[slice.band];
  const columns = band.map((c) => layout.columns[c]);
  const tableWidth = columns.reduce((sum, c) => sum + c.width, 0);
  const x0 = MARGIN;

  // Heading row, repeated on every page.
  ctx.fillStyle = BAND;
  ctx.fillRect(x0, y, tableWidth, layout.headerHeight);
  let x = x0;
  for (const column of columns) {
    if (column.narrow) {
      ctx.save();
      ctx.translate(x + column.width / 2 + 4, y + layout.headerHeight - 6);
      ctx.rotate(-Math.PI / 2);
      text(ctx, fit(ctx, column.header, layout.headerHeight - 10, BOLD_FONT), 0, 0, BOLD_FONT, MUTED);
      ctx.restore();
    } else {
      const inner = column.width - CELL_PAD * 2;
      const label = fit(ctx, column.header, inner, BOLD_FONT);
      const base = y + layout.headerHeight / 2 + 4;
      if (column.align === 'right') text(ctx, label, x + column.width - CELL_PAD, base, BOLD_FONT, MUTED, 'right');
      else text(ctx, label, x + CELL_PAD, base, BOLD_FONT, MUTED);
    }
    x += column.width;
  }
  y += layout.headerHeight;

  if (data.rows.length === 0) {
    text(ctx, labels.none, x0 + CELL_PAD, y + 20, `400 14px ${FONT}`, MUTED);
  }

  for (const row of data.rows.slice(slice.from, slice.to)) {
    const total = isTotalRow(row);
    if (total) {
      ctx.fillStyle = BAND;
      ctx.fillRect(x0, y, tableWidth, ROW);
    }
    x = x0;
    band.forEach((c, n) => {
      const column = columns[n];
      const value = formatCell(row[c] ?? null);
      const font = total ? BOLD_FONT : BODY_FONT;
      const colour = column.narrow ? (STATUS_COLOUR[value] ?? INK) : INK;
      const base = y + 15;
      if (column.align === 'center') text(ctx, value, x + column.width / 2, base, column.narrow ? BOLD_FONT : font, colour, 'center');
      else if (column.align === 'right') text(ctx, value, x + column.width - CELL_PAD, base, font, colour, 'right');
      else text(ctx, fit(ctx, value, column.width - CELL_PAD * 2, font), x + CELL_PAD, base, font, colour);
      x += column.width;
    });
    ctx.strokeStyle = LINE;
    ctx.lineWidth = 0.5;
    ctx.beginPath();
    ctx.moveTo(x0, y + ROW);
    ctx.lineTo(x0 + tableWidth, y + ROW);
    ctx.stroke();
    y += ROW;
  }

  text(ctx, `${labels.madeOn} ${data.madeOn}`, MARGIN, H - 20, `400 10px ${FONT}`, MUTED);
  text(ctx, `${labels.page} ${index + 1} ${labels.of} ${layout.pages.length}`, W - MARGIN, H - 20, `400 10px ${FONT}`, MUTED, 'right');

  return canvasToPage(canvas, layout.landscape ? { width: W, height: H } : undefined);
}
