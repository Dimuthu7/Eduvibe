import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { ALERT_TOPICS, AlertTopic, MutedTopic, NotificationsService } from '../../core/api/notifications.service';
import { Child, StudentsService } from '../../core/api/students.service';
import { AuthService } from '../../core/auth/auth.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { ToastService } from '../../core/ui/toast.service';

/**
 * Who hears what. A parent switches payments, attendance or results off for one child, on every phone they sign in on, because it is
 * their choice. A teacher says what their own parents are told on the Messages page instead. Anyone else sees nothing here.
 */
@Component({
  selector: 'app-alerts-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatCardModule, MatSlideToggleModule, TranslatePipe],
  template: `
    @if (isParent && children().length > 0) {
      <mat-card appearance="outlined" class="alerts">
        <mat-card-content>
          <h2 class="section-title first">{{ 'alerts.title' | t }}</h2>
          <p class="muted">{{ 'alerts.subtitle' | t }}</p>
          @for (child of children(); track child.studentId) {
            <section class="child" [attr.aria-label]="child.fullName">
              <h3 class="name">{{ child.fullName }}</h3>
              @for (topic of topics; track topic) {
                <div class="line">
                  <span>{{ 'alerts.topic.' + topic | t }}</span>
                  <mat-slide-toggle
                    [checked]="on(child.studentId, topic)"
                    [disabled]="busy()"
                    [aria-label]="child.fullName + ': ' + ('alerts.topic.' + topic | t)"
                    (change)="set(child.studentId, topic, $event.checked)"
                  />
                </div>
              }
            </section>
          }
        </mat-card-content>
      </mat-card>
    }
  `,
  styles: `
    :host { display: contents; }
    .alerts { margin-top: 1rem; }
    .section-title.first { margin-top: 0; }
    .muted { margin: 0 0 0.5rem; }
    .child { padding-top: 0.5rem; }
    .child + .child { margin-top: 0.5rem; border-top: 1px solid var(--mat-sys-outline-variant); }
    .name { margin: 0.5rem 0 0.25rem; font: var(--mat-sys-title-small); }
    .line { display: flex; align-items: center; justify-content: space-between; gap: 1rem; min-height: 2.75rem; }
  `,
})
export class AlertsCard {
  private readonly api = inject(NotificationsService);
  private readonly students = inject(StudentsService);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);

  protected readonly topics = ALERT_TOPICS;
  protected readonly isParent = this.auth.hasRole('Parent');

  protected readonly children = signal<Child[]>([]);
  protected readonly muted = signal<MutedTopic[]>([]);
  protected readonly busy = signal(false);

  constructor() {
    if (this.isParent) {
      this.students.family().subscribe((list) => this.children.set(list));
      this.api.myAlerts().subscribe((mine) => this.muted.set(mine.muted));
    }
  }

  protected on(studentId: string, topic: AlertTopic): boolean {
    return !this.muted().some((m) => m.studentId === studentId && m.topic === topic);
  }

  protected set(studentId: string, topic: AlertTopic, on: boolean): void {
    this.busy.set(true);
    this.api.muteAlerts(studentId, topic, on).subscribe({
      next: (mine) => {
        this.muted.set(mine.muted);
        this.busy.set(false);
        this.toast.success(on ? 'alerts.turned_on' : 'alerts.turned_off');
      },
      error: () => this.busy.set(false),
    });
  }
}
