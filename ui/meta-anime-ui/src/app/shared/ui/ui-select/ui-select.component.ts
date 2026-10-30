import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  HostListener,
  OnDestroy,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';

/** One dropdown entry — `value` is emitted verbatim on selection. */
export interface UiSelectOption {
  label: string;
  value: string | number;
}

/**
 * UiSelectComponent
 *
 * Custom dark-mode dropdown replacing the native <select>, whose OS popup
 * renders as a white menu in dark mode. The menu is positioned `fixed` from
 * the trigger rect so it escapes overflow-hidden/auto ancestors (modals,
 * panels, scrollable tables) without per-call-site overflow surgery; it is
 * re-anchored on scroll (capture phase, so inner scrollers count) and resize.
 *
 * Labels are final strings — translate at the call site.
 */
@Component({
  selector: 'app-ui-select',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './ui-select.component.html',
  styleUrl: './ui-select.component.scss',
})
export class UiSelectComponent implements OnDestroy {
  private readonly host = inject(ElementRef<HTMLElement>);

  readonly options = input<UiSelectOption[]>([]);
  readonly value = input<string | number>('');
  readonly placeholder = input<string>('');
  /** Compact variant for dense toolbars (rows-per-page, match-mode op). */
  readonly small = input<boolean>(false);
  /** Mirrors the native select [disabled] state (e.g. settings rows while saving). */
  readonly disabled = input<boolean>(false);
  /** Mirrors the native select [attr.aria-label] — translated at the call site. */
  readonly ariaLabel = input<string | null>(null);

  readonly selectionChange = output<string | number>();

  readonly isOpen = signal(false);

  /** Fixed-position anchor for the menu (viewport coordinates). */
  readonly menuPos = signal<{ top: number; left: number; width: number; maxHeight: number } | null>(null);

  /** Selected option's label; falls back to the placeholder when no option matches. */
  readonly selectedLabel = computed(() => {
    const match = this.options().find(o => o.value === this.value());
    return match ? match.label : this.placeholder();
  });

  toggle(): void {
    if (this.disabled()) return;
    if (this.isOpen()) {
      this.close();
    } else {
      this.open();
    }
  }

  select(value: string | number): void {
    this.close();
    // Native <select> fires change only on an actual change — mirror that.
    if (value !== this.value()) {
      this.selectionChange.emit(value);
    }
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    if (this.isOpen() && !this.host.nativeElement.contains(event.target as Node)) {
      this.close();
    }
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    this.close();
  }

  ngOnDestroy(): void {
    this.teardownViewportListeners();
  }

  private readonly onViewportChange = (): void => {
    this.updateMenuPos();
  };

  private open(): void {
    this.isOpen.set(true);
    this.updateMenuPos();
    // capture: true so scrolls of inner overflow containers re-anchor the menu too
    document.addEventListener('scroll', this.onViewportChange, true);
    window.addEventListener('resize', this.onViewportChange);
  }

  private close(): void {
    this.isOpen.set(false);
    this.menuPos.set(null);
    this.teardownViewportListeners();
  }

  private teardownViewportListeners(): void {
    document.removeEventListener('scroll', this.onViewportChange, true);
    window.removeEventListener('resize', this.onViewportChange);
  }

  /**
   * Menu geometry: directly under the trigger (6px gap), clamped to the viewport.
   * `position: fixed` resolves against the nearest transformed ancestor (e.g. a
   * card with a hover/entrance transform) instead of the viewport — walk up and
   * subtract that containing block's origin so coordinates land where intended.
   */
  private updateMenuPos(): void {
    const host = this.host.nativeElement;
    const rect = host.getBoundingClientRect();
    const width = rect.width;
    const topVp = rect.bottom + 6;
    const leftVp = Math.max(8, Math.min(rect.left, window.innerWidth - width - 8));

    let ox = 0;
    let oy = 0;
    let anchor: HTMLElement | null = host.parentElement;
    while (anchor && anchor !== document.documentElement) {
      const s = getComputedStyle(anchor);
      const containing =
        s.transform !== 'none' ||
        s.filter !== 'none' ||
        s.backdropFilter !== 'none' ||
        s.perspective !== 'none' ||
        /transform|filter|perspective/.test(s.willChange) ||
        /paint|layout|strict|content/.test(s.contain);
      if (containing) {
        const a = anchor.getBoundingClientRect();
        ox = a.left + (anchor.clientLeft ?? 0);
        oy = a.top + (anchor.clientTop ?? 0);
        break;
      }
      anchor = anchor.parentElement;
    }

    this.menuPos.set({
      top: topVp - oy,
      left: leftVp - ox,
      width,
      // 15rem cap from the design; shrink further when the viewport bottom is near
      maxHeight: Math.max(96, Math.min(240, window.innerHeight - topVp - 12)),
    });
  }
}
