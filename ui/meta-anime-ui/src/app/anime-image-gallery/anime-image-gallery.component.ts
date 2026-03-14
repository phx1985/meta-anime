import {
  ChangeDetectionStrategy,
  Component,
  HostListener,
  inject,
  signal,
  effect,
} from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { AnimeImageGalleryService } from '../services/anime-image-gallery.service';

@Component({
  selector: 'app-anime-image-gallery',
  standalone: true,
  imports: [TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './anime-image-gallery.component.html',
  styleUrl: './anime-image-gallery.component.scss',
})
export class AnimeImageGalleryComponent {
  protected gallery = inject(AnimeImageGalleryService);

  /** True while the active image is loading/transitioning. */
  protected imageLoading = signal(false);

  /** Direction hint shown as an arrow overlay during an active touch swipe. */
  protected swipeHint = signal<'prev' | 'next' | null>(null);

  /** Touch start coordinates for swipe navigation. */
  private swipeStartX = 0;
  private swipeStartY = 0;

  constructor() {
    // Whenever the active image changes, show the loading spinner until (load) fires.
    effect(() => {
      const idx = this.gallery.activeIndex();
      const imgs = this.gallery.images();
      if (imgs !== null && imgs.length > 0 && idx >= 0) {
        this.imageLoading.set(true);
      }
    });
  }

  get images(): string[] {
    return this.gallery.images() ?? [];
  }

  get activeIndex(): number {
    return this.gallery.activeIndex();
  }

  get activeImage(): string {
    return this.images[this.activeIndex] ?? '';
  }

  get hasMultiple(): boolean {
    return this.images.length > 1;
  }

  onImageLoad(): void {
    this.imageLoading.set(false);
  }

  onImageError(): void {
    this.imageLoading.set(false);
  }

  @HostListener('document:keydown', ['$event'])
  onKeydown(event: KeyboardEvent): void {
    if (!this.gallery.images()) return;
    if (event.key === 'Escape') {
      this.gallery.close();
    } else if (event.key === 'ArrowRight') {
      this.gallery.next();
    } else if (event.key === 'ArrowLeft') {
      this.gallery.prev();
    }
  }

  /** Touch swipe navigation — horizontal swipe > 40 px navigates prev/next. */
  @HostListener('touchstart', ['$event'])
  onTouchStart(event: TouchEvent): void {
    if (!this.gallery.images()) return;
    this.swipeStartX = event.touches[0].clientX;
    this.swipeStartY = event.touches[0].clientY;
  }

  /** Show directional hint arrow while finger is actively swiping. */
  @HostListener('touchmove', ['$event'])
  onTouchMove(event: TouchEvent): void {
    if (!this.gallery.images()) return;
    const deltaX = event.touches[0].clientX - this.swipeStartX;
    if (Math.abs(deltaX) > 25) {
      this.swipeHint.set(deltaX < 0 ? 'next' : 'prev');
    } else {
      this.swipeHint.set(null);
    }
  }

  @HostListener('touchend', ['$event'])
  onTouchEnd(event: TouchEvent): void {
    this.swipeHint.set(null);
    if (!this.gallery.images()) return;
    const deltaX = event.changedTouches[0].clientX - this.swipeStartX;
    const deltaY = event.changedTouches[0].clientY - this.swipeStartY;
    // Only handle predominantly horizontal swipes (deltaX > 40px and > 1.2× deltaY)
    if (Math.abs(deltaX) < 40 || Math.abs(deltaX) < Math.abs(deltaY) * 1.2) return;
    if (deltaX < 0) {
      this.gallery.next();
    } else {
      this.gallery.prev();
    }
  }

  onBackdropClick(): void {
    this.gallery.close();
  }
}
