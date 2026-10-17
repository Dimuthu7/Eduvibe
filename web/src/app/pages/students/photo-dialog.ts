import { ChangeDetectionStrategy, Component, ElementRef, effect, inject, signal, viewChild } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatSliderModule } from '@angular/material/slider';
import { inlineError } from '../../core/api/problem';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { centred, clampFraming, cropToJpeg, Framing, MAX_ZOOM, panned, sourceSquare, zoomed } from '../../shared/photo-crop';
import { StudentPhotos } from '../../shared/student-photos';
import { SubmitButton } from '../../shared/submit-button';

export interface PhotoDialogData {
  studentId: string;
  /** The picture the teacher took or picked. */
  file: File;
}

/** The round window the picture is seen through, in CSS pixels. What shows in it is exactly what the avatar will show. */
const WINDOW = 256;

type Decoded = CanvasImageSource & { width: number; height: number };

async function decode(file: File): Promise<Decoded | null> {
  try {
    // Honours the camera's rotation, so a portrait photo is not saved on its side.
    if (typeof createImageBitmap === 'function') return await createImageBitmap(file);
  } catch {
    // Fall through to the slower route below.
  }
  const url = URL.createObjectURL(file);
  try {
    const image = new Image();
    image.src = url;
    await image.decode();
    return image;
  } catch {
    return null;
  } finally {
    URL.revokeObjectURL(url);
  }
}

/**
 * Frames a new photo before it is saved: drag to move, pinch or slide to zoom, inside the same circle the avatar uses. Then it is
 * cut to a 320 px square JPEG on the phone and uploaded, so a 5 MB camera picture costs about 30 kB. Closes with true once saved.
 */
@Component({
  selector: 'app-photo-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatDialogModule, MatSliderModule, SubmitButton, TranslatePipe],
  template: `
    <h2 mat-dialog-title>{{ 'students.photo.adjust' | t }}</h2>
    <mat-dialog-content>
      @if (error()) {
        <p class="banner" role="alert">{{ error() | t }}</p>
      }
      <div
        class="window"
        tabindex="0"
        role="img"
        [attr.aria-label]="'students.photo.adjust_hint' | t"
        (pointerdown)="down($event)"
        (pointermove)="move($event)"
        (pointerup)="up($event)"
        (pointercancel)="up($event)"
        (wheel)="wheel($event)"
        (keydown)="key($event)"
      >
        <canvas #canvas [attr.width]="pixels" [attr.height]="pixels"></canvas>
      </div>
      <p class="hint muted">{{ 'students.photo.adjust_hint' | t }}</p>
      <mat-slider min="1" [max]="maxZoom" step="0.01" class="zoom" [disabled]="!source()">
        <input matSliderThumb [value]="framing().zoom" (valueChange)="zoom($event)" [attr.aria-label]="'students.photo.zoom' | t" />
      </mat-slider>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button type="button" mat-dialog-close>{{ 'common.cancel' | t }}</button>
      <app-submit-button label="students.photo.save" [busy]="busy()" [disabled]="!source()" (click)="save()" />
    </mat-dialog-actions>
  `,
  styles: `
    mat-dialog-content { display: grid; justify-items: center; padding-top: 0.5rem; }
    .banner { justify-self: stretch; margin: 0 0 1rem; padding: 0.75rem 1rem; border-radius: var(--mat-sys-corner-medium); background: var(--mat-sys-error-container); color: var(--mat-sys-on-error-container); }
    .window { width: 16rem; height: 16rem; overflow: hidden; border-radius: 50%; background: var(--mat-sys-surface-container-highest); box-shadow: 0 0 0 3px var(--mat-sys-primary); cursor: grab; touch-action: none; outline-offset: 6px; }
    .window:active { cursor: grabbing; }
    canvas { display: block; width: 100%; height: 100%; }
    .hint { margin: 1rem 0 0; text-align: center; }
    .zoom { width: min(16rem, 100%); }
  `,
})
export class PhotoDialog {
  private readonly data = inject<PhotoDialogData>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<PhotoDialog, boolean>>(MatDialogRef);
  private readonly photos = inject(StudentPhotos);
  private readonly canvas = viewChild<ElementRef<HTMLCanvasElement>>('canvas');

