import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, inject, OnInit, signal, viewChild } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatDividerModule } from '@angular/material/divider';
import { MatIconModule } from '@angular/material/icon';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AttendanceService } from '../core/api/attendance.service';
import { EnrolmentFees, FeesService } from '../core/api/fees.service';
import { errorCode } from '../core/api/problem';
import { Student, StudentsService } from '../core/api/students.service';
import { TranslatePipe } from '../core/i18n/translate.pipe';
import { TranslateService } from '../core/i18n/translate.service';
import { formatLkr, todayColombo } from './format';
import { CARD_PREFIX, QrCamera } from './qr-camera';

/** One of the student's classes, with where today's attendance and the fees stand in it. */
interface ClassLine {
  enrolmentId: string;
  classTitle: string;
  /** Present, Late or Absent when the session has been marked, otherwise null. */
  today: string | null;
  owed: number;
  fees: EnrolmentFees | null;
}

/**
 * The scan button in the top bar: point the camera at a student's card and the dialog turns into that student, with today's attendance,
 * what they owe, a payment that can be recorded on the spot and the parent's number to call. Nothing is marked or saved by scanning.
 */
@Component({
  selector: 'app-student-scan-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatDialogModule, MatDividerModule, MatIconModule, TranslatePipe],
  template: `
    @if (student(); as s) {
      <h2 mat-dialog-title>{{ s.fullName }}</h2>
      <mat-dialog-content>
        <p class="muted who">
          {{ s.phone }}
          @if (s.school) {
            · {{ s.school }}
          }
          @if (s.grade) {
            · {{ s.grade }}
          }
        </p>

        @for (line of lines(); track line.enrolmentId) {
          <div class="line">
            <div class="about">
              <strong>{{ line.classTitle }}</strong>
              <span class="state">
                @if (line.today) {
                  <mat-icon class="mark {{ line.today.toLowerCase() }}" aria-hidden="true">{{ icons[line.today] ?? 'help' }}</mat-icon>
                  {{ 'attendance.' + line.today.toLowerCase() | t }}
                } @else {
                  <mat-icon class="mark none" aria-hidden="true">radio_button_unchecked</mat-icon>
                  {{ 'scan.not_marked' | t }}
                }
                @if (line.owed > 0) {
                  · <span class="owed">{{ 'scan.owes' | t }} {{ money(line.owed) }}</span>
                } @else if (line.fees) {
                  · {{ 'scan.paid_up' | t }}
                }
              </span>
            </div>
            @if (line.fees && line.owed > 0) {
              <button mat-stroked-button type="button" (click)="pay(line)">{{ 'fees.record_payment' | t }}</button>
            }
          </div>
          <mat-divider />
        }

        @if (lines().length === 0) {
          <p class="muted">{{ 'scan.no_classes' | t }}</p>
        }
      </mat-dialog-content>
      <mat-dialog-actions>
        @if (s.guardianPhone) {
          <a mat-stroked-button [href]="'tel:' + s.guardianPhone">
            <mat-icon>call</mat-icon>
            {{ 'scan.call_parent' | t }}
          </a>
        }
        <span class="spacer"></span>
        <button mat-button type="button" (click)="again()">{{ 'scan.scan_another' | t }}</button>
        <button mat-flat-button type="button" (click)="open(s)">{{ 'scan.open_student' | t }}</button>
      </mat-dialog-actions>
    } @else {
      <h2 mat-dialog-title>{{ 'scan.find_title' | t }}</h2>
      <mat-dialog-content>
        <div class="scanner">
          <video #video class="video" playsinline muted aria-hidden="true"></video>
          <div class="aim" aria-hidden="true"></div>
        </div>
        <p class="answer" role="status" aria-live="polite">
          @if (error(); as text) {
            <mat-icon aria-hidden="true">error</mat-icon> {{ text }}
          } @else if (cameraProblem(); as problem) {
            <mat-icon aria-hidden="true">videocam_off</mat-icon> {{ problem | t }}
          } @else {
            {{ 'scan.find_hint' | t }}
          }
        </p>
      </mat-dialog-content>
      <mat-dialog-actions>
        <span class="spacer"></span>
        <button mat-button type="button" (click)="close()">{{ 'common.close' | t }}</button>
      </mat-dialog-actions>
    }
  `,
  styles: `
    :host { display: block; }
    .scanner { position: relative; display: grid; place-items: center; aspect-ratio: 1; max-height: 60vh; overflow: hidden; border-radius: var(--mat-sys-corner-medium); background: #000; }
    .video { position: absolute; inset: 0; width: 100%; height: 100%; object-fit: cover; }
    .aim { position: relative; width: 65%; aspect-ratio: 1; border: 3px solid rgba(255, 255, 255, 0.85); border-radius: 1rem; box-shadow: 0 0 0 100vmax rgba(0, 0, 0, 0.4); }
    .answer { display: flex; gap: 0.5rem; align-items: center; justify-content: center; min-height: 2.5rem; margin: 0.75rem 0 0; font: var(--mat-sys-body-medium); text-align: center; color: var(--mat-sys-on-surface-variant); }
    .answer mat-icon { flex: none; color: var(--mat-sys-error); }
    .who { margin: 0 0 0.75rem; }
    .line { display: flex; gap: 0.75rem; align-items: center; justify-content: space-between; padding: 0.75rem 0; }
    .about { display: grid; gap: 0.25rem; min-width: 0; }
    .state { display: flex; gap: 0.25rem; align-items: center; flex-wrap: wrap; font: var(--mat-sys-body-medium); color: var(--mat-sys-on-surface-variant); }
    .state mat-icon { font-size: 18px; width: 18px; height: 18px; }
    .mark.present, .mark.late { color: var(--mat-sys-primary); }
    .mark.absent { color: var(--mat-sys-error); }
    .owed { color: var(--mat-sys-error); font-weight: 500; }
    .spacer { flex: 1 1 auto; }
  `,
})
export class StudentScanDialog implements OnInit {
  private readonly ref = inject<MatDialogRef<StudentScanDialog>>(MatDialogRef);
  private readonly students = inject(StudentsService);
  private readonly fees = inject(FeesService);
  private readonly attendance = inject(AttendanceService);
  private readonly dialog = inject(MatDialog);
  private readonly router = inject(Router);
  private readonly i18n = inject(TranslateService);

