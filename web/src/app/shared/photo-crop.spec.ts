import { baseScale, centred, clampFraming, MAX_ZOOM, panned, sourceSquare, visibleSide, zoomed } from './photo-crop';

// A landscape picture, 4000 x 3000, seen through a 256 px window.
const W = 4000;
const H = 3000;
const V = 256;

describe('photo framing', () => {
  it('starts with the whole shorter side showing, centred', () => {
    const framing = centred(W, H);
    expect(framing).toEqual({ zoom: 1, x: 2000, y: 1500 });
    expect(visibleSide(W, H, V, 1)).toBe(3000);
    expect(sourceSquare(W, H, V, framing)).toEqual({ x: 500, y: 0, side: 3000 });
    expect(baseScale(W, H, V)).toBeCloseTo(V / 3000);
  });

  it('shows a smaller part of the picture as it is zoomed in', () => {
    expect(visibleSide(W, H, V, 2)).toBe(1500);
    expect(visibleSide(W, H, V, 4)).toBe(750);
  });

  it('keeps the window inside the picture however far it is dragged', () => {
    const start = centred(W, H);
    // Dragging the picture far to the right shows its left edge and stops there.
    const left = panned(W, H, V, start, 100000, 0);
    expect(sourceSquare(W, H, V, left).x).toBe(0);
    const right = panned(W, H, V, start, -100000, 0);
    expect(sourceSquare(W, H, V, right).x + 3000).toBe(W);
    // At zoom 1 a landscape picture cannot move up or down at all.
    expect(panned(W, H, V, start, 0, 500).y).toBe(1500);
  });

  it('moves the picture with the finger: the same distance in window pixels at any zoom', () => {
    const moved = panned(W, H, V, zoomed(W, H, V, centred(W, H), 2), -10, 0);
    // At zoom 2 the window shows 1500 picture pixels across 256 window pixels, so 10 pixels is about 58.6.
    expect(moved.x).toBeCloseTo(2000 + 10 * (1500 / V));
  });

  it('limits zoom to between 1 and the maximum and keeps the centre inside the picture', () => {
    const framing = centred(W, H);
    expect(zoomed(W, H, V, framing, 0.2).zoom).toBe(1);
    expect(zoomed(W, H, V, framing, 99).zoom).toBe(MAX_ZOOM);
    const edge = clampFraming(W, H, V, { zoom: 4, x: 0, y: 0 });
    expect(sourceSquare(W, H, V, edge)).toEqual({ x: 0, y: 0, side: 750 });
  });

  it('pulls the window back inside when zooming out near an edge', () => {
    const zoomedIn = clampFraming(W, H, V, { zoom: 4, x: 700, y: 400 });
    const out = zoomed(W, H, V, zoomedIn, 1);
    const square = sourceSquare(W, H, V, out);
    expect(square.x).toBeGreaterThanOrEqual(0);
    expect(square.y).toBeGreaterThanOrEqual(0);
    expect(square.x + square.side).toBeLessThanOrEqual(W);
    expect(square.y + square.side).toBeLessThanOrEqual(H);
  });

  it('works for a tall picture too', () => {
    expect(sourceSquare(3000, 4000, V, centred(3000, 4000))).toEqual({ x: 0, y: 500, side: 3000 });
  });
});
