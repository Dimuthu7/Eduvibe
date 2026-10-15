import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { MatBottomSheetRef, MAT_BOTTOM_SHEET_DATA } from '@angular/material/bottom-sheet';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { ReportKey, ReportsService } from '../core/api/reports.service';
import { AuthService } from '../core/auth/auth.service';
import { TranslatePipe } from '../core/i18n/translate.pipe';
import { TranslateService } from '../core/i18n/translate.service';
import { ToastService } from '../core/ui/toast.service';
import { saveBlob } from './download';
import { todayColombo, formatDate } from './format';
import { shareOrSave } from './receipt';
import { drawTablePdf } from './table-pdf';

/** What a screen tells the sheet: which report, for what, and what to call the files. */
export interface ExportSpec {
  key: ReportKey;
  params: Record<string, string>;
  /** The heading of the PDF and the name of the files (without the extension). */
  title: string;
  /** Short lines under the heading, such as "Class: Maths" and "Month: October 2026". */
  facts: string[];
  fileName: string;
}

/** PDF first (share to WhatsApp and the like, or save), Excel as the second choice. The PDF is made as the sheet opens, so sharing is one tap. */
@Component({
  selector: 'app-export-sheet',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatIconModule, MatProgressSpinnerModule, TranslatePipe],
  template: `
    <h2 class="sheet-title">{{ 'export.title' | t }}</h2>
    <p class="sheet-subtitle">{{ subtitle }}</p>

    @if (failed()) {
      <div class="sheet-error" role="alert">
        <span>{{ 'export.failed' | t }}</span>
        <button mat-button type="button" (click)="prepare()">{{ 'status.retry' | t }}</button>
      </div>
    }

    <div class="options">
      @if (canShare) {
        <button class="option" type="button" (click)="share()" [disabled]="!pdf()">
          <mat-icon>ios_share</mat-icon>
          <span class="option-text">
            <span class="option-title">{{ 'export.share_pdf' | t }}</span>
            <span class="option-hint">{{ (pdf() ? 'export.share_hint' : 'export.preparing') | t }}</span>
          </span>
        </button>
      }
      <button class="option" type="button" (click)="savePdf()" [disabled]="!pdf()">
        <mat-icon>picture_as_pdf</mat-icon>
        <span class="option-text">
          <span class="option-title">{{ 'export.download_pdf' | t }}</span>
          @if (!canShare) {
            <span class="option-hint">{{ (pdf() ? 'export.download_hint' : 'export.preparing') | t }}</span>
          }
        </span>
      </button>
      <button class="option" type="button" (click)="saveExcel()" [disabled]="excelBusy()">
        <mat-icon>table_view</mat-icon>
        <span class="option-text">
          <span class="option-title">{{ 'export.download_excel' | t }}</span>
          <span class="option-hint">{{ 'export.excel_hint' | t }}</span>
        </span>
      </button>
    </div>
    @if (!pdf() && !failed()) {
      <div class="sheet-busy"><mat-spinner diameter="20" /> {{ 'export.preparing' | t }}</div>
    }
  `,
  styles: `
    :host { display: block; }
    .sheet-title { font: var(--mat-sys-title-large); margin: 0.25rem 0 0; }
    .sheet-subtitle { font: var(--mat-sys-body-medium); color: var(--mat-sys-on-surface-variant); margin: 0.25rem 0 0.5rem; overflow-wrap: anywhere; }
    .sheet-error { display: flex; align-items: center; justify-content: space-between; gap: 0.5rem; padding: 0.5rem 0.75rem; margin-bottom: 0.5rem; border-radius: var(--mat-sys-corner-medium); background: var(--mat-sys-error-container); color: var(--mat-sys-on-error-container); font: var(--mat-sys-body-medium); }
    .options { display: grid; gap: 0.25rem; }
    .option { display: flex; align-items: center; gap: 1rem; width: 100%; min-height: 3.5rem; padding: 0.5rem 1rem; border: 0; border-radius: var(--mat-sys-corner-large); background: transparent; color: var(--mat-sys-on-surface); font: inherit; text-align: start; cursor: pointer; }
    .option:hover:not(:disabled), .option:focus-visible { background: var(--mat-sys-surface-container-high); outline: none; }
    .option:disabled { opacity: 0.5; cursor: default; }
    .option mat-icon { flex: none; color: var(--mat-sys-primary); }
    .option-text { display: grid; }
    .option-title { font: var(--mat-sys-title-medium); }
    .option-hint { font: var(--mat-sys-body-small); color: var(--mat-sys-on-surface-variant); }
    .sheet-busy { display: flex; align-items: center; gap: 0.75rem; padding: 0.5rem 1rem; font: var(--mat-sys-body-medium); color: var(--mat-sys-on-surface-variant); }
  `,
})
export class ExportSheet implements OnInit {
  protected readonly spec = inject<ExportSpec>(MAT_BOTTOM_SHEET_DATA);
  /** "Fees · 2026 O/L Science · October 2026": what is about to be exported. */
  protected readonly subtitle = [this.spec.title, ...this.spec.facts.map((f) => f.slice(f.indexOf(': ') + 2))].join(' · ');

