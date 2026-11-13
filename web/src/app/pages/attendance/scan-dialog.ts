import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, inject, OnInit, signal, viewChild } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { firstValueFrom } from 'rxjs';
import { AttendanceService } from '../../core/api/attendance.service';
import { errorCode } from '../../core/api/problem';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { TranslateService } from '../../core/i18n/translate.service';
import { CARD_PREFIX, QrCamera } from '../../shared/qr-camera';

export interface ScanData {
  sessionId: string;
  /** Enrolments scanned earlier in this visit, so the count carries on. */
  arrived: string[];
}

type Answer = { kind: 'ok' | 'again' | 'error'; text: string };

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
  private readonly camera = new QrCamera();

  constructor() {
    this.showCount();
    inject(DestroyRef).onDestroy(() => this.camera.stop());
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
    const problem = await this.camera.start(() => this.video().nativeElement, (value) => this.handle(value));
    if (problem) this.cameraProblem.set(problem);
  }

  private async handle(value: string): Promise<void> {
    if (!value.startsWith(CARD_PREFIX)) {
      this.say('error', this.i18n.translate('error.card_unknown'), [200]);
      return;
    }

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
    }
  }

  private say(kind: Answer['kind'], text: string, buzz: number[]): void {
    this.answer.set({ kind, text });
    navigator.vibrate?.(buzz);
  }
}
