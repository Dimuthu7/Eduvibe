import { ChangeDetectionStrategy, Component, computed, inject, input, linkedSignal, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MessageEvent, SaveRuleInput } from '../../core/api/messaging.service';
import { LANGUAGES } from '../../core/i18n/language';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { TranslateService } from '../../core/i18n/translate.service';
import { fillSample, smsParts } from '../../shared/sms-parts';

/** The shares of classes a teacher can ask for before a parent hears that their child is missing too many. */
export const PERCENT_CHOICES = [50, 60, 70, 75, 80, 90];

/** The icon beside each alert, so the list can be read at a glance. */
const EVENT_ICON: Record<string, string> = {
  fee_before: 'event_upcoming',
  fee_due_today: 'payments',
  fee_overdue: 'warning',
  payment_received: 'receipt_long',
  claim_rejected: 'money_off',
  absent: 'event_busy',
  arrived: 'how_to_reg',
  low_attendance: 'running_with_errors',
  results_published: 'quiz',
};

/**
 * One automatic alert on the Messages page: whether it goes out, how it is sent, the teacher's own line inside the WhatsApp template, and
 * the words of the text message in each language, with a preview of what a parent reads and what it will cost.
 */
@Component({
  selector: 'app-message-event-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    MatButtonModule,
    MatButtonToggleModule,
    MatExpansionModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatSelectModule,
    MatSlideToggleModule,
    TranslatePipe,
  ],
  template: `
    <mat-expansion-panel class="event">
      <mat-expansion-panel-header>
        <mat-panel-title>
          <mat-icon aria-hidden="true">{{ icon() }}</mat-icon>
          {{ 'messages.event.' + item().event | t }}
        </mat-panel-title>
        <mat-panel-description>{{ summary() | t }}</mat-panel-description>
      </mat-expansion-panel-header>

      <p class="muted what">{{ 'messages.about.' + item().event | t }}</p>

      <div class="line">
        <span>{{ 'messages.send_it' | t }}</span>
        <mat-slide-toggle
          [checked]="item().on"
          [disabled]="busy()"
          [aria-label]="'messages.send_it' | t"
          (change)="rule.emit({ on: $event.checked, way: item().way, note: item().note, percent: item().percent })"
        />
      </div>

      @if (item().on) {
        <mat-button-toggle-group
          class="choices"
          [value]="item().way"
          [disabled]="busy()"
          [attr.aria-label]="'messages.way_label' | t"
          hideSingleSelectionIndicator
          (change)="rule.emit({ on: true, way: $event.value, note: item().note, percent: item().percent })"
        >
          @for (choice of item().ways; track choice) {
            <mat-button-toggle [value]="choice">{{ 'messages.way.' + choice | t }}</mat-button-toggle>
          }
        </mat-button-toggle-group>
        <p class="muted hint">{{ 'messages.way_hint' | t }}</p>

        @if (item().percent !== null) {
          <mat-form-field appearance="outline" class="percent">
            <mat-label>{{ 'messages.percent_label' | t }}</mat-label>
            <mat-select
              [value]="item().percent"
              [disabled]="busy()"
              (selectionChange)="rule.emit({ on: true, way: item().way, note: item().note, percent: $event.value })"
            >
              @for (choice of percents; track choice) {
                <mat-option [value]="choice">{{ choice }}%</mat-option>
              }
            </mat-select>
          </mat-form-field>
          <p class="muted hint">{{ percentHint() }}</p>
        }

        @if (usesWhatsApp()) {
          <h3 class="section-title">{{ 'messages.note_title' | t }}</h3>
          <p class="muted hint">{{ 'messages.note_hint' | t }}</p>
          <mat-form-field appearance="outline" class="wide">
            <mat-label>{{ 'messages.note_label' | t }}</mat-label>
            <input matInput [value]="note()" [attr.maxlength]="noteMax()" [disabled]="busy()" (input)="note.set(words($event))" />
            <mat-hint align="end">{{ note().length }} / {{ noteMax() }}</mat-hint>
          </mat-form-field>
          <div class="bubble">
            <p class="said">{{ 'messages.template_stands_for' | t }}</p>
            <p class="own">{{ note() || ('messages.note_default' | t) }}</p>
          </div>
          <div class="actions">
            <button mat-flat-button type="button" [disabled]="busy() || note() === (item().note ?? '')" (click)="saveNote()">
              {{ 'common.save' | t }}
            </button>
          </div>
        }

        @if (usesSms()) {
          <h3 class="section-title">{{ 'messages.words_title' | t }}</h3>
          <p class="muted hint">{{ 'messages.words_hint' | t }}</p>
          <mat-button-toggle-group
            class="choices languages"
            [value]="language()"
            [attr.aria-label]="'messages.language_label' | t"
            hideSingleSelectionIndicator
            (change)="language.set($event.value)"
          >
            @for (text of item().texts; track text.language) {
              <mat-button-toggle [value]="text.language">{{ languageName(text.language) }}</mat-button-toggle>
            }
          </mat-button-toggle-group>

          <mat-form-field appearance="outline" class="wide">
            <mat-label>{{ 'messages.words_label' | t }}</mat-label>
            <textarea #box matInput rows="4" [value]="draft()" [disabled]="busy()" (input)="draft.set(words($event))"></textarea>
          </mat-form-field>

          <p class="muted hint add">{{ 'messages.fields_hint' | t }}</p>
          <div class="fields">
            @for (field of item().fields; track field) {
              <button mat-stroked-button type="button" class="field" [disabled]="busy()" (click)="insert(field, box)">
                {{ 'messages.field.' + field | t }}
              </button>
            }
          </div>

          <div class="bubble">
            <p class="said">{{ preview() }}</p>
            <p class="parts">{{ parts() }}</p>
          </div>

          <div class="actions">
            <button
              mat-flat-button
              type="button"
              [disabled]="busy() || draft().trim().length === 0 || draft() === current()"
              (click)="text.emit({ language: language(), text: draft() })"
            >
              {{ 'common.save' | t }}
            </button>
            <button mat-button type="button" [disabled]="busy() || !custom()" (click)="text.emit({ language: language(), text: '' })">
              {{ 'messages.reset' | t }}
            </button>
          </div>
        }
      }
    </mat-expansion-panel>
  `,
  styles: `
    :host { display: block; }
    mat-panel-title { gap: 0.5rem; align-items: center; }
    mat-panel-title mat-icon { font-size: 20px; width: 20px; height: 20px; flex: none; color: var(--mat-sys-on-surface-variant); }
    mat-panel-description { justify-content: flex-end; flex-grow: 0; white-space: nowrap; }
    .what { margin: 0 0 0.5rem; font: var(--mat-sys-body-small); }
    .line { display: flex; align-items: center; justify-content: space-between; gap: 1rem; min-height: 2.75rem; }
    /* The choices are long in Sinhala and Tamil, so they wrap rather than run off a phone screen. */
    .choices { max-width: 100%; flex-wrap: wrap; margin-top: 0.25rem; }
    .choices mat-button-toggle { flex: 1 1 auto; }
    .languages { margin-bottom: 0.25rem; }
    .hint { margin: 0.5rem 0 0; font: var(--mat-sys-body-small); }
    .hint.add { margin-top: 0; }
    .section-title { margin: 1.25rem 0 0; font: var(--mat-sys-title-small); }
    .percent { margin-top: 0.75rem; width: 100%; max-width: 16rem; }
    .wide { width: 100%; margin-top: 0.75rem; }
    .fields { display: flex; flex-wrap: wrap; gap: 0.375rem; margin: 0.5rem 0 0.75rem; }
    .fields .field { font: var(--mat-sys-label-medium); }
    /* The message as a parent will see it on their phone. */
    .bubble { padding: 0.625rem 0.75rem; border-radius: var(--mat-sys-corner-medium); background: var(--mat-sys-surface-container-high); }
    .bubble p { margin: 0; overflow-wrap: anywhere; }
    .said { font: var(--mat-sys-body-medium); white-space: pre-wrap; }
    .own { margin-top: 0.375rem !important; font: var(--mat-sys-body-medium); font-style: italic; }
    .parts { margin-top: 0.375rem !important; font: var(--mat-sys-body-small); color: var(--mat-sys-on-surface-variant); }
    .actions { display: flex; flex-wrap: wrap; gap: 0.5rem; margin-top: 0.75rem; }
  `,
})
export class MessageEventCard {
  private readonly i18n = inject(TranslateService);

