import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { AuthService } from '../services/auth.service';

@Component({
  selector: 'app-login',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, TranslatePipe],
  templateUrl: './login.component.html',
  styleUrl: './login.component.scss',
})
export class LoginComponent {
  private authService = inject(AuthService);
  private router = inject(Router);
  private fb = inject(FormBuilder);
  private translate = inject(TranslateService);

  form = this.fb.group({
    password: ['', [Validators.required]],
  });

  submitting = signal(false);
  errorMessage = signal<string | null>(null);
  showPassword = signal(false);
  currentLang = signal<string>(this.translate.currentLang ?? 'en');

  readonly availableLanguages: ReadonlyArray<{ code: string; label: string }> = [
    { code: 'en', label: 'English' },
    { code: 'fr', label: 'Français' },
  ];

  togglePassword(): void {
    this.showPassword.update((v) => !v);
  }

  switchLanguage(code: string): void {
    this.currentLang.set(code);
    this.translate.use(code);
  }

  submit(): void {
    if (this.form.invalid || this.submitting()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    const password = this.form.value.password ?? '';
    this.authService.login(password).subscribe({
      next: () => {
        this.submitting.set(false);
        this.router.navigate(['/']);
      },
      error: (err: unknown) => {
        this.submitting.set(false);
        if (err instanceof HttpErrorResponse && err.status === 401) {
          const messages: string[] = this.translate.instant('login.wrongPasswordMessages');
          const funny = Array.isArray(messages) && messages.length
            ? messages[Math.floor(Math.random() * messages.length)]
            : 'Access denied.';
          this.errorMessage.set(funny);
        } else {
          this.errorMessage.set(this.translate.instant('login.errorGeneric'));
        }
      },
    });
  }
}
