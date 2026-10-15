import { A4, pdfFromPages } from './pdf';

const page = (size: number) => ({ jpeg: new Uint8Array(size).fill(7), width: 100, height: 141 });

async function textOf(blob: Blob): Promise<string> {
  return new TextDecoder('latin1').decode(await blob.arrayBuffer());
}

describe('pdfFromPages', () => {
  it('makes a PDF with one A4 page per picture', async () => {
    const pdf = await textOf(pdfFromPages([page(30), page(40)]));
    expect(pdf.startsWith('%PDF-1.4')).toBe(true);
    expect(pdf.trimEnd().endsWith('%%EOF')).toBe(true);
    expect(pdf).toContain('/Count 2');
    expect(pdf.match(/\/Type \/Page /g)).toHaveLength(2);
    expect(pdf).toContain(`/MediaBox [0 0 ${A4.width} ${A4.height}]`);
    expect(pdf).toContain('/Filter /DCTDecode');
  });

  it('records where every object starts so readers can find them', async () => {
    const blob = pdfFromPages([page(30), page(40)]);
    const pdf = await textOf(blob);
    const table = pdf.slice(pdf.lastIndexOf('\nxref\n'));
    const entries = [...table.matchAll(/(\d{10}) 00000 n /g)].map((m) => Number(m[1]));
    expect(entries).toHaveLength(8);
    entries.forEach((offset, i) => expect(pdf.slice(offset, offset + `${i + 1} 0 obj`.length)).toBe(`${i + 1} 0 obj`));
    const startxref = Number(/startxref\n(\d+)/.exec(pdf)?.[1]);
    expect(pdf.slice(startxref, startxref + 4)).toBe('xref');
  });

  it('states each picture length exactly', async () => {
    const pdf = await textOf(pdfFromPages([page(33)]));
    expect(pdf).toContain('/Length 33 >>\nstream\n');
  });

  it('gives a page its own size when it is not portrait A4', async () => {
    const pdf = await textOf(pdfFromPages([page(30), { ...page(30), pageWidth: A4.height, pageHeight: A4.width }]));
    expect(pdf).toContain(`/MediaBox [0 0 ${A4.width} ${A4.height}]`);
    expect(pdf).toContain(`/MediaBox [0 0 ${A4.height} ${A4.width}]`);
    expect(pdf).toContain(`q ${A4.height} 0 0 ${A4.width} 0 0 cm`);
  });
});
