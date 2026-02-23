import { Component, inject, signal, DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslatePipe } from '@ngx-translate/core';
import { AnimeService } from '../services/anime.service';
import { SyncSignalRService } from '../services/sync-signalr.service';
import { AnimeDetailsResponse } from '../models/anime.models';
import { AnimeCardComponent } from './anime-card/anime-card.component';

const PAGE_SIZE = 100;

@Component({
  selector: 'app-anime-list',
  standalone: true,
  imports: [TranslatePipe, AnimeCardComponent],
  templateUrl: './anime-list.component.html',
  styleUrl: './anime-list.component.scss'
})
export class AnimeListComponent {
  private animeService = inject(AnimeService);
  private syncSignalR  = inject(SyncSignalRService);
  private destroyRef   = inject(DestroyRef);

  animes  = signal<AnimeDetailsResponse[]>([]);
  loading = signal(true);
  error   = signal(false);

  constructor() {
    this.loadAnimes();

    // Auto-refresh when a sync completes while on this page
    this.syncSignalR.syncCompleted$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.loadAnimes());
  }

  private loadAnimes(): void {
    this.loading.set(true);
    this.error.set(false);
    this.animeService.getAnimes(1, PAGE_SIZE)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: data => {
          this.animes.set(data.data);
          this.loading.set(false);
        },
        error: () => {
          this.error.set(true);
          this.loading.set(false);
        }
      });
  }
}
