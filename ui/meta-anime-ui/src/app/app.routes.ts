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
import { EnrollmentService } from './services/enrollment.service';

/** Redirect to /welcome if MAL is not yet connected. */
const malConnectedGuard = () => {
  const enrollmentService = inject(EnrollmentService);
  const router = inject(Router);
  return enrollmentService.getMalStatus().pipe(
    map((status) => status.connected || router.createUrlTree(['/welcome'])),
    catchError(() => of(true)),
  );
};

export const routes: Routes = [
  { path: '', component: HomeComponent, canActivate: [malConnectedGuard] },
  { path: 'welcome', component: SetupWizardComponent },
  { path: 'animes', component: AnimeListComponent },
  { path: 'seasonal', component: SeasonalComponent },
  { path: 'settings', component: SettingsComponent },
  { path: 'auth/mal/callback', component: MalCallbackComponent },
  { path: '**', component: NotFoundComponent },
];
