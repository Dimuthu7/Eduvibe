import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { tap } from 'rxjs';
import { FeesService, ReminderSettings } from '../../core/api/fees.service';
import { errorCode, inlineError } from '../../core/api/problem';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { TranslateService } from '../../core/i18n/translate.service';
import { Loadable } from '../../core/state/loadable';
import { applyServerError, fieldErrorKey } from '../../shared/forms';
import { SubmitButton } from '../../shared/submit-button';

/** The weekly choices: 0 stands for "until paid"; the server accepts 1 to 8. */
export const WEEKLY_CHOICES: readonly number[] = [0, 1, 2, 3, 4, 5, 6, 7, 8];

export interface RemindersDialogData {
  classId: string;
  classTitle: string;
}

/** A class's automatic fee reminders: on or off, and how many weekly reminders follow the due date. Opened from the class menu. */
@Component({
  selector: 'app-reminders-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, SubmitButton, MatButtonModule, MatDialogModule, MatFormFieldModule, MatSelectModule, MatSlideToggleModule, TranslatePipe],
  template: `
    <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
      <h2 mat-dialog-title>{{ 'reminders.title' | t }}</h2>
      <mat-dialog-content>
        @switch (settings.status()) {
          @case ('ready') {
            <p class="intro">{{ 'reminders.intro' | t }}</p>
            @if (error()) {
              <p class="banner" role="alert">{{ error() | t }}</p>
            }
            <mat-slide-toggle formControlName="enabled">{{ 'reminders.enabled' | t }}</mat-slide-toggle>
            <mat-form-field class="weekly">
              <mat-label>{{ 'reminders.weekly' | t }}</mat-label>
              <mat-select formControlName="weeklyLimit">
                @for (n of choices; track n) {
                  <mat-option [value]="n">{{ choiceText(n) }}</mat-option>
                }
              </mat-select>
              <mat-hint>{{ 'reminders.weekly_hint' | t }}</mat-hint>
              <mat-error>{{ key('weeklyLimit') | t }}</mat-error>
            </mat-form-field>
            @if (skipped() > 0) {
              <p class="note">{{ 'reminders.skipped' | t }}</p>
            }
          }
          @case ('error') {
            <p>{{ 'error.load_failed' | t }}</p>
          }
          @default {
            <div class="skeleton"></div>
          }
        }
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button mat-button type="button" mat-dialog-close>{{ 'common.cancel' | t }}</button>
        <app-submit-button label="common.save" [busy]="busy()" [disabled]="settings.status() !== 'ready'" />
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    .intro, .note { margin: 0 0 1rem; color: var(--mat-sys-on-surface-variant); }
    .note { margin: 1rem 0 0; }
    .banner { margin: 0 0 1rem; padding: 0.75rem 1rem; border-radius: var(--mat-sys-corner-medium); background: var(--mat-sys-error-container); color: var(--mat-sys-on-error-container); }
    .weekly { display: block; margin-top: 1.25rem; }
  `,
})
export class RemindersDialog {
  private readonly api = inject(FeesService);
  private readonly i18n = inject(TranslateService);
  private readonly ref = inject<MatDialogRef<RemindersDialog, boolean>>(MatDialogRef);
  protected readonly data = inject<RemindersDialogData>(MAT_DIALOG_DATA);

  protected readonly choices = WEEKLY_CHOICES;
  protected readonly settings = new Loadable<ReminderSettings>();
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly skipped = signal(0);
  private readonly fb = inject(FormBuilder).nonNullable;
  protected readonly form = this.fb.group({ enabled: true, weeklyLimit: 0 });

  constructor() {
    const source = this.api.reminderSettings(this.data.classId).pipe(
      tap((loaded) => {
        this.form.patchValue({ enabled: loaded.enabled, weeklyLimit: loaded.weeklyLimit ?? 0 });
        this.skipped.set(loaded.skippedEnrolments.length);
      }),
    );
    this.settings.load(source);
  }

  protected choiceText(n: number): string {
    return n === 0 ? this.i18n.translate('reminders.until_paid') : this.i18n.format(n === 1 ? 'reminders.weeks_one' : 'reminders.weeks_many', { count: n });
  }

  protected key(name: string): string {
    const control = this.form.get(name);
    return control && control.touched ? fieldErrorKey(control) : '';
  }

  protected submit(): void {
    this.busy.set(true);
    this.error.set('');
    const { enabled, weeklyLimit } = this.form.getRawValue();
    this.api.saveReminderSettings(this.data.classId, { enabled, weeklyLimit: weeklyLimit === 0 ? null : weeklyLimit }).subscribe({
      next: () => this.ref.close(true),
      error: (e) => {
        this.busy.set(false);
        if (!applyServerError(this.form, errorCode(e))) this.error.set(inlineError(e));
      },
    });
  }
}

/** Opens a class's reminder settings. Emits true once saved, or nothing if cancelled. */
export function openReminders(dialog: MatDialog, data: RemindersDialogData) {
  return dialog.open<RemindersDialog, RemindersDialogData, boolean>(RemindersDialog, { data, width: '28rem', maxWidth: 'calc(100vw - 1rem)', autoFocus: false }).afterClosed();
}
