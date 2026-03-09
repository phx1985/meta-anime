import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslatePipe } from '@ngx-translate/core';
import { AnimeService } from '../../services/anime.service';
import { ManualMapping } from '../../models/anime.models';

@Component({
  selector: 'app-manual-mappings',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe],
  templateUrl: './manual-mappings.component.html',
  styleUrl: './manual-mappings.component.scss',
})
export class ManualMappingsComponent implements OnInit {
  private animeService = inject(AnimeService);
  private destroyRef = inject(DestroyRef);

  readonly loading = signal(true);
  readonly mappings = signal<ManualMapping[]>([]);
  readonly error = signal(false);
  readonly deletingId = signal<string | null>(null);

  ngOnInit(): void {
    this.animeService
      .getAllManualMappings()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (data) => {
          this.mappings.set(data);
          this.loading.set(false);
        },
        error: () => {
          this.error.set(true);
          this.loading.set(false);
        },
      });
  }

  deleteMapping(id: string): void {
    this.deletingId.set(id);
    this.animeService
      .deleteManualMapping(id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.mappings.update(list => list.filter(m => m.id !== id));
          this.deletingId.set(null);
        },
        error: () => {
          this.deletingId.set(null);
        },
      });
  }

  formatDate(isoString: string): string {
    try {
      return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium' }).format(new Date(isoString));
    } catch {
      return isoString;
    }
  }
}