  readonly item = input.required<MessageEvent>();
  readonly noteMax = input(80);
  readonly weeks = input(4);
  readonly quietDays = input(14);
  readonly busy = input(false);

  readonly rule = output<SaveRuleInput>();
  readonly text = output<{ language: string; text: string }>();

  protected readonly percents = PERCENT_CHOICES;

  /** The language being written in. The API puts the teacher's own first, which is the one they will write in. */
  protected readonly language = linkedSignal(() => this.item().texts[0]?.language ?? 'en');

  protected readonly icon = computed(() => EVENT_ICON[this.item().event] ?? 'notifications');

  protected readonly summary = computed(() => (this.item().on ? `messages.way.${this.item().way}` : 'messages.off'));

  protected readonly usesWhatsApp = computed(() => this.item().way === 'whatsapp' || this.item().way === 'whatsapp_then_sms');
  protected readonly usesSms = computed(() => this.item().way === 'sms' || this.item().way === 'whatsapp_then_sms');

  protected readonly percentHint = computed(() =>
    this.i18n.format('messages.percent_hint', { weeks: this.weeks(), days: this.quietDays() }),
  );

  /** The words in use for the language on screen: the teacher's own, or GuruBook's when they have written none. */
  protected readonly current = computed(() => {
    const text = this.item().texts.find((t) => t.language === this.language());
    return text?.custom ?? text?.default ?? '';
  });

  protected readonly custom = computed(() => !!this.item().texts.find((t) => t.language === this.language())?.custom);

  protected readonly note = linkedSignal(() => this.item().note ?? '');

  /** What is in the box. It follows the saved words until the teacher types, and goes back to them after a save or a reset. */
  protected readonly draft = linkedSignal<string, string>({
    source: () => `${this.language()}\u0000${this.current()}`,
    computation: (source, previous) => (previous?.source === source ? previous.value : source.slice(source.indexOf('\u0000') + 1)),
  });

  protected readonly preview = computed(() => fillSample(this.draft()));
  protected readonly parts = computed(() => this.i18n.format('sms.parts', { parts: smsParts(this.preview()) }));

  protected languageName(code: string): string {
    return LANGUAGES.find((l) => l.code === code)?.name ?? code;
  }

  protected words(event: Event): string {
    return (event.target as HTMLInputElement | HTMLTextAreaElement).value;
  }

  protected saveNote(): void {
    this.rule.emit({ on: true, way: this.item().way, note: this.note(), percent: this.item().percent });
  }

  /** Puts a field where the teacher was typing, so the words read the way they meant them. */
  protected insert(field: string, box: HTMLTextAreaElement): void {
    const at = box.selectionStart ?? this.draft().length;
    const to = box.selectionEnd ?? at;
    const put = `{${field}}`;
    this.draft.set(`${this.draft().slice(0, at)}${put}${this.draft().slice(to)}`);
    queueMicrotask(() => {
      box.focus();
      box.setSelectionRange(at + put.length, at + put.length);
    });
  }
}