  protected readonly maxZoom = MAX_ZOOM;
  protected readonly pixels = WINDOW * Math.min(2, Math.max(1, Math.round(globalThis.devicePixelRatio || 1)));
  protected readonly source = signal<Decoded | null>(null);
  protected readonly framing = signal<Framing>({ zoom: 1, x: 0, y: 0 });
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  private readonly pointers = new Map<number, { x: number; y: number }>();

  constructor() {
    void decode(this.data.file).then((image) => {
      if (!image) {
        this.error.set('students.photo.unreadable');
        return;
      }
      this.framing.set(centred(image.width, image.height));
      this.source.set(image);
    });

    effect(() => {
      const image = this.source();
      const framing = this.framing();
      const canvas = this.canvas()?.nativeElement;
      const context = canvas?.getContext('2d');
      if (!image || !canvas || !context) return;
      const square = sourceSquare(image.width, image.height, WINDOW, framing);
      context.imageSmoothingQuality = 'high';
      context.drawImage(image, square.x, square.y, square.side, square.side, 0, 0, canvas.width, canvas.height);
    });
  }

  protected down(event: PointerEvent): void {
    (event.currentTarget as HTMLElement).setPointerCapture(event.pointerId);
    this.pointers.set(event.pointerId, { x: event.clientX, y: event.clientY });
  }

  protected move(event: PointerEvent): void {
    const image = this.source();
    const before = this.pointers.get(event.pointerId);
    if (!image || !before) return;
    const others = [...this.pointers.entries()].filter(([id]) => id !== event.pointerId).map(([, p]) => p);
    const now = { x: event.clientX, y: event.clientY };
    this.pointers.set(event.pointerId, now);
    if (others.length === 0) {
      this.framing.update((f) => panned(image.width, image.height, WINDOW, f, now.x - before.x, now.y - before.y));
    } else {
      // Two fingers: the distance between them is the zoom.
      const other = others[0];
      const was = Math.hypot(before.x - other.x, before.y - other.y);
      const is = Math.hypot(now.x - other.x, now.y - other.y);
      if (was > 0) this.framing.update((f) => zoomed(image.width, image.height, WINDOW, f, f.zoom * (is / was)));
    }
  }

  protected up(event: PointerEvent): void {
    this.pointers.delete(event.pointerId);
  }

  protected wheel(event: WheelEvent): void {
    event.preventDefault();
    this.zoom(this.framing().zoom * (event.deltaY < 0 ? 1.08 : 1 / 1.08));
  }

  protected key(event: KeyboardEvent): void {
    const image = this.source();
    const step = { ArrowLeft: [24, 0], ArrowRight: [-24, 0], ArrowUp: [0, 24], ArrowDown: [0, -24] }[event.key];
    if (!image || !step) return;
    event.preventDefault();
    this.framing.update((f) => panned(image.width, image.height, WINDOW, f, step[0], step[1]));
  }

  protected zoom(value: number): void {
    const image = this.source();
    if (image) this.framing.update((f) => zoomed(image.width, image.height, WINDOW, f, value));
  }

  protected async save(): Promise<void> {
    const image = this.source();
    if (!image || this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    const framing = clampFraming(image.width, image.height, WINDOW, this.framing());
    const blob = await cropToJpeg(image, WINDOW, framing);
    if (!blob) {
      this.busy.set(false);
      this.error.set('students.photo.unreadable');
      return;
    }
    try {
      await this.photos.upload(this.data.studentId, blob);
      this.ref.close(true);
    } catch (e) {
      this.busy.set(false);
      this.error.set(inlineError(e));
    }
  }
}
