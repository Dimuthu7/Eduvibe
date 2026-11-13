/** The text on a GuruBook student card: this prefix and then the card's code. */
export const CARD_PREFIX = 'EV1:';

/** Why there is nothing to scan with: a translation key, so the screen can say it in the teacher's language. */
export type CameraProblem = 'scan.no_camera' | 'scan.camera_blocked';

const LOOK_EVERY_MS = 150;
const SAME_CODE_MS = 3000;

interface Detector {
  detect(source: CanvasImageSource): Promise<{ rawValue: string }[]>;
}

/**
 * The back camera, read for QR codes. Phones that can read QR codes themselves use that; the rest decode the picture in the browser
 * with a small library loaded only when it is needed. The same code is handed over once and then held back for a few seconds, so a
 * card left in front of the lens is not read over and over.
 */
export class QrCamera {
  private stream: MediaStream | null = null;
  private timer: ReturnType<typeof setTimeout> | null = null;
  private stopped = false;
  private busy = false;
  private lastCode = '';
  private lastAt = 0;

  /**
   * Asks for the camera and calls `onCode` with each code read, waiting for it before looking again. The video element is asked for
   * rather than passed, because a screen that shows the camera only part of the time has none until it is drawn. Returns a problem
   * key when there is no camera to use, and null when it is running.
   */
  async start(element: () => HTMLVideoElement | null | undefined, onCode: (value: string) => void | Promise<void>): Promise<CameraProblem | null> {
    if (!navigator.mediaDevices?.getUserMedia) {
      return 'scan.no_camera';
    }
    try {
      this.stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: { ideal: 'environment' } }, audio: false });
    } catch (error) {
      return error instanceof DOMException && error.name === 'NotAllowedError' ? 'scan.camera_blocked' : 'scan.no_camera';
    }
    const video = await this.waitFor(element);
    if (this.stopped || !video) {
      this.stop();
      return null;
    }

    video.srcObject = this.stream;
    await video.play().catch(() => undefined);
    const look = await this.lookFunction(video);
    const tick = async () => {
      if (this.stopped) return;
      if (video.readyState >= 2) {
        const value = await look().catch(() => null);
        if (value && this.isNew(value)) {
          this.busy = true;
          try {
            await onCode(value);
          } finally {
            this.busy = false;
          }
        }
      }
      this.timer = setTimeout(tick, LOOK_EVERY_MS);
    };
    void tick();
    return null;
  }

  /** Stops the camera and the looking. Safe to call before it ever started, and more than once. */
  stop(): void {
    this.stopped = true;
    if (this.timer) clearTimeout(this.timer);
    this.timer = null;
    this.stream?.getTracks().forEach((track) => track.stop());
    this.stream = null;
  }

  /** The element can be a moment behind the camera, so it is waited for briefly rather than demanded at once. */
  private async waitFor(element: () => HTMLVideoElement | null | undefined): Promise<HTMLVideoElement | null> {
    for (let tries = 0; tries < 40 && !this.stopped; tries++) {
      const video = element();
      if (video) return video;
      await new Promise((resume) => setTimeout(resume, 25));
    }
    return null;
  }

  private isNew(value: string): boolean {
    const now = Date.now();
    if (this.busy || (value === this.lastCode && now - this.lastAt < SAME_CODE_MS)) return false;
    this.lastCode = value;
    this.lastAt = now;
    return true;
  }

  private async lookFunction(video: HTMLVideoElement): Promise<() => Promise<string | null>> {
    const Native = (window as unknown as { BarcodeDetector?: new (options: { formats: string[] }) => Detector }).BarcodeDetector;
    if (Native) {
      const detector = new Native({ formats: ['qr_code'] });
      return async () => (await detector.detect(video))[0]?.rawValue ?? null;
    }

    const { default: jsQR } = await import('jsqr');
    const canvas = document.createElement('canvas');
    const ctx = canvas.getContext('2d', { willReadFrequently: true })!;
    return async () => {
      const scale = Math.min(1, 480 / (video.videoWidth || 480));
      canvas.width = Math.round(video.videoWidth * scale);
      canvas.height = Math.round(video.videoHeight * scale);
      ctx.drawImage(video, 0, 0, canvas.width, canvas.height);
      const image = ctx.getImageData(0, 0, canvas.width, canvas.height);
      return jsQR(image.data, image.width, image.height, { inversionAttempts: 'dontInvert' })?.data ?? null;
    };
  }
}