  private readonly ref = inject(MatBottomSheetRef<ExportSheet>);
  private readonly reports = inject(ReportsService);
  private readonly auth = inject(AuthService);
  private readonly i18n = inject(TranslateService);
  private readonly toast = inject(ToastService);

  protected readonly pdf = signal<Blob | null>(null);
  protected readonly failed = signal(false);
  protected readonly excelBusy = signal(false);
  /** Sharing a file needs a phone (or a browser) that can; elsewhere only the downloads are offered. */
  protected readonly canShare = canShareFiles();

  ngOnInit(): void {
    void this.prepare();
  }

  /** Fetches the report and draws the PDF. Done up front because the phone only opens its share sheet straight after a tap, not after a wait. */
  protected async prepare(): Promise<void> {
    this.failed.set(false);
    this.pdf.set(null);
    try {
      const table = await firstValueFrom(this.reports.table(this.spec.key, this.spec.params));
      const user = this.auth.user();
      this.pdf.set(
        await drawTablePdf(
          {
            title: this.spec.title,
            facts: this.spec.facts,
            teacher: `${user?.firstName ?? ''} ${user?.lastName ?? ''}`.trim(),
            headers: table.headers,
            rows: table.rows,
            madeOn: formatDate(todayColombo()),
          },
          { page: this.t('attendance.page'), of: this.t('attendance.of'), madeOn: this.t('attendance.made_on'), none: this.t('export.no_rows') },
        ),
      );
    } catch (error) {
      // The HTTP error interceptor has already told the person why; the sheet only offers another go.
      this.failed.set(true);
      // Anything else is a bug (for example the page could not be drawn): the global handler should hear about it.
      if (!(error instanceof HttpErrorResponse)) throw error;
    }
  }

  protected async share(): Promise<void> {
    const pdf = this.pdf();
    if (!pdf) return;
    const result = await shareOrSave(pdf, `${this.spec.fileName}.pdf`, this.spec.title);
    if (result === 'saved') this.toast.success('export.saved');
    this.ref.dismiss();
  }

  protected savePdf(): void {
    const pdf = this.pdf();
    if (!pdf) return;
    saveBlob(pdf, `${this.spec.fileName}.pdf`);
    this.toast.success('export.saved');
    this.ref.dismiss();
  }

  protected saveExcel(): void {
    this.excelBusy.set(true);
    this.reports.download(this.spec.key, this.spec.params).subscribe({
      next: (file) => {
        saveBlob(file, `${this.spec.fileName}.xlsx`);
        this.toast.success('export.saved');
        this.ref.dismiss();
      },
      error: () => this.excelBusy.set(false),
    });
  }

  private t(key: string): string {
    return this.i18n.translate(key);
  }
}

function canShareFiles(): boolean {
  if (typeof navigator === 'undefined' || typeof navigator.canShare !== 'function') return false;
  try {
    return navigator.canShare({ files: [new File([''], 'report.pdf', { type: 'application/pdf' })] });
  } catch {
    return false;
  }
}
