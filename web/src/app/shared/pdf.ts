/** A page picture already encoded as JPEG, and its size in pixels. */
export interface PdfPage {
  jpeg: Uint8Array;
  width: number;
  height: number;
  /** The page size in PDF points when it is not portrait A4 (a wide table is printed on landscape A4). */
  pageWidth?: number;
  pageHeight?: number;
}

/** A4 in PDF points (1/72 inch). */
export const A4 = { width: 595.28, height: 841.89 };

/**
 * Wraps page pictures into a PDF file, one picture per page (A4 portrait unless the page says otherwise). The pictures are drawn in the browser so every
 * language (Sinhala and Tamil included) looks right without bundling fonts or a PDF library.
 */
export function pdfFromPages(pages: PdfPage[]): Blob {
  const text = new TextEncoder();
  const parts: Uint8Array[] = [];
  const offsets: number[] = [];
  let length = 0;
  const push = (chunk: Uint8Array) => {
    parts.push(chunk);
    length += chunk.length;
  };
  const write = (value: string) => push(text.encode(value));
  const startObject = (id: number) => {
    offsets[id] = length;
    write(`${id} 0 obj\n`);
  };

  // Objects: 1 catalog, 2 page list, then a page, its content and its picture for every page.
  const pageId = (i: number) => 3 + i * 3;
  write('%PDF-1.4\n');
  push(Uint8Array.of(0x25, 0xe2, 0xe3, 0xcf, 0xd3, 0x0a)); // marks the file as binary for mail and chat programs

  startObject(1);
  write('<< /Type /Catalog /Pages 2 0 R >>\nendobj\n');
  startObject(2);
  write(`<< /Type /Pages /Count ${pages.length} /Kids [${pages.map((_, i) => `${pageId(i)} 0 R`).join(' ')}] >>\nendobj\n`);

  pages.forEach((page, i) => {
    const id = pageId(i);
    const pageWidth = page.pageWidth ?? A4.width;
    const pageHeight = page.pageHeight ?? A4.height;
    startObject(id);
    write(
      `<< /Type /Page /Parent 2 0 R /MediaBox [0 0 ${pageWidth} ${pageHeight}] /Resources << /XObject << /Im0 ${id + 2} 0 R >> >> /Contents ${id + 1} 0 R >>\nendobj\n`,
    );
    const draw = `q ${pageWidth} 0 0 ${pageHeight} 0 0 cm /Im0 Do Q`;
    startObject(id + 1);
    write(`<< /Length ${draw.length} >>\nstream\n${draw}\nendstream\nendobj\n`);
    startObject(id + 2);
    write(
      `<< /Type /XObject /Subtype /Image /Width ${page.width} /Height ${page.height} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length ${page.jpeg.length} >>\nstream\n`,
    );
    push(page.jpeg);
    write('\nendstream\nendobj\n');
  });

  const count = 3 + pages.length * 3;
  const xref = length;
  write(`xref\n0 ${count}\n0000000000 65535 f \n`);
  for (let id = 1; id < count; id++) write(`${String(offsets[id]).padStart(10, '0')} 00000 n \n`);
  write(`trailer\n<< /Size ${count} /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF\n`);

  return new Blob(parts as BlobPart[], { type: 'application/pdf' });
}
