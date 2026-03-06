import { Injectable, signal } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class AnimeImageGalleryService {
  /** The current set of images displayed in the gallery. `null` means gallery is closed. */
  readonly images = signal<string[] | null>(null);
  /** The index of the currently displayed image. */
  readonly activeIndex = signal(0);

  /**
   * Opens the gallery with the given images.
   * @param images Array of image URLs to display.
   * @param startIndex Index of the first image to show (default 0).
   */
  open(images: string[], startIndex = 0): void {
    if (!images.length) return;
    this.images.set([...images]);
    this.activeIndex.set(Math.max(0, Math.min(startIndex, images.length - 1)));
  }

  close(): void {
    this.images.set(null);
    this.activeIndex.set(0);
  }

  next(): void {
    const imgs = this.images();
    if (!imgs) return;
    this.activeIndex.update((i) => (i + 1) % imgs.length);
  }

  prev(): void {
    const imgs = this.images();
    if (!imgs) return;
    this.activeIndex.update((i) => (i - 1 + imgs.length) % imgs.length);
  }
}
