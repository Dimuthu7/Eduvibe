import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, inject, OnInit, signal, viewChild } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { firstValueFrom } from 'rxjs';
import { AttendanceService } from '../../core/api/attendance.service';
import { errorCode } from '../../core/api/problem';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { TranslateService } from '../../core/i18n/translate.service';

export interface ScanData {
  sessionId: string;
  /** Enrolments scanned earlier in this visit, so the count carries on. */
  arrived: string[];
}

type Answer = { kind: 'ok' | 'again' | 'error'; text: string };

const LOOK_EVERY_MS = 150;
const SAME_CARD_MS = 3000;
const CARD_PREFIX = 'EV1:';

interface Detector {
  detect(source: CanvasImageSource): Promise<{ rawValue: string }[]>;
}

/**
 * Full-screen camera for scanning student cards. Each card is looked up on the server (nothing is saved there); the dialog keeps the
 * list of who has arrived and hands it back when the teacher is done. Phones that can read QR codes themselves use that; the rest
 * decode the picture in the browser with a small library loaded only now.
 */
@Component({
  selector: 'app-scan-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatDialogModule, MatIconModule, TranslatePipe],
  template: `
    <div class="scanner">
      <video #video class="video" playsinline muted aria-hidden="true"></video>
      <div class="aim" aria-hidden="true"></div>
      <div class="top">
        <strong>{{ 'scan.title' | t }}</strong>
        <span class="count" role="status">{{ countText() }}</span>
      </div>
      <div class="bottom">
        <p class="answer {{ answer()?.kind ?? 'hint' }}" role="status" aria-live="polite">
          @if (cameraProblem(); as problem) {
            <mat-icon aria-hidden="true">videocam_off</mat-icon> {{ problem | t }}
          } @else if (answer(); as a) {
            <mat-icon aria-hidden="true">{{ a.kind === 'error' ? 'error' : 'check_circle' }}</mat-icon> {{ a.text }}
          } @else {
            {{ 'scan.hint' | t }}
          }
        </p>
        <button mat-flat-button type="button" (click)="done()">{{ 'scan.done' | t }}</button>
      </div>
    </div>
  `,
  styles: `
    :host { display: block; }
    .scanner { position: relative; display: grid; place-items: center; height: 100dvh; overflow: hidden; background: #000; color: #fff; }
    .video { position: absolute; inset: 0; width: 100%; height: 100%; object-fit: cover; }
    .aim { position: relative; width: min(70vw, 18rem); aspect-ratio: 1; border: 3px solid rgba(255, 255, 255, 0.85); border-radius: 1.25rem; box-shadow: 0 0 0 100vmax rgba(0, 0, 0, 0.45); }
    .top, .bottom { position: absolute; left: 0; right: 0; display: flex; gap: 0.75rem; align-items: center; justify-content: space-between; padding: 1rem; }
    .top { top: 0; padding-top: max(1rem, env(safe-area-inset-top)); }
    .bottom { bottom: 0; flex-direction: column; align-items: stretch; padding-bottom: max(1rem, env(safe-area-inset-bottom)); }
    .count { padding: 0.25rem 0.75rem; border-radius: 999px; background: rgba(255, 255, 255, 0.2); font: var(--mat-sys-label-large); }
    .answer { display: flex; gap: 0.5rem; align-items: center; justify-content: center; min-height: 3rem; margin: 0; padding: 0.5rem 1rem; border-radius: 0.75rem; background: rgba(0, 0, 0, 0.6); font: var(--mat-sys-title-medium); text-align: center; }
    .answer.ok mat-icon { color: #7ee2a0; }
    .answer.again mat-icon { color: #ffd479; }
    .answer.error mat-icon { color: #ff9b94; }
    .answer.hint { font: var(--mat-sys-body-large); }
  `,
})
export class ScanDialog implements OnInit {
  private readonly data = inject<ScanData>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<ScanDialog, string[]>>(MatDialogRef);
  private readonly api = inject(AttendanceService);
  private readonly i18n = inject(TranslateService);

  private readonly video = viewChild.required<ElementRef<HTMLVideoElement>>('video');
  protected readonly answer = signal<Answer | null>(null);
  protected readonly cameraProblem = signal<string | null>(null);
  protected readonly countText = signal('');

  private readonly arrived = new Set<string>(this.data.arrived);
  private stream: MediaStream | null = null;
  private timer: ReturnType<typeof setTimeout> | null = null;
  private lastCode = '';
  private lastAt = 0;
  private busy = false;
  private stopped = false;

  constructor() {
    this.showCount();
    inject(DestroyRef).onDestroy(() => this.stop());
    // Closing with the back button or a tap outside keeps what was scanned, just like Done.
    this.ref.disableClose = true;
    this.ref.backdropClick().subscribe(() => this.done());
    this.ref.keydownEvents().subscribe((e) => e.key === 'Escape' && this.done());
  }

  ngOnInit(): void {
    void this.start();
  }

  protected done(): void {
    this.ref.close([...this.arrived]);
  }

  private showCount(): void {
    this.countText.set(this.i18n.format('scan.arrived', { count: this.arrived.size }));
  }

  private async start(): Promise<void> {
    if (!navigator.mediaDevices?.getUserMedia) {
      this.cameraProblem.set('scan.no_camera');
      return;
    }
    try {
      this.stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: { ideal: 'environment' } }, audio: false });
    } catch (error) {
      this.cameraProblem.set(error instanceof DOMException && error.name === 'NotAllowedError' ? 'scan.camera_blocked' : 'scan.no_camera');
      return;
    }
    if (this.stopped) {
      this.stop();
      return;
    }

    const video = this.video().nativeElement;
    video.srcObject = this.stream;
    await video.play().catch(() => undefined);
    const look = await this.lookFunction(video);
    const tick = async () => {
      if (this.stopped) return;
      if (video.readyState >= 2) {
        const value = await look().catch(() => null);
        if (value) await this.handle(value);
      }
      this.timer = setTimeout(tick, LOOK_EVERY_MS);
    };
    void tick();
  }

  /** The phone's own QR reader when it has one, otherwise the library on a shrunken copy of the frame. */
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

  private async handle(value: string): Promise<void> {
    const now = Date.now();
    if (this.busy || (value === this.lastCode && now - this.lastAt < SAME_CARD_MS)) return;
    this.lastCode = value;
    this.lastAt = now;

    if (!value.startsWith(CARD_PREFIX)) {
      this.say('error', this.i18n.translate('error.card_unknown'), [200]);
      return;
    }

    this.busy = true;
    try {
      const student = await firstValueFrom(this.api.scan(this.data.sessionId, value));
      if (this.arrived.has(student.enrolmentId)) {
        this.say('again', this.i18n.format('scan.again', { name: student.fullName }), [40]);
      } else {
        this.arrived.add(student.enrolmentId);
        this.showCount();
        this.say('ok', this.i18n.format('scan.here', { name: student.fullName }), [60]);
      }
    } catch (error) {
      const code = errorCode(error);
      this.say('error', this.i18n.translate(code ? `error.${code}` : 'error.unknown'), [200]);
    } finally {
      this.busy = false;
    }
  }

  private say(kind: Answer['kind'], text: string, buzz: number[]): void {
    this.answer.set({ kind, text });
    navigator.vibrate?.(buzz);
  }

  private stop(): void {
    this.stopped = true;
    if (this.timer) clearTimeout(this.timer);
    this.stream?.getTracks().forEach((track) => track.stop());
    this.stream = null;
  }
}
