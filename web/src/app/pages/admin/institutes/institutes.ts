import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { forkJoin } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatDialog } from '@angular/material/dialog';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatIconModule } from '@angular/material/icon';
import { AdminService, Institute, TeacherSummary } from '../../../core/api/admin.service';
import { CatalogStore } from '../../../core/api/catalog.service';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { Loadable } from '../../../core/state/loadable';
import { ToastService } from '../../../core/ui/toast.service';
import { InstituteDialog } from './institute-dialog';

interface Page {
  institutes: Institute[];
  teachers: TeacherSummary[];
}

/** Super Admin: institutes in one list, a pop-up to add or edit, and which teachers work at each. */
@Component({
  selector: 'app-admin-institutes',
  host: { class: 'page' },
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatCheckboxModule, MatExpansionModule, MatIconModule, TranslatePipe],
  template: `
    <header class="page-header">
      <div>
        <h1 class="page-title">{{ 'institutes.title' | t }}</h1>
        @if (page.status() === 'ready') {
          <p class="page-subtitle">{{ 'institutes.count' | t }}: {{ institutes().length }}</p>
        }
      </div>
      <button mat-flat-button type="button" (click)="open()">
        <mat-icon>add</mat-icon>
        {{ 'institutes.add' | t }}
      </button>
    </header>

    @switch (page.status()) {
      @case ('ready') {
        @if (institutes().length > 0) {
          <mat-accordion displayMode="flat">
            @for (institute of institutes(); track institute.id) {
              <mat-expansion-panel>
                <mat-expansion-panel-header>
                  <mat-panel-title>
                    <span class="avatar" aria-hidden="true"><mat-icon>apartment</mat-icon></span>
                    <span class="titles">
                      <span class="row-title">{{ institute.name }}</span>
                      <span class="row-meta">
                        {{ institute.district }}@if (institute.town) { · {{ institute.town }} }
                        · {{ institute.teacherIds.length }} {{ 'institutes.teachers_count' | t }}
                      </span>
                    </span>
                  </mat-panel-title>
                </mat-expansion-panel-header>
                @if (institute.address || institute.phone) {
                  <p class="muted">{{ institute.address }}@if (institute.address && institute.phone) { · }{{ institute.phone }}</p>
                }
                <h3 class="section-title">{{ 'institutes.teachers' | t }}</h3>
                <div class="teachers">
                  @for (teacher of activeTeachers(); track teacher.id) {
                    <mat-checkbox
                      [checked]="institute.teacherIds.includes(teacher.id)"
                      (change)="toggleTeacher(institute, teacher.id, $event.checked)"
                    >
                      {{ teacher.fullName }}
                    </mat-checkbox>
                  } @empty {
                    <p class="muted">{{ 'teachers.none' | t }}</p>
                  }
                </div>
                <mat-action-row>
                  <button mat-button type="button" (click)="open(institute)">
                    <mat-icon>edit</mat-icon>
                    {{ 'institutes.edit' | t }}
                  </button>
                </mat-action-row>
              </mat-expansion-panel>
            }
          </mat-accordion>
        } @else {
          <div class="empty">
            <mat-icon>apartment</mat-icon>
            <p>{{ 'institutes.none' | t }}</p>
            <button mat-stroked-button type="button" (click)="open()">{{ 'institutes.add' | t }}</button>
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
    mat-panel-title { align-items: center; gap: 0.875rem; }
    .titles { display: flex; flex-direction: column; min-width: 0; }
    .teachers { display: grid; grid-template-columns: repeat(auto-fill, minmax(14rem, 1fr)); }
  `,
})
export class AdminInstitutes {
  private readonly api = inject(AdminService);
  private readonly dialog = inject(MatDialog);
  private readonly toast = inject(ToastService);
  private readonly catalog = inject(CatalogStore);

  protected readonly page = new Loadable<Page>();
  protected readonly institutes = computed(() => this.page.data()?.institutes ?? []);
  protected readonly activeTeachers = computed(() => (this.page.data()?.teachers ?? []).filter((t) => t.isActive));

  constructor() {
    this.catalog.ensureLoaded();
    this.load();
  }

  protected load(): void {
    this.page.load(forkJoin({ institutes: this.api.institutes(), teachers: this.api.teachers() }));
  }

  protected open(institute?: Institute): void {
    this.dialog
      .open<InstituteDialog, Institute | null, Institute>(InstituteDialog, {
        data: institute ?? null,
        width: '40rem',
        maxWidth: 'calc(100vw - 1rem)',
        autoFocus: 'first-tabbable',
      })
      .afterClosed()
      .subscribe((saved) => {
        if (!saved) return;
        this.page.update((p) => ({
          ...p,
          institutes: institute
            ? p.institutes.map((r) => (r.id === saved.id ? saved : r))
            : [...p.institutes, saved].sort((a, b) => a.name.localeCompare(b.name)),
        }));
        this.toast.success('institutes.saved');
      });
  }

  protected toggleTeacher(institute: Institute, teacherId: string, on: boolean): void {
    const teacherIds = on
      ? [...institute.teacherIds, teacherId]
      : institute.teacherIds.filter((id) => id !== teacherId);
    this.api.setInstituteTeachers(institute.id, teacherIds).subscribe({
      next: () =>
        this.page.update((p) => ({
          ...p,
          institutes: p.institutes.map((r) => (r.id === institute.id ? { ...r, teacherIds } : r)),
        })),
      // Put the checkbox back to what the server has.
      error: () => this.page.update((p) => ({ ...p })),
    });
  }
}
