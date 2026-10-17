import { ChangeDetectionStrategy, Component, computed, inject, input, output, viewChild } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule, MatMenuTrigger } from '@angular/material/menu';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { ToastService } from '../../core/ui/toast.service';
import { ConfirmData, ConfirmDialog } from '../../shared/confirm-dialog';
import { StudentAvatar } from '../../shared/student-avatar';
import { StudentPhotos } from '../../shared/student-photos';
import { PhotoDialog, PhotoDialogData } from './photo-dialog';

/**
 * A student's big avatar with a camera on its corner. Tapping it offers to take a photo with the phone's camera, pick one from the
 * gallery, or remove the current one. A new picture is framed in a round window first (PhotoDialog), then saved.
 */
@Component({
  selector: 'app-photo-picker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatIconModule, MatMenuModule, StudentAvatar, TranslatePipe],
  template: `
    <button type="button" class="face" [matMenuTriggerFor]="menu" [attr.aria-label]="(has() ? 'students.photo.change' : 'students.photo.add') | t">
      <app-student-avatar [name]="name()" [of]="studentId()" size="lg" />
      <span class="camera" aria-hidden="true"><mat-icon>photo_camera</mat-icon></span>
    </button>
    <mat-menu #menu="matMenu">
      @if (canCapture) {
        <button mat-menu-item type="button" (click)="camera.click()">
          <mat-icon>photo_camera</mat-icon>
          <span>{{ 'students.photo.take' | t }}</span>
        </button>
      }
      <button mat-menu-item type="button" (click)="gallery.click()">
        <mat-icon>photo_library</mat-icon>
        <span>{{ 'students.photo.choose' | t }}</span>
      </button>
      @if (has()) {
        <button mat-menu-item type="button" (click)="confirmRemove()">
          <mat-icon>delete</mat-icon>
          <span>{{ 'students.photo.remove' | t }}</span>
        </button>
      }
    </mat-menu>
    <input #camera type="file" accept="image/*" capture="user" hidden (change)="picked($event)" />
    <input #gallery type="file" accept="image/*" hidden (change)="picked($event)" />
  `,
  styles: `
    :host { display: inline-block; }
    .face { position: relative; display: block; padding: 0; border: 0; border-radius: 50%; background: none; cursor: pointer; -webkit-tap-highlight-color: transparent; }
    .face:focus-visible { outline: 2px solid var(--mat-sys-primary); outline-offset: 3px; }
    .camera { position: absolute; right: -0.125rem; bottom: -0.125rem; display: grid; place-items: center; width: 2rem; height: 2rem; border-radius: 50%; background: var(--mat-sys-primary); color: var(--mat-sys-on-primary); box-shadow: 0 0 0 2px var(--mat-sys-surface); }
    .camera mat-icon { width: 1.125rem; height: 1.125rem; font-size: 1.125rem; }
  `,
})
export class PhotoPicker {
  readonly studentId = input.required<string>();
  readonly name = input.required<string>();
  /** Fires after a photo was saved or removed. */
  readonly changed = output<void>();

  private readonly photos = inject(StudentPhotos);
  private readonly dialog = inject(MatDialog);
  private readonly toast = inject(ToastService);
  private readonly trigger = viewChild.required(MatMenuTrigger);

  protected readonly has = computed(() => this.photos.entryOf(this.studentId()) !== undefined);
  /** "Take a photo" only on a touch screen; on a computer it would just open the file chooser twice. */
  protected readonly canCapture = typeof matchMedia === 'function' && matchMedia('(pointer: coarse)').matches;

  /** Opens the menu, for a text button elsewhere on the page. */
  open(): void {
    this.trigger().openMenu();
  }

  protected picked(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) return;
    this.dialog
      .open<PhotoDialog, PhotoDialogData, boolean>(PhotoDialog, {
        data: { studentId: this.studentId(), file },
        width: '24rem',
        maxWidth: 'calc(100vw - 1rem)',
      })
      .afterClosed()
      .subscribe((saved) => {
        if (!saved) return;
        this.toast.success('students.photo.saved');
        this.changed.emit();
      });
  }

  protected confirmRemove(): void {
    this.dialog
      .open<ConfirmDialog, ConfirmData, boolean>(ConfirmDialog, {
        data: { title: 'students.photo.remove', message: 'students.photo.remove_confirm', confirm: 'students.photo.remove' },
        maxWidth: 'calc(100vw - 1rem)',
      })
      .afterClosed()
      .subscribe((yes) => {
        if (!yes) return;
        void this.photos.remove(this.studentId()).then(() => {
          this.toast.success('students.photo.removed');
          this.changed.emit();
        });
      });
  }
}
