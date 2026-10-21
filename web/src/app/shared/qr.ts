/** The dark squares of a QR code for this text, row by row. The encoder is loaded only when a code is first needed. */
export async function qrModules(value: string): Promise<boolean[][]> {
  const { default: qrcode } = await import('qrcode-generator');
  const qr = qrcode(0, 'M');
  qr.addData(value);
  qr.make();
  const size = qr.getModuleCount();
  return Array.from({ length: size }, (_, row) => Array.from({ length: size }, (_, col) => qr.isDark(row, col)));
}

/** Draws the code as square blocks, with the usual four-block quiet zone left blank around it, inside a square of the given size. */
export function drawQr(ctx: CanvasRenderingContext2D, modules: boolean[][], x: number, y: number, size: number, ink: string): void {
  const quiet = 4;
  const block = size / (modules.length + quiet * 2);
  ctx.fillStyle = ink;
  modules.forEach((row, r) =>
    row.forEach((dark, c) => {
      // A hair over the block size hides the seams that anti-aliasing leaves between neighbours.
      if (dark) ctx.fillRect(x + (c + quiet) * block, y + (r + quiet) * block, block + 0.35, block + 0.35);
    }),
  );
}
