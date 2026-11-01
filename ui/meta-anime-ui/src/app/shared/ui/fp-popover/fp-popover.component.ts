import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  HostListener,
  afterNextRender,
  computed,
  inject,
  input,
  output,
} from '@angular/core';
import { FormsModule } from '@angular/forms';

/** One selectable option in the popover list. `nsfw` renders the warning-tinted variant. */
export interface FpOption {
  key: string;
  label: string;
  nsfw?: boolean;
}

/** Payload of `itemToggle` — the key plus the originating click (parent handlers expect it). */
export interface FpItemEvent {
  key: string;
  event: MouseEvent;
}

/**
 * FpPopoverComponent
 *
 * Fixed-overlay multi-select popover (search + option list + AND/OR operator
 * toggle + clear-all/done footer). Extracted from the anime-list filter popups
 * (genre / theme / demographic) — the three call sites were byte-identical.
 *
 * All labels arrive pre-translated from the call site; every interaction is
 * surfaced as an output so the owning component keeps its exact filter state
 * flows (toggle/clear/operator mutation, mutual exclusion, preset invalidation).
 *
 * A11y: role=dialog/aria-modal overlay with focus moved in on open, restored to
 * the trigger on close, Escape to close and a Tab focus trap while open. The
 * search filter is a pure computed so the option list is not re-filtered (and
 * re-allocated) on every change-detection run.
 */
@Component({
  selector: 'app-ui-fp-popover',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './fp-popover.component.html',
  styleUrl: './fp-popover.component.scss',
})
export class FpPopoverComponent {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  /** Popup heading (pre-translated). */
  readonly title = input.required<string>();
  /** Options to render. */
  readonly items = input.required<FpOption[]>();
  /** Currently selected keys. */
  readonly selected = input<string[]>([]);
  /** External search text (owned by the call site so it survives close/reopen). */
  readonly search = input<string>('');
  /** Demographic-style popups render no search box. */
  readonly searchable = input(true);
  /** Current AND/OR operator. */
  readonly operator = input<'or' | 'and'>('or');
  /** Operator toggle is only rendered once more than one item is selected. */
  readonly showOperator = input(false);
  /** Pre-translated labels. */
  readonly searchPlaceholder = input('');
  readonly clearLabel = input('');
  readonly doneLabel = input('');
  readonly closeLabel = input('');

  /** Backdrop / close / done all end the interaction. */
  readonly closed = output<void>();
  /** Search text changed (typing or clear button). */
  readonly searchChange = output<string>();
  /** Option row clicked — parent performs the selection toggle. */
  readonly itemToggle = output<FpItemEvent>();
  /** Clear-all clicked; forwards the MouseEvent (parent stops propagation). */
  readonly clearAll = output<MouseEvent>();
  /** Operator button clicked. */
  readonly operatorChange = output<'or' | 'and'>();

  /** Options matching the search text (label or raw key, case-insensitive). */
  readonly filteredItems = computed<FpOption[]>(() => {
    const query = this.search().toLowerCase();
    const items = this.items();
    if (!query) return items;
    return items.filter(
      (item) => item.label.toLowerCase().includes(query) || item.key.toLowerCase().includes(query),
    );
  });

  private readonly selectedSet = computed(() => new Set(this.selected()));

  isSelected(key: string): boolean {
    return this.selectedSet().has(key);
  }

  /** Focus is returned here when the popover closes (the trigger button). */
  private readonly returnFocusTo: HTMLElement | null =
    (document.activeElement as HTMLElement) ?? null;

  constructor() {
    // Move focus into the dialog once rendered (search box first when present).
    afterNextRender(() => {
      const popup = this.popupEl();
      if (!popup) return;
      const target =
        popup.querySelector<HTMLElement>('.fp-popup__search-input') ??
        popup.querySelector<HTMLElement>('.fp-option') ??
        popup.querySelector<HTMLElement>('.fp-popup__close');
      target?.focus();
    });
  }

  ngOnDestroy(): void {
    const el = this.returnFocusTo;
    if (el && el.isConnected) el.focus();
  }

  private popupEl(): HTMLElement | null {
    return this.host.nativeElement.querySelector<HTMLElement>('.fp-popup');
  }

  /** Escape closes the popover (the only open fp popover at a time — mutual exclusion). */
  @HostListener('document:keydown.escape')
  onEscape(): void {
    this.closed.emit();
  }

  /** Tab focus trap: keep keyboard focus inside the popup while it is open. */
  @HostListener('document:keydown.tab', ['$event'])
  onTab(event: Event): void {
    const keyEvent = event as KeyboardEvent;
    const popup = this.popupEl();
    if (!popup) return;
    const focusables = Array.from(
      popup.querySelectorAll<HTMLElement>(
        'button:not([disabled]), input:not([disabled]), [href], [tabindex]:not([tabindex="-1"])',
      ),
    );
    if (focusables.length === 0) return;
    const first = focusables[0];
    const last = focusables[focusables.length - 1];
    const active = document.activeElement;
    if (!(active instanceof HTMLElement) || !popup.contains(active)) {
      event.preventDefault();
      (keyEvent.shiftKey ? last : first).focus();
    } else if (keyEvent.shiftKey && active === first) {
      event.preventDefault();
      last.focus();
    } else if (!keyEvent.shiftKey && active === last) {
      event.preventDefault();
      first.focus();
    }
  }

  onOptionClick(key: string, event: MouseEvent): void {
    event.stopPropagation(); // keep the popup open while multi-selecting
    this.itemToggle.emit({ key, event });
  }

  onClearAll(event: MouseEvent): void {
    event.stopPropagation();
    this.clearAll.emit(event);
  }

  onSearchInput(value: string): void {
    this.searchChange.emit(value);
  }

  clearSearch(event: MouseEvent): void {
    event.stopPropagation();
    this.searchChange.emit('');
  }

  setOperator(op: 'or' | 'and', event: MouseEvent): void {
    event.stopPropagation();
    this.operatorChange.emit(op);
  }
}
