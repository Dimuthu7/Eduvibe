import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { ClassesService } from '../../core/api/classes.service';
import { DriveStatus, MATERIAL_KINDS, Material, MaterialsService } from '../../core/api/materials.service';
import { PlansService } from '../../core/api/plans.service';
import { errorCode, errorKey } from '../../core/api/problem';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { TranslateService } from '../../core/i18n/translate.service';
import { Loadable } from '../../core/state/loadable';
import { ToastService } from '../../core/ui/toast.service';
import { ConfirmDialog } from '../../shared/confirm-dialog';
import { formatSize, materialIcon, openMaterial } from '../../shared/material-view';
import { SearchSelect, SelectOption } from '../../shared/search-select';
import { MaterialDialog, MaterialDialogData } from './material-dialog';

const ALL = 'all';

/** What Google's return trip to the app can report, as the message to show. */
const OUTCOME_MESSAGES: Record<string, { key: string; ok: boolean }> = {
  connected: { key: 'materials.drive_connected', ok: true },
  denied: { key: 'materials.outcome_denied', ok: false },
  permission: { key: 'materials.outcome_permission', ok: false },
  locked: { key: 'error.plan_feature_locked', ok: false },
  failed: { key: 'materials.outcome_failed', ok: false },
};

/**
 * A teacher's notes and past papers. The files live in the teacher's own Google Drive; this screen connects the Drive, adds files to a
 * class, and lists them by class, kind, topic and year. A teacher whose plan lacks the feature sees what it would give instead.
 */
