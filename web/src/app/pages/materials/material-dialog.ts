import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { errorCode, inlineError } from '../../core/api/problem';
import {
  MATERIAL_ACCEPT,
  MATERIAL_EXTENSIONS,
  MATERIAL_KINDS,
  MATERIAL_MAX_BYTES,
  Material,
  MaterialFolder,
  MaterialKind,
  MaterialsService,
} from '../../core/api/materials.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { TranslateService } from '../../core/i18n/translate.service';
import { applyServerError, fieldErrorKey, filled } from '../../shared/forms';
import { formatSize, materialIcon } from '../../shared/material-view';
import { SearchSelect, SelectOption } from '../../shared/search-select';
import { SubmitButton } from '../../shared/submit-button';
import { folderLabel } from './folder-tree';

export interface MaterialDialogData {
  classes: SelectOption[];
  /** The class the page is showing, to start on. */
  classId?: string;
  /** The shelf and folder the page is showing, to start in. */
  kind?: MaterialKind;
  folderId?: string | null;
  /** Every folder the teacher has, to choose where the file goes. */
  folders: MaterialFolder[];
  /** The material being changed; missing when adding a file. */
  material?: Material;
  /** Topics already used, offered as suggestions so spelling stays the same. */
  topics: string[];
}

/** Add a file to a class, or change a file's title, kind, topic and year. Closes with the saved material. */
@Component({
  selector: 'app-material-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule, SearchSelect, SubmitButton, MatAutocompleteModule, MatButtonModule, MatDialogModule, MatFormFieldModule, MatIconModule, MatInputModule,
    MatSelectModule, TranslatePipe,
  ],
  template: `
    <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
      <h2 mat-dialog-title>{{ (editing ? 'materials.edit' : 'materials.add') | t }}</h2>
      <mat-dialog-content>
        @if (error()) {
          <p class="banner" role="alert">{{ error() | t }}</p>
        }
        <div class="form-grid">
          @if (!editing) {
            <app-search-select class="wide" formControlName="classId" [label]="'attendance.class' | t" [options]="data.classes" [required]="true" />
            <div class="wide picker">
              <input #picker type="file" hidden [accept]="accept" (change)="choose(picker)" />
              <button mat-stroked-button type="button" (click)="picker.click()">
                <mat-icon>upload_file</mat-icon>
                {{ (file() ? 'materials.change_file' : 'materials.choose_file') | t }}
              </button>
              @if (file(); as f) {
                <div class="chosen">
                  <mat-icon aria-hidden="true">{{ icon(f.type) }}</mat-icon>
                  <span class="name">{{ f.name }}</span>
                  <span class="muted">{{ size(f.size) }}</span>
                </div>
              } @else {
                <p class="muted hint">{{ 'materials.file_hint' | t }}</p>
              }
              @if (fileError()) {
                <p class="file-error" role="alert">{{ fileError() | t }}</p>
              }
            </div>
          }
          <mat-form-field class="wide">
            <mat-label>{{ 'materials.name' | t }}</mat-label>
            <input matInput formControlName="title" maxlength="120" autocomplete="off" required />
            <mat-error>{{ key('title') | t }}</mat-error>
          </mat-form-field>
          <mat-form-field class="wide">
            <mat-label>{{ 'materials.kind' | t }}</mat-label>
            <mat-select formControlName="kind">
              @for (k of kinds; track k) {
                <mat-option [value]="k">{{ 'materials.kind.' + k | t }}</mat-option>
              }
            </mat-select>
          </mat-form-field>
          <app-search-select class="wide" formControlName="folderId" [label]="'materials.folder' | t" [options]="folderOptions()" />
          <mat-form-field>
            <mat-label>{{ 'materials.topic' | t }}</mat-label>
            <input matInput formControlName="topic" maxlength="80" autocomplete="off" [matAutocomplete]="topics" />
            <mat-autocomplete #topics="matAutocomplete">
              @for (t of topicSuggestions(); track t) {
                <mat-option [value]="t">{{ t }}</mat-option>
              }
            </mat-autocomplete>
            <mat-error>{{ key('topic') | t }}</mat-error>
          </mat-form-field>
          <mat-form-field>
            <mat-label>{{ 'materials.year' | t }}</mat-label>
            <input matInput type="number" inputmode="numeric" formControlName="year" min="1990" max="2100" step="1" />
            <mat-hint>{{ 'materials.year_hint' | t }}</mat-hint>
            <mat-error>{{ key('year') | t }}</mat-error>
          </mat-form-field>
        </div>
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button mat-button type="button" mat-dialog-close>{{ 'common.cancel' | t }}</button>
        <app-submit-button [label]="editing ? 'common.save' : 'materials.upload'" [busy]="busy()" />
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    .banner { margin: 0 0 1rem; padding: 0.75rem 1rem; border-radius: var(--mat-sys-corner-medium); background: var(--mat-sys-error-container); color: var(--mat-sys-on-error-container); }
    .wide { grid-column: 1 / -1; }
    .picker { display: grid; justify-items: start; gap: 0.5rem; }
    .chosen { display: flex; gap: 0.5rem; align-items: center; max-width: 100%; }
    .chosen mat-icon { flex: none; width: 1.25rem; height: 1.25rem; font-size: 1.25rem; color: var(--mat-sys-primary); }
    .name { min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .hint, .file-error { margin: 0; font: var(--mat-sys-body-small); }
    .file-error { color: var(--mat-sys-error); }
  `,
})
export class MaterialDialog {
  private readonly api = inject(MaterialsService);
  private readonly ref = inject<MatDialogRef<MaterialDialog, Material>>(MatDialogRef);
  private readonly i18n = inject(TranslateService);
  protected readonly data = inject<MaterialDialogData>(MAT_DIALOG_DATA);

