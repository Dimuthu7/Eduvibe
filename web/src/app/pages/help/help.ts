import { ChangeDetectionStrategy, Component } from '@angular/core';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '../../core/i18n/translate.pipe';

/** A topic of the help page. Its text is `help.<id>.title` and `help.<id>.1` to `help.<id>.<steps>`. */
export interface HelpTopic {
  id: string;
  icon: string;
  steps: number;
}

export const HELP_TOPICS: readonly HelpTopic[] = [
  { id: 'start', icon: 'rocket_launch', steps: 5 },
  { id: 'classes', icon: 'menu_book', steps: 3 },
  { id: 'students', icon: 'groups', steps: 4 },
  { id: 'attendance', icon: 'fact_check', steps: 4 },
  { id: 'fees', icon: 'payments', steps: 4 },
  { id: 'exams', icon: 'quiz', steps: 4 },
  { id: 'whatsapp', icon: 'chat', steps: 3 },
  { id: 'plan', icon: 'workspace_premium', steps: 2 },
];

/** A short guide for teachers: what each part of the app is for, as a few steps each. */
@Component({
  selector: 'app-help',
  host: { class: 'page' },
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatExpansionModule, MatIconModule, TranslatePipe],
  template: `
    <header class="page-header">
      <div>
        <h1 class="page-title">{{ 'help.title' | t }}</h1>
        <p class="page-subtitle">{{ 'help.subtitle' | t }}</p>
      </div>
    </header>

    <mat-accordion class="topics">
      @for (topic of topics; track topic.id; let first = $first) {
        <mat-expansion-panel [expanded]="first">
          <mat-expansion-panel-header>
            <mat-panel-title>
              <mat-icon aria-hidden="true">{{ topic.icon }}</mat-icon>
              {{ 'help.' + topic.id + '.title' | t }}
            </mat-panel-title>
          </mat-expansion-panel-header>
          <ol>
            @for (n of steps(topic); track n) {
              <li>{{ 'help.' + topic.id + '.' + n | t }}</li>
            }
          </ol>
        </mat-expansion-panel>
      }
    </mat-accordion>

    <p class="muted more">{{ 'help.more' | t }}</p>
  `,
  styles: `
    .topics { display: block; }
    mat-panel-title { gap: 0.75rem; align-items: center; }
    ol { margin: 0; padding-left: 1.25rem; display: grid; gap: 0.5rem; }
    .more { margin-top: 1rem; }
  `,
})
export class Help {
  protected readonly topics = HELP_TOPICS;

  protected steps(topic: HelpTopic): number[] {
    return Array.from({ length: topic.steps }, (_, i) => i + 1);
  }
}