@Component({
  selector: 'app-materials',
  host: { class: 'page' },
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule, SearchSelect, MatButtonModule, MatChipsModule, MatIconModule, MatMenuModule, MatProgressSpinnerModule, TranslatePipe,
  ],
  template: `
    <header class="page-header">
      <div>
        <h1 class="page-title">{{ 'materials.title' | t }}</h1>
        <p class="page-subtitle">{{ 'materials.subtitle' | t }}</p>
      </div>
      @if (status()?.connected && !locked()) {
        <button mat-flat-button type="button" (click)="add()" [disabled]="status()?.needsReconnect">
          <mat-icon>add</mat-icon>
          {{ 'materials.add' | t }}
        </button>
      }
    </header>

    @if (locked()) {
      <div class="notice locked">
        <mat-icon aria-hidden="true">lock</mat-icon>
        <div>
          <strong>{{ 'materials.locked_title' | t }}</strong>
          <p>
            {{ 'materials.locked_message' | t }}
            @if (availableOn()) {
              {{ 'my_plan.available_on' | t }} <strong>{{ availableOn() }}</strong>.
            }
          </p>
        </div>
      </div>
    } @else {
      @switch (drive.status()) {
        @case ('ready') {
          @if (status(); as s) {
            @if (!s.configured) {
              <div class="notice">
                <mat-icon aria-hidden="true">cloud_off</mat-icon>
                <div>
                  <strong>{{ 'materials.not_configured_title' | t }}</strong>
                  <p>{{ 'materials.not_configured_message' | t }}</p>
                </div>
              </div>
            } @else if (!s.connected) {
              <div class="notice">
                <mat-icon aria-hidden="true">add_to_drive</mat-icon>
                <div>
                  <strong>{{ 'materials.connect_title' | t }}</strong>
                  <p>{{ 'materials.connect_message' | t }}</p>
                  <button mat-flat-button type="button" (click)="connect()" [disabled]="connecting()">{{ 'materials.connect' | t }}</button>
                </div>
              </div>
            } @else if (s.needsReconnect) {
              <div class="notice warn">
                <mat-icon aria-hidden="true">sync_problem</mat-icon>
                <div>
                  <strong>{{ 'materials.reconnect_title' | t }}</strong>
                  <p>{{ 'materials.reconnect_message' | t }}</p>
                  <button mat-flat-button type="button" (click)="connect()" [disabled]="connecting()">{{ 'materials.reconnect' | t }}</button>
                </div>
              </div>
            } @else {
              <div class="connected">
                <mat-icon aria-hidden="true">add_to_drive</mat-icon>
                <span class="who">{{ 'materials.connected_as' | t }} <strong>{{ s.email ?? '' }}</strong></span>
                <button mat-button type="button" (click)="disconnect()">{{ 'materials.disconnect' | t }}</button>
              </div>
            }
          }
        }
        @case ('error') {
          <div class="empty">
            <p>{{ 'error.load_failed' | t }}</p>
            <button mat-stroked-button type="button" (click)="load()">{{ 'status.retry' | t }}</button>
          </div>
        }
        @default {
          <div class="skeleton"></div>
        }
      }

      @if (drive.status() === 'ready') {
        <div class="filters">
          <app-search-select [formControl]="classControl" [label]="'attendance.class' | t" [options]="classChoices()" />
          @if (topicChoices().length > 2) {
            <app-search-select [formControl]="topicControl" [label]="'materials.topic' | t" [options]="topicChoices()" />
          }
          @if (yearChoices().length > 2) {
            <app-search-select [formControl]="yearControl" [label]="'materials.year' | t" [options]="yearChoices()" />
          }
        </div>
        <mat-chip-listbox class="kinds" [value]="kind()" (change)="kind.set($event.value ?? 'all')" [attr.aria-label]="'materials.kind' | t">
          <mat-chip-option value="all">{{ 'materials.all_kinds' | t }}</mat-chip-option>
          @for (k of kinds; track k) {
            <mat-chip-option [value]="k">{{ 'materials.kind.' + k | t }}</mat-chip-option>
          }
        </mat-chip-listbox>

        @switch (materials.status()) {
          @case ('ready') {
            @if (shown().length === 0) {
              <div class="empty">
                <mat-icon>folder_open</mat-icon>
                <p>{{ (all().length === 0 ? 'materials.none' : 'materials.none_matching') | t }}</p>
                @if (all().length === 0 && status()?.connected && !status()?.needsReconnect) {
                  <button mat-stroked-button type="button" (click)="add()">{{ 'materials.add' | t }}</button>
                }
              </div>
            } @else {
              <div class="panel">
                @for (m of shown(); track m.id) {
                  <div class="panel-row">
                    <button type="button" class="open" (click)="open(m)" [disabled]="busyId() === m.id">
                      <span class="avatar" aria-hidden="true">
                        @if (busyId() === m.id) {
                          <mat-spinner diameter="20" />
                        } @else {
                          <mat-icon>{{ icon(m.contentType) }}</mat-icon>
                        }
                      </span>
                      <span class="row-main">
                        <span class="row-title">{{ m.title }}</span>
                        <span class="row-meta">{{ classTitle(m.classId) }} · {{ 'materials.kind.' + m.kind | t }}</span>
                        <span class="row-meta">{{ details(m) }}</span>
                      </span>
                    </button>
                    <button mat-icon-button type="button" [matMenuTriggerFor]="menu" [attr.aria-label]="'common.actions' | t">
                      <mat-icon>more_vert</mat-icon>
                    </button>
                    <mat-menu #menu="matMenu">
                      <button mat-menu-item type="button" (click)="open(m)">
                        <mat-icon>{{ m.canPreview ? 'open_in_new' : 'download' }}</mat-icon>
                        <span>{{ (m.canPreview ? 'materials.open' : 'materials.download') | t }}</span>
                      </button>
                      @if (m.canPreview) {
                        <button mat-menu-item type="button" (click)="open(m, true)">
                          <mat-icon>download</mat-icon>
                          <span>{{ 'materials.download' | t }}</span>
                        </button>
                      }
                      <button mat-menu-item type="button" (click)="edit(m)">
                        <mat-icon>edit</mat-icon>
                        <span>{{ 'materials.edit_action' | t }}</span>
                      </button>
                      <button mat-menu-item type="button" (click)="remove(m)">
                        <mat-icon>delete</mat-icon>
                        <span>{{ 'materials.remove' | t }}</span>
                      </button>
                    </mat-menu>
                  </div>
                }
              </div>
            }
          }
          @case ('error') {
            <div class="empty">
              <p>{{ 'error.load_failed' | t }}</p>
              <button mat-stroked-button type="button" (click)="loadMaterials()">{{ 'status.retry' | t }}</button>
            </div>
          }
          @default {
            <div class="skeleton"></div>
            <div class="skeleton"></div>
          }
        }
      }
    }
  `,
  styles: `
    .notice { display: flex; gap: 1rem; align-items: flex-start; margin-bottom: 1rem; padding: 1rem; border-radius: var(--mat-sys-corner-large); background: var(--mat-sys-surface-container); }
    .notice > mat-icon { flex: none; width: 2rem; height: 2rem; font-size: 2rem; color: var(--mat-sys-primary); }
    .notice p { margin: 0.25rem 0 0.75rem; color: var(--mat-sys-on-surface-variant); }
    .notice.locked p { margin-bottom: 0; }
    .notice.warn { background: var(--mat-sys-tertiary-container); color: var(--mat-sys-on-tertiary-container); }
    .notice.warn > mat-icon, .notice.warn p { color: inherit; }
    .connected { display: flex; flex-wrap: wrap; gap: 0.25rem 0.5rem; align-items: center; margin-bottom: 0.5rem; color: var(--mat-sys-on-surface-variant); }
    .connected mat-icon { width: 1.25rem; height: 1.25rem; font-size: 1.25rem; color: var(--mat-sys-primary); }
    .who { flex: 1 1 12rem; min-width: 0; overflow-wrap: anywhere; }
    .who strong { color: var(--mat-sys-on-surface); }
    .filters { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); column-gap: 1rem; }
    .filters > :first-child { grid-column: 1 / -1; }
    .kinds { display: block; margin-bottom: 1rem; }
    .open { display: flex; flex: 1; gap: 0.875rem; align-items: center; min-width: 0; margin: -0.875rem 0 -0.875rem -1rem; padding: 0.875rem 0 0.875rem 1rem; border: 0; background: none; color: inherit; font: inherit; text-align: left; cursor: pointer; }
    .open:hover:not(:disabled) { background: var(--mat-sys-surface-container); }
    .open .row-main { display: grid; }
    .avatar mat-icon { width: 1.375rem; height: 1.375rem; font-size: 1.375rem; }
  `,
})
export class Materials {
  /** Where Google's return trip ended, from the ?drive= query parameter. */
  readonly outcome = input<string>('', { alias: 'drive' });
  /** Optional class to start on, from the ?classId= query parameter. */
  readonly classId = input<string>('');

