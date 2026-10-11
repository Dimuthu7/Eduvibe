import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { errorCode, errorKey, inlineError } from '../../core/api/problem';
import { InviteInfo, Registered, StudentsService } from '../../core/api/students.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { Loadable } from '../../core/state/loadable';
import { applyServerError, fieldErrorKey, filled, phoneValidator } from '../../shared/forms';
import { SubmitButton } from '../../shared/submit-button';

/**
 * The page a student opens from a sign-up link, with nobody signed in. They give their details and choose a
 * password; the teacher then approves them into the class.
 */
@Component({
  selector: 'app-register',
  host: { class: 'page page-narrow' },
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink, SubmitButton, MatButtonModule, MatCardModule, MatFormFieldModule, MatIconModule, MatInputModule, TranslatePipe],
  template: `
    @if (done(); as result) {
      <mat-card appearance="outlined">
        <mat-card-content class="done">
          <mat-icon aria-hidden="true">check_circle</mat-icon>
          <h1 class="page-title">{{ 'register.done_title' | t }}</h1>
          <p>{{ 'register.done_class' | t }} <strong>{{ result.classTitle }}</strong>.</p>
          <p>{{ (result.outcome === 'created' ? 'register.done_created' : 'register.done_existing') | t }}</p>
          <a mat-flat-button routerLink="/login">{{ 'register.to_login' | t }}</a>
        </mat-card-content>
      </mat-card>
    } @else if (problem(); as why) {
      <div class="empty">
        <mat-icon>link_off</mat-icon>
        <p>{{ why | t }}</p>
      </div>
    } @else {
      @switch (info.status()) {
        @case ('ready') {
          @if (info.data(); as invite) {
            <mat-card appearance="outlined">
              <mat-card-header>
                <mat-card-title>{{ 'register.title' | t }}</mat-card-title>
                <mat-card-subtitle>
                  {{ invite.classTitle }}@if (invite.teacherName) { · {{ invite.teacherName }} }
                </mat-card-subtitle>
              </mat-card-header>
              <mat-card-content>
                <form class="stack form" [formGroup]="form" (ngSubmit)="submit()" novalidate>
                  @if (error()) {
                    <p class="field-error" role="alert">{{ error() | t }}</p>
                  }
                  <mat-form-field>
                    <mat-label>{{ 'person.first_name' | t }}</mat-label>
                    <input matInput formControlName="firstName" autocomplete="given-name" maxlength="60" required />
                    <mat-error>{{ key('firstName') | t }}</mat-error>
                  </mat-form-field>
                  <mat-form-field>
                    <mat-label>{{ 'person.last_name' | t }}</mat-label>
                    <input matInput formControlName="lastName" autocomplete="family-name" maxlength="60" required />
                    <mat-error>{{ key('lastName') | t }}</mat-error>
                  </mat-form-field>
                  <mat-form-field>
                    <mat-label>{{ 'students.phone' | t }}</mat-label>
                    <input matInput type="tel" inputmode="tel" formControlName="phone" autocomplete="tel" required />
                    <mat-hint>{{ 'register.phone_hint' | t }}</mat-hint>
                    <mat-error>{{ key('phone') | t }}</mat-error>
                  </mat-form-field>
                  <mat-form-field>
                    <mat-label>{{ 'students.school' | t }}</mat-label>
                    <input matInput formControlName="school" autocomplete="off" maxlength="120" />
                  </mat-form-field>
                  <mat-form-field>
                    <mat-label>{{ 'students.grade' | t }}</mat-label>
                    <input matInput formControlName="grade" autocomplete="off" maxlength="30" />
                  </mat-form-field>
                  <mat-form-field>
                    <mat-label>{{ 'students.guardian' | t }}</mat-label>
                    <input matInput formControlName="guardianName" autocomplete="off" maxlength="120" />
                  </mat-form-field>
                  <mat-form-field>
                    <mat-label>{{ 'students.guardian_phone' | t }}</mat-label>
                    <input matInput type="tel" inputmode="tel" formControlName="guardianPhone" autocomplete="off" />
                    <mat-error>{{ key('guardianPhone') | t }}</mat-error>
                  </mat-form-field>
                  <mat-form-field>
                    <mat-label>{{ 'register.password' | t }}</mat-label>
                    <input matInput [type]="showPassword() ? 'text' : 'password'" formControlName="password" autocomplete="new-password" autocapitalize="none" autocorrect="off" spellcheck="false" required />
                    <button mat-icon-button matSuffix type="button" [attr.aria-label]="(showPassword() ? 'login.hide_password' : 'login.show_password') | t" [attr.aria-pressed]="showPassword()" (click)="showPassword.set(!showPassword())">
                      <mat-icon>{{ showPassword() ? 'visibility_off' : 'visibility' }}</mat-icon>
                    </button>
                    <mat-hint>{{ 'register.password_hint' | t }}</mat-hint>
                    <mat-error>{{ key('password') | t }}</mat-error>
                  </mat-form-field>
                  <app-submit-button label="register.submit" [busy]="busy()" />
                </form>
              </mat-card-content>
            </mat-card>
          }
        }
        @default {
          <div class="skeleton"></div>
          <div class="skeleton"></div>
        }
      }
    }
  `,
  styles: `
    .form { padding-top: 1rem; }
    .done { display: grid; justify-items: center; gap: 0.5rem; text-align: center; padding: 1.5rem 0.5rem; }
    .done mat-icon { width: 3.5rem; height: 3.5rem; font-size: 3.5rem; color: var(--mat-sys-primary); }
    .done p { margin: 0; }
    .done a { margin-top: 1rem; }
  `,
})
export class Register {
  /** The link token from the address. */
  readonly token = input.required<string>();

  private readonly api = inject(StudentsService);

  protected readonly info = new Loadable<InviteInfo>();
  protected readonly done = signal<Registered | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly showPassword = signal(false);
  /** Set when the link stopped working while the form was open. */
  private readonly closed = signal('');
  /** Why the page cannot be used (unknown, expired, full or revoked link; no connection), or '' when it can. */
  protected readonly problem = computed(() => this.closed() || (this.info.status() === 'error' ? errorKey(this.info.error()) : ''));

  protected readonly form = inject(FormBuilder).nonNullable.group({
    firstName: ['', [filled, Validators.maxLength(60)]],
    lastName: ['', [filled, Validators.maxLength(60)]],
    phone: ['', [filled, phoneValidator]],
    school: ['', Validators.maxLength(120)],
    grade: ['', Validators.maxLength(30)],
    guardianName: ['', Validators.maxLength(120)],
    guardianPhone: ['', phoneValidator],
    password: ['', [Validators.required, Validators.minLength(8)]],
  });

  constructor() {
    queueMicrotask(() => this.info.load(this.api.inviteInfo(this.token())));
  }

  protected key(name: string): string {
    const control = this.form.get(name);
    return control && control.touched ? fieldErrorKey(control) : '';
  }

  protected submit(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    this.busy.set(true);
    this.error.set('');
    this.api.register(this.token(), this.form.getRawValue()).subscribe({
      next: (result) => this.done.set(result),
      error: (e) => {
        this.busy.set(false);
        const code = errorCode(e);
        if (code?.startsWith('invite_')) {
          this.closed.set(`error.${code}`);
        } else if (!applyServerError(this.form, code)) {
          this.error.set(inlineError(e));
        }
      },
    });
  }
}
