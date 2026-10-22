import { A4, PdfPage } from './pdf';
import { canvasToPage, FONT, INK, LINE, MUTED, fit, newCanvas, text } from './pdf-draw';
import { drawQr, qrModules } from './qr';

/** What the QR text starts with; the server only accepts codes that begin this way. */
export const CARD_PREFIX = 'EV1:';

export interface CardFace {
  fullName: string;
  indexNumber: number | null;
  code: string;
}

export interface CardSheetData {
  classTitle: string;
  teacher: string;
  cards: CardFace[];
}

export interface CardSheetLabels {
  /** "No. 3" for a student's number in the class. */
  number: (n: number) => string;
  scan: string;
}

const MARGIN = 28;
const CARD_W = 256;
const CARD_H = 148;
const GAP_X = 24;
const GAP_Y = 8;
const COLUMNS = 2;
export const CARDS_PER_PAGE = COLUMNS * Math.floor((A4.height - MARGIN * 2 + GAP_Y) / (CARD_H + GAP_Y));

const NAME_FONT = `600 14px ${FONT}`;
const SMALL_FONT = `400 10.5px ${FONT}`;
const LABEL_FONT = `500 10px ${FONT}`;

/** Splits a name over at most two lines so a long name still fits beside the code. */
function twoLines(ctx: CanvasRenderingContext2D, name: string, width: number): string[] {
  ctx.font = NAME_FONT;
  const words = name.split(/\s+/);
  const first: string[] = [];
  while (words.length > 0 && ctx.measureText([...first, words[0]].join(' ')).width <= width) first.push(words.shift()!);
  if (first.length === 0) first.push(words.shift()!);
  return [fit(ctx, first.join(' '), width, NAME_FONT), ...(words.length ? [fit(ctx, words.join(' '), width, NAME_FONT)] : [])];
}

/** The cards as A4 pages, ten to a page with cut lines. Drawn in the browser so Sinhala and Tamil names print correctly. */
export async function drawCardSheet(data: CardSheetData, labels: CardSheetLabels): Promise<PdfPage[]> {
  const pages: PdfPage[] = [];
  for (let start = 0; start < data.cards.length || start === 0; start += CARDS_PER_PAGE) {
    const { canvas, ctx } = newCanvas(A4.width, A4.height);
    const slice = data.cards.slice(start, start + CARDS_PER_PAGE);
    for (let i = 0; i < slice.length; i++) {
      const x = MARGIN + (i % COLUMNS) * (CARD_W + GAP_X);
      const y = MARGIN + Math.floor(i / COLUMNS) * (CARD_H + GAP_Y);
      await drawCard(ctx, slice[i], data, labels, x, y);
    }
    pages.push(await canvasToPage(canvas));
  }
  return pages;
}

async function drawCard(ctx: CanvasRenderingContext2D, card: CardFace, data: CardSheetData, labels: CardSheetLabels, x: number, y: number): Promise<void> {
  ctx.save();
  ctx.setLineDash([4, 3]);
  ctx.strokeStyle = LINE;
  ctx.lineWidth = 0.75;
  ctx.strokeRect(x, y, CARD_W, CARD_H);
  ctx.restore();

  const qr = 112;
  drawQr(ctx, await qrModules(CARD_PREFIX + card.code), x + 10, y + (CARD_H - qr) / 2 - 6, qr, INK);
  text(ctx, labels.scan, x + 10 + qr / 2, y + CARD_H - 12, LABEL_FONT, MUTED, 'center');

  const left = x + qr + 20;
  const width = CARD_W - qr - 28;
  let line = y + 36;
  for (const part of twoLines(ctx, card.fullName, width)) {
    text(ctx, part, left, line, NAME_FONT, INK);
    line += 18;
  }
  if (card.indexNumber !== null) {
    text(ctx, labels.number(card.indexNumber), left, line + 3, `600 12px ${FONT}`, INK);
    line += 20;
  }
  text(ctx, fit(ctx, data.classTitle, width, SMALL_FONT), left, line + 6, SMALL_FONT, MUTED);
  if (data.teacher) text(ctx, fit(ctx, data.teacher, width, SMALL_FONT), left, line + 22, SMALL_FONT, MUTED);
  text(ctx, 'GuruBook', x + CARD_W - 10, y + CARD_H - 12, LABEL_FONT, MUTED, 'right');
}
