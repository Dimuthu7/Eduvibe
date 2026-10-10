import { ChangeDetectionStrategy, Component, inject, input, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatTabsModule } from '@angular/material/tabs';
import { CatalogItem, CatalogKind, CatalogService, CatalogStore } from '../../../core/api/catalog.service';
import { errorCode, inlineError } from '../../../core/api/problem';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { Loadable } from '../../../core/state/loadable';
import { ToastService } from '../../../core/ui/toast.service';
import { applyServerError, fieldErrorKey, filled } from '../../../shared/forms';
import { SubmitButton } from '../../../shared/submit-button';

/** One editable list (streams or subjects): add a name, hide or show an existing one. */
@Component({
  selector: 'app-catalog-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    SubmitButton,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatSlideToggleModule,
    TranslatePipe,
  ],
  template: `
    <form class="add" [formGroup]="form" (ngSubmit)="add()" novalidate>
      <mat-form-field subscriptSizing="dynamic">
        <mat-label>{{ (kind() === 'streams' ? 'catalog.new_stream' : 'catalog.new_subject') | t }}</mat-label>
        <input matInput formControlName="name" autocomplete="off" maxlength="80" />
        <mat-error>{{ key() | t }}</mat-error>
      </mat-form-field>
      <app-submit-button label="catalog.add" [busy]="busy()" />
    </form>
    @if (error()) {
      <p class="field-error" role="alert">{{ error() | t }}</p>
    }

    @switch (items.status()) {
      @case ('ready') {
        <p class="muted hint">{{ 'catalog.hint' | t }}</p>
        <div class="panel">
          @for (item of items.data(); track item.id) {
            <div class="panel-row" [class.hidden-item]="!item.isActive">
              <span class="row-main row-title">{{ item.name }}</span>
              <mat-slide-toggle [checked]="item.isActive" (change)="setActive(item, $event.checked)">
                {{ 'catalog.in_use' | t }}
              </mat-slide-toggle>
            </div>
          } @empty {
            <div class="empty"><p>{{ 'catalog.none' | t }}</p></div>
          }
        </div>
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
    :host { display: block; padding-top: 1rem; }
    .add { display: flex; gap: 0.75rem; align-items: flex-start; margin-bottom: 0.5rem; }
    .add mat-form-field { flex: 1; max-width: 24rem; }
    .add app-submit-button { display: contents; }
    .add ::ng-deep button { height: 3.5rem; }
    .hint { margin: 0.5rem 0; }
    .hidden-item .row-title { opacity: 0.55; text-decoration: line-through; }
  `,
})
export class CatalogList implements OnInit {
  readonly kind = input.required<CatalogKind>();

  private readonly api = inject(CatalogService);
  private readonly store = inject(CatalogStore);
  private readonly toast = inject(ToastService);

  protected readonly items = new Loadable<CatalogItem[]>();
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly form = inject(FormBuilder).nonNullable.group({
    name: ['', [filled, Validators.maxLength(80)]],
  });

  // The kind is an input, so it is only known from ngOnInit on.
  ngOnInit(): void {
    this.load();
  }

  protected key(): string {
    const control = this.form.controls.name;
    return control.touched ? fieldErrorKey(control) : '';
  }

  protected load(): void {
    this.items.load(this.api.items(this.kind(), true));
  }

  protected add(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    this.busy.set(true);
    this.error.set('');
    this.api.add(this.kind(), this.form.controls.name.value.trim()).subscribe({
      next: (item) => {
        this.items.update((all) => [...all, item]);
        this.form.reset();
        this.busy.set(false);
        this.toast.success('catalog.added');
        this.store.ensureLoaded(true);
      },
      error: (e) => {
        this.busy.set(false);
        if (!applyServerError(this.form, errorCode(e))) this.error.set(inlineError(e));
      },
    });
  }

  protected setActive(item: CatalogItem, isActive: boolean): void {
    this.api.update(this.kind(), item.id, { isActive }).subscribe({
      next: (saved) => {
        this.items.update((all) => all.map((i) => (i.id === saved.id ? saved : i)));
        this.store.ensureLoaded(true);
      },
      error: () => this.items.update((all) => [...all]),
    });
  }
}

/** Super Admin: the education streams and subjects teachers choose from. */
@Component({
  selector: 'app-admin-catalog',
  host: { class: 'page' },
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatTabsModule, CatalogList, TranslatePipe],
  template: `
    <header class="page-header">
      <div>
        <h1 class="page-title">{{ 'catalog.title' | t }}</h1>
        <p class="page-subtitle">{{ 'catalog.subtitle' | t }}</p>
      </div>
    </header>
    <mat-tab-group animationDuration="0ms" mat-stretch-tabs="false">
      <mat-tab [label]="'catalog.streams' | t">
        <ng-template matTabContent><app-catalog-list kind="streams" /></ng-template>
      </mat-tab>
      <mat-tab [label]="'catalog.subjects' | t">
        <ng-template matTabContent><app-catalog-list kind="subjects" /></ng-template>
      </mat-tab>
    </mat-tab-group>
  `,
})
export class AdminCatalog {}
