import { ChangeDetectionStrategy, Component, OnInit, inject, signal, computed } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '../i18n/translate.pipe';

export interface AnimeFilter {
  title: string;
  genre: string;
  year: string;
}

const STORAGE_KEY = 'meta-anime-last-filter';

const GENRES = [
  'Action',
  'Adventure',
  'Comedy',
  'Drama',
  'Fantasy',
  'Horror',
  'Mecha',
  'Mystery',
  'Romance',
  'Sci-Fi',
  'Slice of Life',
  'Sports',
  'Supernatural',
  'Thriller',
];

@Component({
  selector: 'app-home',
  standalone: true,
  imports: [FormsModule, TranslatePipe],
  templateUrl: './home.component.html',
  styleUrl: './home.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class HomeComponent implements OnInit {
  readonly genres = GENRES;

  readonly filter = signal<AnimeFilter>({ title: '', genre: '', year: '' });
  readonly hasSearched = signal(false);

  readonly currentYear = new Date().getFullYear();
  readonly years = Array.from({ length: 40 }, (_, i) =>
    String(this.currentYear - i),
  );

  readonly isSearchEnabled = computed(
    () => !!(this.filter().title || this.filter().genre || this.filter().year),
  );

  ngOnInit(): void {
    const saved = localStorage.getItem(STORAGE_KEY);
    if (saved) {
      try {
        const parsed = JSON.parse(saved) as AnimeFilter;
        this.filter.set(parsed);
        if (this.isSearchEnabled()) {
          this.search();
        }
      } catch {
        localStorage.removeItem(STORAGE_KEY);
      }
    }
  }

  updateFilter(partial: Partial<AnimeFilter>): void {
    this.filter.update((f) => ({ ...f, ...partial }));
    this.persistFilter();
  }

  search(): void {
    if (!this.isSearchEnabled()) return;
    this.persistFilter();
    this.hasSearched.set(true);
  }

  private persistFilter(): void {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(this.filter()));
  }
}