  private readonly video = viewChild<ElementRef<HTMLVideoElement>>('video');
  protected readonly student = signal<Student | null>(null);
  protected readonly lines = signal<ClassLine[]>([]);
  protected readonly cameraProblem = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly money = formatLkr;
  protected readonly icons: Record<string, string | undefined> = { Present: 'check_circle', Late: 'schedule', Absent: 'cancel' };

  // Stopped for good once a card is read, so going back to the camera starts a new one.
  private camera = new QrCamera();

  constructor() {
    inject(DestroyRef).onDestroy(() => this.camera.stop());
  }

  ngOnInit(): void {
    void this.start();
  }

  protected close(): void {
    this.ref.close();
  }

  /** Back to the camera for the next card, without closing and reopening the dialog. */
  protected again(): void {
    this.student.set(null);
    this.lines.set([]);
    this.camera = new QrCamera();
    void this.start();
  }

  protected open(student: Student): void {
    this.ref.close();
    void this.router.navigate(['/students', student.id]);
  }

  protected async pay(line: ClassLine): Promise<void> {
    const fees = line.fees;
    if (!fees) return;
    const { PaymentDialog } = await import('../pages/fees/payment-dialog');
    const done = await firstValueFrom(
      this.dialog.open(PaymentDialog, { data: fees, width: '40rem', maxWidth: 'calc(100vw - 1rem)' }).afterClosed(),
    );
    if (done) await this.show(fees.studentId);
  }

  private async start(): Promise<void> {
    this.cameraProblem.set(null);
    this.error.set(null);
    const problem = await this.camera.start(() => this.video()?.nativeElement, (value) => this.found(value));
    if (problem) this.cameraProblem.set(problem);
  }

  private async found(value: string): Promise<void> {
    const text = value.startsWith(CARD_PREFIX) ? value : '';
    if (!text) {
      this.say('card_unknown');
      return;
    }

    try {
      const student = await firstValueFrom(this.students.findByCard(text));
      this.camera.stop();
      this.student.set(student);
      navigator.vibrate?.([60]);
      await this.load(student);
    } catch (e) {
      this.say(errorCode(e) ?? 'unknown');
    }
  }

  /** Opens the quick view for a student already known, used again after a payment is recorded. */
  private async show(studentId: string): Promise<void> {
    const student = await firstValueFrom(this.students.student(studentId));
    this.student.set(student);
    await this.load(student);
  }

  private async load(student: Student): Promise<void> {
    const today = todayColombo();
    const active = student.enrolments.filter((e) => e.status !== 'Left');
    this.lines.set(active.map((e) => ({ enrolmentId: e.id, classTitle: e.classTitle, today: null, owed: 0, fees: null })));

    // Each class is asked about on its own; a class that fails simply shows nothing rather than losing the whole view.
    const lines = await Promise.all(
      active.map(async (e): Promise<ClassLine> => {
        const [fees, report] = await Promise.all([
          firstValueFrom(this.fees.enrolment(e.id)).catch(() => null),
          firstValueFrom(this.attendance.studentReport(e.id, today, today)).catch(() => null),
        ]);
        return {
          enrolmentId: e.id,
          classTitle: e.classTitle,
          today: report?.entries.at(-1)?.status ?? null,
          owed: fees?.outstanding ?? 0,
          fees,
        };
      }),
    );
    this.lines.set(lines);
  }

  private say(code: string): void {
    this.error.set(this.i18n.translate(`error.${code}`));
    navigator.vibrate?.([200]);
  }
}
