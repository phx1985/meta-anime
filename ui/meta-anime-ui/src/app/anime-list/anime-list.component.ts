import { Component, inject, signal, DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslatePipe } from '@ngx-translate/core';
import { AnimeService } from '../services/anime.service';
import { AnimeResponse } from '../models/anime.models';

const PAGE_SIZE = 100;

@Component({
  selector: 'app-anime-list',
  standalone: true,
  imports: [TranslatePipe],
  templateUrl: './anime-list.component.html',
  styleUrl: './anime-list.component.scss'
})
export class AnimeListComponent {
  private animeService = inject(AnimeService);
  private destroyRef = inject(DestroyRef);

  animes = signal<AnimeResponse[]>([]);
  loading = signal(true);
  error = signal(false);

  constructor() {
    this.animeService.getAnimes(1, PAGE_SIZE)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: data => {
          this.animes.set(data.Data);
          this.loading.set(false);
        },
        error: () => {
          this.error.set(true);
          this.loading.set(false);
        }
      });
  }
}