  protected readonly editing = !!this.data.material;
  protected readonly kinds = MATERIAL_KINDS;
  protected readonly accept = MATERIAL_ACCEPT;
  protected readonly icon = materialIcon;
  protected readonly size = formatSize;
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly file = signal<File | null>(null);
  protected readonly fileError = signal('');

  protected readonly form = inject(FormBuilder).nonNullable.group({
    classId: [this.data.classId ?? '', this.editing ? [] : [Validators.required]],
    title: [this.data.material?.title ?? '', [filled, Validators.maxLength(120)]],
    kind: [this.data.material?.kind ?? this.data.kind ?? ('PastPaper' as MaterialKind)],
    folderId: [this.data.material?.folderId ?? this.data.folderId ?? ''],
    topic: [this.data.material?.topic ?? '', Validators.maxLength(80)],
    year: [this.data.material?.year ?? (null as number | null), [Validators.min(1990), Validators.max(2100)]],
  });

  private readonly chosenClass = toSignal(this.form.controls.classId.valueChanges, { initialValue: this.form.controls.classId.value });
  private readonly chosenKind = toSignal(this.form.controls.kind.valueChanges, { initialValue: this.form.controls.kind.value });

  /** The folders of the chosen class and shelf, named by where they sit; "no folder" puts the file on the shelf itself. */
  protected readonly folderOptions = computed<SelectOption[]>(() => {
    const classId = this.data.material?.classId ?? this.chosenClass();
    const mine = this.data.folders.filter((f) => f.classId === classId && f.kind === this.chosenKind());
    return [
      { value: '', label: this.i18n.translate('materials.no_folder') },
      ...mine.map((f) => ({ value: f.id, label: folderLabel(f.id, mine) })).sort((a, b) => a.label.localeCompare(b.label)),
    ];
  });

  private readonly typedTopic = toSignal(this.form.controls.topic.valueChanges, { initialValue: this.form.controls.topic.value });

  /** Topics already used that contain what is typed, so the same topic is spelt the same way each time. */
  protected readonly topicSuggestions = computed(() => {
    const typed = this.typedTopic().trim().toLowerCase();
    return this.data.topics.filter((t) => t.toLowerCase().includes(typed) && t.toLowerCase() !== typed);
  });

  constructor() {
    // A folder belongs to one class and shelf, so changing either clears a folder that no longer fits.
    effect(() => {
      const options = this.folderOptions();
      untracked(() => {
        if (!options.some((o) => o.value === this.form.controls.folderId.value)) this.form.controls.folderId.setValue('');
      });
    });
  }

  protected key(name: string): string {
    const control = this.form.get(name);
    return control && control.touched ? fieldErrorKey(control) : '';
  }

  protected choose(input: HTMLInputElement): void {
    const picked = input.files?.[0] ?? null;
    input.value = '';
    if (!picked) return;
    const extension = picked.name.split('.').pop()?.toLowerCase() ?? '';
    if (!(MATERIAL_EXTENSIONS as readonly string[]).includes(extension) || !picked.name.includes('.')) {
      this.fileError.set('materials.file_type');
      return;
    }
    if (picked.size > MATERIAL_MAX_BYTES) {
      this.fileError.set('materials.file_too_large');
      return;
    }
    if (picked.size === 0) {
      this.fileError.set('error.material_file_required');
      return;
    }
    this.fileError.set('');
    this.file.set(picked);
    // The file's own name is a fair first title.
    if (this.form.controls.title.value.trim() === '') {
      this.form.controls.title.setValue(picked.name.replace(/\.[^.]+$/, '').slice(0, 120));
    }
  }

  protected submit(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    const file = this.file();
    if (!this.editing && !file) {
      this.fileError.set('error.material_file_required');
      return;
    }

    this.busy.set(true);
    this.error.set('');
    const value = this.form.getRawValue();
    const topic = value.topic.trim();
    const request = this.data.material
      ? this.api.update(this.data.material.id, { title: value.title.trim(), kind: value.kind, topic: topic || null, year: value.year, folderId: value.folderId || null })
      : this.api.add(this.upload(file!, value, topic));
    request.subscribe({
      next: (saved) => this.ref.close(saved),
      error: (e) => {
        this.busy.set(false);
        if (!applyServerError(this.form, errorCode(e))) this.error.set(inlineError(e));
      },
    });
  }

  private upload(file: File, value: { classId: string; title: string; kind: MaterialKind; year: number | null; folderId: string }, topic: string): FormData {
    const form = new FormData();
    form.append('classId', value.classId);
    form.append('title', value.title.trim());
    form.append('kind', value.kind);
    if (value.folderId) form.append('folderId', value.folderId);
    if (topic) form.append('topic', topic);
    if (value.year !== null) form.append('year', String(value.year));
    form.append('file', file, file.name);
    return form;
  }
}
