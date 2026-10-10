import { booleanAttribute, ChangeDetectionStrategy, Component, computed, DoCheck, effect, inject, input, signal, viewChild } from '@angular/core';
import { ControlValueAccessor, NgControl } from '@angular/forms';
import { ErrorStateMatcher } from '@angular/material/core';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatChipGrid, MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInput, MatInputModule } from '@angular/material/input';
import { TranslatePipe } from '../core/i18n/translate.pipe';
import { fieldErrorKey } from './forms';

export interface SelectOption {
  value: string;
  label: string;
}

/**
 * A dropdown you can type into: typing filters the list, so a long list (districts, subjects) does not
 * need scrolling. Single choice stores the option value; with `multiple` it stores an array and shows
 * the choices as chips. Use it with formControlName like any other field.
 */
@Component({
  selector: 'app-search-select',
  // Default change detection: the error state follows the outer control, which can be touched from outside.
  changeDetection: ChangeDetectionStrategy.Default,
  imports: [MatAutocompleteModule, MatChipsModule, MatFormFieldModule, MatIconModule, MatInputModule, TranslatePipe],
  template: `
    <mat-form-field>
      <mat-label>{{ label() }}</mat-label>
      @if (multiple()) {
        <mat-chip-grid #grid [required]="required()" [errorStateMatcher]="matcher" [attr.aria-label]="label()">
          @for (item of selectedOptions(); track item.value) {
            <mat-chip-row (removed)="remove(item.value)">
              {{ item.label }}
              <button matChipRemove type="button" [attr.aria-label]="'common.remove' | t">
                <mat-icon>cancel</mat-icon>
              </button>
            </mat-chip-row>
          }
        </mat-chip-grid>
        <input
          [matChipInputFor]="grid"
          [matAutocomplete]="auto"
          [value]="text()"
          [placeholder]="selected().length ? '' : ('common.type_to_search' | t)"
          (input)="onType($event)"
          (focus)="onFocus()"
          (blur)="onBlur()"
        />
      } @else {
        <input
          matInput
          [matAutocomplete]="auto"
          [value]="text()"
          [required]="required()"
          [errorStateMatcher]="matcher"
          [placeholder]="'common.type_to_search' | t"
          autocomplete="off"
          (input)="onType($event)"
          (focus)="onFocus()"
          (blur)="onBlur()"
        />
      }
      @if (!multiple()) {
        <mat-icon matSuffix aria-hidden="true">arrow_drop_down</mat-icon>
      }
      <mat-autocomplete #auto="matAutocomplete" [displayWith]="display" [autoActiveFirstOption]="true" (optionSelected)="choose($event.option.value)">
        @for (option of filtered(); track option.value) {
          <mat-option [value]="option.value">{{ option.label }}</mat-option>
        } @empty {
          <mat-option disabled>{{ 'common.no_matches' | t }}</mat-option>
        }
      </mat-autocomplete>
      <mat-error>{{ errorKey() | t }}</mat-error>
    </mat-form-field>
  `,
  styles: `
    :host { display: block; }
  `,
})
export class SearchSelect implements ControlValueAccessor, DoCheck {
  readonly label = input.required<string>();
  readonly options = input.required<SelectOption[]>();
  readonly multiple = input(false, { transform: booleanAttribute });
  readonly required = input(false, { transform: booleanAttribute });

  private readonly textInput = viewChild(MatInput);
  private readonly chipGrid = viewChild(MatChipGrid);
  private readonly control = inject(NgControl, { self: true, optional: true });

  protected readonly selected = signal<string[]>([]);
  protected readonly text = signal('');
  private readonly query = signal('');
  private focused = false;
  private onChange: (value: string | string[]) => void = () => undefined;
  private onTouched: () => void = () => undefined;

  protected readonly selectedOptions = computed(() =>
    this.selected()
      .map((value) => this.options().find((o) => o.value === value))
      .filter((o): o is SelectOption => !!o),
  );

  protected readonly filtered = computed(() => {
    const query = this.query().trim().toLowerCase();
    const taken = this.multiple() ? this.selected() : [];
    return this.options().filter((o) => !taken.includes(o.value) && (!query || o.label.toLowerCase().includes(query)));
  });

  /** Tells the form field to show its error once the outer control is touched and invalid. */
  protected readonly matcher: ErrorStateMatcher = {
    isErrorState: () => !!this.control?.control && this.control.control.invalid && this.control.control.touched,
  };

  /** How the autocomplete writes a chosen value into the box: its label, or nothing for chips. */
  protected readonly display = (value: string): string =>
    this.multiple() ? '' : (this.options().find((o) => o.value === value)?.label ?? '');

  constructor() {
    if (this.control) this.control.valueAccessor = this;
    // The options often arrive after the value (they load from the server): show the label once they do.
    effect(() => {
      this.options();
      if (!this.multiple() && !this.focused) this.text.set(this.display(this.selected()[0] ?? ''));
    });
  }

  /** The inner box has no form control of its own, so refresh its error state from the outer control. */
  ngDoCheck(): void {
    this.textInput()?.updateErrorState();
    this.chipGrid()?.updateErrorState();
  }

  protected errorKey(): string {
    return this.control?.control ? fieldErrorKey(this.control.control) : '';
  }

  writeValue(value: string | string[] | null): void {
    const list = Array.isArray(value) ? value : value ? [value] : [];
    this.selected.set(list);
    if (!this.multiple()) this.text.set(this.display(list[0] ?? ''));
  }

  registerOnChange(fn: (value: string | string[]) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  protected onFocus(): void {
    this.focused = true;
    this.query.set('');
  }

  protected onType(event: Event): void {
    const text = (event.target as HTMLInputElement).value;
    this.text.set(text);
    this.query.set(text);
    if (!this.multiple() && text === '' && this.selected().length > 0) this.commit([]);
  }

  /** Leaving the box drops half-typed text and shows the real choice again. */
  protected onBlur(): void {
    this.focused = false;
    this.onTouched();
    this.query.set('');
    this.text.set(this.multiple() ? '' : this.display(this.selected()[0] ?? ''));
  }

  protected choose(value: string): void {
    this.query.set('');
    if (this.multiple()) {
      this.text.set('');
      this.commit([...this.selected(), value]);
    } else {
      this.text.set(this.display(value));
      this.commit([value]);
    }
  }

  protected remove(value: string): void {
    this.commit(this.selected().filter((v) => v !== value));
  }

  private commit(list: string[]): void {
    this.selected.set(list);
    this.onChange(this.multiple() ? list : (list[0] ?? ''));
  }
}
