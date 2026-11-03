import {
  ChangeDetectionStrategy,
  Component,
  computed,
  ElementRef,
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
  private readonly hostEl = inject(ElementRef<HTMLElement>);

  /** True while the active image is loading/transitioning. */
  protected imageLoading = signal(false);

  /** Direction hint shown as an arrow overlay during an active touch swipe. */
  protected swipeHint = signal<'prev' | 'next' | null>(null);

  /** Touch start coordinates for swipe navigation. */
  private swipeStartX = 0;
  private swipeStartY = 0;

  /** Focus management state (dialog open/close). */
  private wasOpen = false;
  private triggerEl: HTMLElement | null = null;
  private focusTimer: ReturnType<typeof setTimeout> | null = null;

  // Memoized view models — plain getters would allocate a fresh [] on every
  // change-detection read; computeds cache until the source signals change.
  private readonly imagesSig = computed(() => this.gallery.images() ?? []);
  private readonly activeIndexSig = computed(() => this.gallery.activeIndex());
  private readonly activeImageSig = computed(
    () => this.imagesSig()[this.activeIndexSig()] ?? '',
  );
  private readonly hasMultipleSig = computed(() => this.imagesSig().length > 1);

  constructor() {
    // Whenever the active image changes, show the loading spinner until (load) fires.
    effect(() => {
      const idx = this.gallery.activeIndex();
      const imgs = this.gallery.images();
      if (imgs !== null && imgs.length > 0 && idx >= 0) {
        this.imageLoading.set(true);
      }
    });

    // Dialog focus management: move focus into the gallery on open and
    // restore it to the trigger element on close.
    effect((onCleanup) => {
      const open = this.gallery.images() !== null;
      if (open && !this.wasOpen) {
        this.triggerEl = (document.activeElement as HTMLElement) ?? null;
        this.focusTimer = setTimeout(() => {
          this.focusTimer = null;
          const closeBtn = this.hostEl.nativeElement.querySelector('.gallery-close') as HTMLElement | null;
          closeBtn?.focus();
        }, 0);
        onCleanup(() => {
          if (this.focusTimer !== null) {
            clearTimeout(this.focusTimer);
            this.focusTimer = null;
          }
        });
      } else if (!open && this.wasOpen) {
        this.triggerEl?.focus();
        this.triggerEl = null;
      }
      this.wasOpen = open;
    });
  }

  get images(): string[] {
    return this.imagesSig();
  }

  get activeIndex(): number {
    return this.activeIndexSig();
  }

  get activeImage(): string {
    return this.activeImageSig();
  }

  get hasMultiple(): boolean {
    return this.hasMultipleSig();
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
