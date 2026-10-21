import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { firstValueFrom } from 'rxjs';
import { StudentCard, StudentsService } from '../core/api/students.service';
import { AuthService } from '../core/auth/auth.service';
import { TranslatePipe } from '../core/i18n/translate.pipe';
import { TranslateService } from '../core/i18n/translate.service';
import { ToastService } from '../core/ui/toast.service';
import { drawCardSheet } from './card-pdf';
import { saveBlob } from './download';
import { pdfFromPages } from './pdf';
import { shareOrSave } from './receipt';

/** Which cards to print: every active student of a class, or one student (with the class they are printed for). */
export interface CardsSpec {
  classId: string;
  classTitle: string;
  studentId?: string;
  /** The name of the PDF without its extension. */
  fileName: string;
}

/**
 * Makes the PDF of student cards as it opens, so sharing or saving is one tap afterwards (a phone only opens its share sheet straight
 * after a tap, not after a wait).
 */
@Component({
  selector: 'app-cards-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatDialogModule, MatIconModule, MatProgressSpinnerModule, TranslatePipe],
  template: `
    <h2 mat-dialog-title>{{ 'cards.title' | t }}</h2>
    <mat-dialog-content>
      <p class="intro">{{ 'cards.intro' | t }}</p>
      @switch (stage()) {
        @case ('preparing') {
          <div class="busy"><mat-spinner diameter="24" /> {{ 'cards.preparing' | t }}</div>
        }
        @case ('empty') {
          <p class="muted">{{ 'cards.no_students' | t }}</p>
        }
        @case ('failed') {
          <p role="alert" class="failed">{{ 'cards.failed' | t }}</p>
        }
        @default {
          <p class="muted">{{ countText() }}</p>
        }
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button type="button" mat-dialog-close>{{ 'common.close' | t }}</button>
      @if (stage() === 'failed') {
        <button mat-button type="button" (click)="prepare()">{{ 'status.retry' | t }}</button>
      }
      @if (canShare) {
        <button mat-flat-button type="button" [disabled]="stage() !== 'ready'" (click)="share()">
          <mat-icon>ios_share</mat-icon>
          {{ 'cards.share' | t }}
        </button>
        <button mat-stroked-button type="button" [disabled]="stage() !== 'ready'" (click)="save()">{{ 'cards.save' | t }}</button>
      } @else {
        <button mat-flat-button type="button" [disabled]="stage() !== 'ready'" (click)="save()">
          <mat-icon>download</mat-icon>
          {{ 'cards.save' | t }}
        </button>
      }
    </mat-dialog-actions>
  `,
  styles: `
    .intro { margin: 0 0 1rem; color: var(--mat-sys-on-surface-variant); }
    .busy { display: flex; gap: 0.75rem; align-items: center; }
    .failed { color: var(--mat-sys-error); }
    .muted { margin: 0; color: var(--mat-sys-on-surface-variant); }
    mat-dialog-actions { flex-wrap: wrap; gap: 0.5rem; }
    mat-icon { font-size: 1.125rem; width: 1.125rem; height: 1.125rem; }
  `,
})
export class CardsDialog implements OnInit {
  private readonly spec = inject<CardsSpec>(MAT_DIALOG_DATA);
  private readonly students = inject(StudentsService);
  private readonly auth = inject(AuthService);
  private readonly i18n = inject(TranslateService);
  private readonly toast = inject(ToastService);

  protected readonly stage = signal<'preparing' | 'ready' | 'empty' | 'failed'>('preparing');
  protected readonly countText = signal('');
  protected readonly canShare = typeof navigator.canShare === 'function';
  private pdf: Blob | null = null;

  ngOnInit(): void {
    void this.prepare();
  }

  protected async prepare(): Promise<void> {
    this.stage.set('preparing');
    try {
      const cards: StudentCard[] = this.spec.studentId
        ? [await firstValueFrom(this.students.card(this.spec.studentId, this.spec.classId))]
        : await firstValueFrom(this.students.cards(this.spec.classId));
      this.countText.set(this.i18n.format('cards.count', { count: cards.length }));
      if (cards.length === 0) {
        this.stage.set('empty');
        return;
      }

      const user = this.auth.user();
      const pages = await drawCardSheet(
        { classTitle: this.spec.classTitle, teacher: `${user?.firstName ?? ''} ${user?.lastName ?? ''}`.trim(), cards },
        { number: (n) => this.i18n.format('cards.pdf_number', { n }), scan: this.i18n.translate('cards.pdf_scan') },
      );
      this.pdf = pdfFromPages(pages);
      this.stage.set('ready');
    } catch {
      this.stage.set('failed');
    }
  }

  protected async share(): Promise<void> {
    if (!this.pdf) return;
    const result = await shareOrSave(this.pdf, `${this.spec.fileName}.pdf`, this.i18n.translate('cards.title'));
    if (result === 'saved') this.toast.success('cards.saved');
  }

  protected save(): void {
    if (!this.pdf) return;
    saveBlob(this.pdf, `${this.spec.fileName}.pdf`);
    this.toast.success('cards.saved');
  }
}