  private readonly api = inject(MaterialsService);
  private readonly classesApi = inject(ClassesService);
  private readonly plans = inject(PlansService);
  private readonly dialog = inject(MatDialog);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);
  private readonly i18n = inject(TranslateService);

  protected readonly kinds = MATERIAL_KINDS;
  protected readonly icon = materialIcon;
  protected readonly drive = new Loadable<DriveStatus>();
  protected readonly status = this.drive.data;
  protected readonly locked = signal(false);
  protected readonly availableOn = signal<string | null>(null);
  protected readonly materials = new Loadable<Material[]>();
  protected readonly all = computed(() => this.materials.data() ?? []);
  protected readonly connecting = signal(false);
  protected readonly busyId = signal<string | null>(null);

  private readonly classOptions = signal<SelectOption[]>([]);
  protected readonly classControl = new FormControl(ALL, { nonNullable: true });
  protected readonly topicControl = new FormControl(ALL, { nonNullable: true });
  protected readonly yearControl = new FormControl(ALL, { nonNullable: true });
  protected readonly kind = signal<string>('all');
  private readonly chosenClass = toSignal(this.classControl.valueChanges, { initialValue: ALL });
  private readonly chosenTopic = toSignal(this.topicControl.valueChanges, { initialValue: ALL });
  private readonly chosenYear = toSignal(this.yearControl.valueChanges, { initialValue: ALL });

  protected readonly classChoices = computed<SelectOption[]>(() => [{ value: ALL, label: this.i18n.translate('fees.all_classes') }, ...this.classOptions()]);

  /** Topics and years in the files of the chosen class and kind, so a filter only offers what would find something. */
  private readonly inScope = computed(() =>
    this.all().filter((m) => (this.chosenClass() === ALL || m.classId === this.chosenClass()) && (this.kind() === 'all' || m.kind === this.kind())),
  );
  protected readonly topicChoices = computed<SelectOption[]>(() => [
    { value: ALL, label: this.i18n.translate('materials.all_topics') },
    ...[...new Set(this.inScope().map((m) => m.topic).filter((t): t is string => !!t))].sort((a, b) => a.localeCompare(b)).map((t) => ({ value: t, label: t })),
  ]);
  protected readonly yearChoices = computed<SelectOption[]>(() => [
    { value: ALL, label: this.i18n.translate('materials.all_years') },
    ...[...new Set(this.inScope().map((m) => m.year).filter((y): y is number => y !== null))].sort((a, b) => b - a).map((y) => ({ value: String(y), label: String(y) })),
  ]);

  protected readonly shown = computed(() =>
    this.inScope().filter(
      (m) => (this.chosenTopic() === ALL || m.topic === this.chosenTopic()) && (this.chosenYear() === ALL || String(m.year) === this.chosenYear()),
    ),
  );

  constructor() {
    this.classesApi.classes('all').subscribe((list) => {
      this.classOptions.set(list.map((c) => ({ value: c.id, label: c.title })));
      if (this.classId() && list.some((c) => c.id === this.classId())) this.classControl.setValue(this.classId());
    });

    // Google's return trip lands here with the result in the address; say it once and tidy the address.
    effect(() => {
      const outcome = OUTCOME_MESSAGES[this.outcome()];
      if (!outcome) return;
      untracked(() => {
        if (outcome.ok) this.toast.success(outcome.key);
        else this.toast.error(outcome.key);
        void this.router.navigate([], { queryParams: { drive: null }, queryParamsHandling: 'merge', replaceUrl: true });
      });
    });

    // A topic or year that the chosen class or kind no longer has would hide everything, so the filter goes back to "all".
    effect(() => {
      const topics = this.topicChoices();
      const years = this.yearChoices();
      untracked(() => {
        if (!topics.some((o) => o.value === this.topicControl.value)) this.topicControl.setValue(ALL);
        if (!years.some((o) => o.value === this.yearControl.value)) this.yearControl.setValue(ALL);
      });
    });

    this.load();
  }

  protected load(): void {
    this.locked.set(false);
    this.drive.status.set('loading');
    this.api.driveStatus().subscribe({
      next: (status) => {
        this.drive.data.set(status);
        this.drive.status.set('ready');
        this.loadMaterials();
      },
      error: (e) => {
        if (errorCode(e) === 'plan_feature_locked') {
          this.locked.set(true);
          this.drive.status.set('ready');
          this.plans.mine().subscribe({
            next: (plan) => this.availableOn.set(plan.features.find((f) => f.key === 'drive_materials')?.availableOn ?? null),
            error: () => undefined,
          });
        } else {
          this.drive.error.set(e);
          this.drive.status.set('error');
          this.toast.error(errorKey(e));
        }
      },
    });
  }

  protected loadMaterials(): void {
    this.materials.load(this.api.list());
  }

  protected classTitle(id: string): string {
    return this.classOptions().find((c) => c.value === id)?.label ?? '';
  }

  protected details(m: Material): string {
    return [m.topic, m.year, formatSize(m.sizeBytes)].filter((p) => p !== null && p !== '').join(' · ');
  }

  protected connect(): void {
    this.connecting.set(true);
    this.api.connect().subscribe({
      next: ({ url }) => window.location.assign(url),
      error: (e) => {
        this.connecting.set(false);
        this.toast.error(errorKey(e));
      },
    });
  }

  protected disconnect(): void {
    this.dialog
      .open<ConfirmDialog, { title: string; message: string; confirm: string }, boolean>(ConfirmDialog, {
        data: { title: 'materials.disconnect_title', message: 'materials.disconnect_message', confirm: 'materials.disconnect' },
        maxWidth: 'calc(100vw - 1rem)',
      })
      .afterClosed()
      .subscribe((yes) => {
        if (!yes) return;
        this.api.disconnect().subscribe(() => {
          this.drive.update((s) => ({ ...s, connected: false, email: null, needsReconnect: false }));
          this.toast.success('materials.drive_disconnected');
        });
      });
  }

  protected add(): void {
    const chosen = this.classControl.value;
    this.openDialog({ classes: this.classOptions(), classId: chosen === ALL ? undefined : chosen, topics: this.knownTopics() }, (saved) => {
      this.materials.update((all) => [saved, ...all]);
      this.toast.success('materials.added');
    });
  }

  protected edit(material: Material): void {
    this.openDialog({ classes: this.classOptions(), material, topics: this.knownTopics() }, (saved) => {
      this.materials.update((all) => all.map((m) => (m.id === saved.id ? saved : m)));
      this.toast.success('materials.saved');
    });
  }

  protected remove(material: Material): void {
    this.dialog
      .open<ConfirmDialog, { title: string; message: string; confirm: string }, boolean>(ConfirmDialog, {
        data: { title: 'materials.remove_title', message: 'materials.remove_message', confirm: 'materials.remove' },
        maxWidth: 'calc(100vw - 1rem)',
      })
      .afterClosed()
      .subscribe((yes) => {
        if (!yes) return;
        this.api.remove(material.id).subscribe(() => {
          this.materials.update((all) => all.filter((m) => m.id !== material.id));
          this.toast.success('materials.removed');
        });
      });
  }

  protected open(material: Material, download = false): void {
    if (this.busyId()) return;
    this.busyId.set(material.id);
    openMaterial((asDownload) => this.api.file(material.id, asDownload), material.fileName, material.canPreview && !download, (error) => {
      this.busyId.set(null);
      if (!error) return;
      const code = errorCode(error);
      this.toast.error(errorKey(error));
      // Google stopped accepting the link: show the reconnect notice at once rather than on the next visit.
      if (code === 'drive_reconnect') this.drive.update((s) => ({ ...s, needsReconnect: true }));
    });
  }

  private openDialog(data: MaterialDialogData, saved: (material: Material) => void): void {
    this.dialog
      .open<MaterialDialog, MaterialDialogData, Material>(MaterialDialog, { data, width: '40rem', maxWidth: 'calc(100vw - 1rem)' })
      .afterClosed()
      .subscribe((result) => {
        if (result) saved(result);
      });
  }

  private knownTopics(): string[] {
    return [...new Set(this.all().map((m) => m.topic).filter((t): t is string => !!t))].sort((a, b) => a.localeCompare(b));
  }
}
