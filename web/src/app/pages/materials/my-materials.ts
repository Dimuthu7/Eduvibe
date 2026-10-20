import { ChangeDetectionStrategy, Component, computed, input, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MATERIAL_KINDS, MaterialsService, StudentMaterial } from '../../core/api/materials.service';
import { errorCode, errorKey } from '../../core/api/problem';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { TranslateService } from '../../core/i18n/translate.service';
import { Loadable } from '../../core/state/loadable';
import { ToastService } from '../../core/ui/toast.service';
import { formatSize, materialIcon, openMaterial } from '../../shared/material-view';

/** Files that sit together: the same shelf and the same folders. */
interface Section {
  label: string;
  items: StudentMaterial[];
}

interface ClassMaterials {
  classId: string;
  title: string;
  teacher: string | null;
  /** Sections get a heading only when the teacher has organised the class into folders or shelves. */
  sections: Section[];
  headings: boolean;
}

/** Why a file will not open, in words a student can act on. The teacher's Drive trouble is not the student's to fix. */
const UNAVAILABLE = new Set(['drive_reconnect', 'drive_not_connected', 'drive_unavailable']);

/** A student's notes and past papers, class by class. Files open through the app; nothing in the teacher's Drive is shared. */
@Component({
  selector: 'app-my-materials',
  host: { class: 'page page-narrow' },
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, MatButtonModule, MatChipsModule, MatIconModule, MatProgressSpinnerModule, TranslatePipe],
  template: `
    <a mat-button routerLink="/welcome" class="back">
      <mat-icon>arrow_back</mat-icon>
      {{ 'welcome.my_classes' | t }}
    </a>
    <header class="page-header">
      <div>
        <h1 class="page-title">{{ 'materials.my_title' | t }}</h1>
        <p class="page-subtitle">{{ 'materials.my_subtitle' | t }}</p>
      </div>
    </header>

    @switch (data.status()) {
      @case ('ready') {
        @if (kindsPresent().length > 1) {
          <mat-chip-listbox class="kinds" [value]="kind()" (change)="kind.set($event.value ?? 'all')" [attr.aria-label]="'materials.kind' | t">
            <mat-chip-option value="all">{{ 'materials.all_kinds' | t }}</mat-chip-option>
            @for (k of kindsPresent(); track k) {
              <mat-chip-option [value]="k">{{ 'materials.kind.' + k | t }}</mat-chip-option>
            }
          </mat-chip-listbox>
        }
        @for (group of groups(); track group.classId) {
          <h2 class="section-title" [id]="'class-' + group.classId">{{ group.title }}</h2>
          @if (group.teacher) {
            <p class="muted teacher">{{ group.teacher }}</p>
          }
          @for (section of group.sections; track section.label) {
            @if (group.headings) {
              <h3 class="section-label">{{ section.label }}</h3>
            }
            <div class="panel">
              @for (item of section.items; track item.material.id) {
                <button type="button" class="panel-row open" (click)="open(item)" [disabled]="busyId() === item.material.id">
                  <span class="avatar" aria-hidden="true">
                    @if (busyId() === item.material.id) {
                      <mat-spinner diameter="20" />
                    } @else {
                      <mat-icon>{{ icon(item.material.contentType) }}</mat-icon>
                    }
                  </span>
                  <span class="row-main">
                    <span class="row-title">{{ item.material.title }}</span>
                    <span class="row-meta">{{ meta(item, group.headings) }}</span>
                  </span>
                  <mat-icon class="chevron" aria-hidden="true">{{ item.material.canPreview ? 'open_in_new' : 'download' }}</mat-icon>
                </button>
              }
            </div>
          }
        } @empty {
          <div class="empty">
            <mat-icon>folder_open</mat-icon>
            <p>{{ 'materials.my_none' | t }}</p>
          </div>
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
        <div class="skeleton"></div>
      }
    }
  `,
  styles: `
    .back { margin-left: -0.75rem; margin-bottom: 0.5rem; }
    .teacher { margin: -0.5rem 0 0.5rem; font: var(--mat-sys-body-small); }
    .kinds { display: block; margin-bottom: 0.5rem; }
    .panel { margin-bottom: 1rem; }
    .section-label { margin: 0 0 0.375rem; color: var(--mat-sys-on-surface-variant); font: var(--mat-sys-label-large); overflow-wrap: anywhere; }
    .open { width: 100%; border: 0; background: none; color: inherit; font: inherit; text-align: left; cursor: pointer; }
    .open:hover:not(:disabled) { background: var(--mat-sys-surface-container); }
    .open .row-main { display: grid; }
    .avatar mat-icon { width: 1.375rem; height: 1.375rem; font-size: 1.375rem; }
    .chevron { flex: none; color: var(--mat-sys-on-surface-variant); }
  `,
})
export class MyMaterials {
  /** Optional class to list first, from the ?classId= query parameter. */
  readonly classId = input<string>('');

