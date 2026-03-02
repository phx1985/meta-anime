import { Routes } from '@angular/router';
import { HomeComponent } from './home/home.component';
import { AnimeListComponent } from './anime-list/anime-list.component';
import { SettingsComponent } from './settings/settings.component';
import { SeasonalComponent } from './seasonal/seasonal.component';
import { NotFoundComponent } from './not-found/not-found.component';
import { MalCallbackComponent } from './mal-callback/mal-callback.component';

export const routes: Routes = [
  { path: '', component: HomeComponent },
  { path: 'animes', component: AnimeListComponent },
  { path: 'seasonal', component: SeasonalComponent },
  { path: 'settings', component: SettingsComponent },
  { path: 'auth/mal/callback', component: MalCallbackComponent },
  { path: '**', component: NotFoundComponent },
];
