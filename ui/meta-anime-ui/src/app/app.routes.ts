import { Routes } from '@angular/router';
import { HomeComponent } from './home/home.component';
import { AnimeListComponent } from './anime-list/anime-list.component';

export const routes: Routes = [
  { path: '', component: HomeComponent },
  { path: 'animes', component: AnimeListComponent },
  { path: '**', redirectTo: '' }
];