  private readonly api = inject(MaterialsService);
  private readonly toast = inject(ToastService);
  private readonly i18n = inject(TranslateService);

  protected readonly data = new Loadable<StudentMaterial[]>();
  protected readonly kind = signal<string>('all');
  protected readonly icon = materialIcon;
  protected readonly busyId = signal<string | null>(null);

  protected readonly kindsPresent = computed(() => MATERIAL_KINDS.filter((k) => (this.data.data() ?? []).some((i) => i.material.kind === k)));

  /** Classes with files, the class asked for first. Inside a class the files sit under their shelf and folders, as the teacher arranged them. */
  protected readonly groups = computed<ClassMaterials[]>(() => {
    const groups = new Map<string, { classId: string; title: string; teacher: string | null; items: StudentMaterial[] }>();
    for (const item of this.data.data() ?? []) {
      if (this.kind() !== 'all' && item.material.kind !== this.kind()) continue;
      const group = groups.get(item.material.classId) ?? { classId: item.material.classId, title: item.classTitle, teacher: item.teacherName, items: [] };
      group.items.push(item);
      groups.set(item.material.classId, group);
    }
    const all = [...groups.values()].map((g) => this.arranged(g)).sort((a, b) => a.title.localeCompare(b.title));
    const first = this.classId();
    return first ? [...all.filter((g) => g.classId === first), ...all.filter((g) => g.classId !== first)] : all;
  });

  constructor() {
    this.load();
  }

  protected load(): void {
    this.data.load(this.api.mine());
  }

  /** Splits a class's files into sections by shelf and folders, shelves in their usual order and top-level files before folders. */
  private arranged(group: { classId: string; title: string; teacher: string | null; items: StudentMaterial[] }): ClassMaterials {
    const sections = new Map<string, Section & { order: number }>();
    for (const item of group.items) {
      const shelf = this.i18n.translate(`materials.kind.${item.material.kind}`);
      const label = [shelf, ...item.folderPath].join(' / ');
      const section = sections.get(label) ?? { label, items: [], order: MATERIAL_KINDS.indexOf(item.material.kind) };
      section.items.push(item);
      sections.set(label, section);
    }
    const ordered = [...sections.values()].sort((a, b) => a.order - b.order || a.label.localeCompare(b.label));
    return {
      classId: group.classId,
      title: group.title,
      teacher: group.teacher,
      sections: ordered.map(({ label, items }) => ({ label, items })),
      headings: ordered.length > 1 || group.items.some((i) => i.folderPath.length > 0),
    };
  }

  /** The line under a title: the shelf (unless the section heading already says it), topic, year and size. */
  protected meta(item: StudentMaterial, headings: boolean): string {
    const m = item.material;
    const shelf = headings ? [] : [this.i18n.translate(`materials.kind.${m.kind}`)];
    return [...shelf, m.topic, m.year, formatSize(m.sizeBytes)].filter((p) => p !== null && p !== '').join(' · ');
  }

  protected open(item: StudentMaterial): void {
    if (this.busyId()) return;
    const m = item.material;
    this.busyId.set(m.id);
    openMaterial((download) => this.api.myFile(m.id, download), m.fileName, m.canPreview, (error) => {
      this.busyId.set(null);
      if (!error) return;
      const code = errorCode(error);
      this.toast.error(code && UNAVAILABLE.has(code) ? 'materials.unavailable' : errorKey(error));
    });
  }
}
