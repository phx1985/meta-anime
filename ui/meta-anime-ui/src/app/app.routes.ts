import { inject } from '@angular/core';
import { Routes, Router } from '@angular/router';
import { map, catchError } from 'rxjs/operators';
import { of } from 'rxjs';
import { HomeComponent } from './home/home.component';
import { AnimeListComponent } from './anime-list/anime-list.component';
import { SettingsComponent } from './settings/settings.component';
import { SeasonalComponent } from './seasonal/seasonal.component';
import { NotFoundComponent } from './not-found/not-found.component';
import { MalCallbackComponent } from './mal-callback/mal-callback.component';
import { SetupWizardComponent } from './setup-wizard/setup-wizard.component';
import { LoginComponent } from './login/login.component';
import { LibraryAnalysisComponent } from './library-analysis/library-analysis.component';
import { EnrollmentService } from './services/enrollment.service';
import { AuthService } from './services/auth.service';
import { authGuard } from './guards/auth.guard';

/** Redirect to /welcome if MAL is not yet connected. */
const malConnectedGuard = () => {
  const enrollmentService = inject(EnrollmentService);
  const router = inject(Router);
  return enrollmentService.getMalStatus().pipe(
    map((status) => status.connected || router.createUrlTree(['/welcome'])),
    catchError(() => of(true)),
  );
};

/**
 * Guard for the /login route.
 * Redirects to home if auth is not required (public mode) or if already authenticated.
 */
const loginGuard = () => {
  const authService = inject(AuthService);
  const router = inject(Router);
  return authService.getStatus().pipe(
    map((status) => {
      if (!status.requiresAuth) return router.createUrlTree(['/']);
      if (authService.isAuthenticated()) return router.createUrlTree(['/']);
      return true;
    }),
    catchError(() => of(true)),
  );
};

export const routes: Routes = [
  { path: '', component: HomeComponent, canActivate: [authGuard, malConnectedGuard] },
  { path: 'login', component: LoginComponent, canActivate: [loginGuard] },
  { path: 'welcome', component: SetupWizardComponent },
  { path: 'animes', component: AnimeListComponent, canActivate: [authGuard] },
  { path: 'seasonal', component: SeasonalComponent, canActivate: [authGuard] },
  { path: 'settings', component: SettingsComponent, canActivate: [authGuard] },
  { path: 'library', component: LibraryAnalysisComponent, canActivate: [authGuard] },
  { path: 'auth/mal/callback', component: MalCallbackComponent },
  { path: '**', component: NotFoundComponent },
];
