import { ChangeDetectionStrategy, Component, DestroyRef, inject, OnInit } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { EnrollmentService } from '../services/enrollment.service';
import { NotificationService } from '../services/notification.service';

@Component({
  selector: 'app-mal-callback',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe],
  templateUrl: './mal-callback.component.html',
  styleUrl: './mal-callback.component.scss',
})
export class MalCallbackComponent implements OnInit {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private enrollmentService = inject(EnrollmentService);
  private notifService = inject(NotificationService);
  private destroyRef = inject(DestroyRef);

  ngOnInit(): void {
    const code = this.route.snapshot.queryParams['code'] as string | undefined;
    const state = this.route.snapshot.queryParams['state'] as string | undefined;

    if (!code || !state) {
      this.notifService.push({
        id: `mal-connect-error-${Date.now()}`,
        severity: 'error',
        titleKey: 'notifications.malConnect.errorTitle',
        messageKey: 'notifications.malConnect.errorMessage',
      });
      this.router.navigate(['/settings']);
      return;
    }

    this.enrollmentService
      .exchangeMalCode(code, state)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.notifService.push({
            id: `mal-connect-success-${Date.now()}`,
            severity: 'success',
            titleKey: 'notifications.malConnect.successTitle',
            messageKey: 'notifications.malConnect.successMessage',
          });
          const wizardActive = localStorage.getItem('wizard_active') === 'true';
          this.router.navigate([wizardActive ? '/welcome' : '/settings']);
        },
        error: () => {
          this.notifService.push({
            id: `mal-connect-error-${Date.now()}`,
            severity: 'error',
            titleKey: 'notifications.malConnect.errorTitle',
            messageKey: 'notifications.malConnect.errorMessage',
          });
          this.router.navigate(['/settings']);
        },
      });
  }
}
