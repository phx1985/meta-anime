import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  computed,
  effect,
  inject,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { SeiyuuNexusService } from './seiyuu-nexus.service';
import { NexusNode, SeiyuuNexusResponse } from '../models/anime.models';

// LanguageV2 -> ISO code chip (uppercase, rendered as text — no emoji in UI).
// languageV2 arrives as full English names ('Japanese', 'English', …); anything
// unmapped falls back to the first two letters uppercased.
const LANG_CODES: Record<string, string> = {
  japanese: 'JP',
  english: 'EN',
  french: 'FR',
  german: 'DE',
  spanish: 'ES',
  italian: 'IT',
  portuguese: 'PT',
  korean: 'KO',
  chinese: 'ZH',
  arabic: 'AR',
  russian: 'RU',
};

function langCode(language: string): string {
  const key = language.trim().toLowerCase();
  return LANG_CODES[key] ?? language.trim().slice(0, 2).toUpperCase();
}

function monogram(name: string): string {
  const trimmed = name.trim();
  return trimmed ? trimmed[0].toUpperCase() : '?';
}

/**
 * Poster fallback for anime titles: initials of up to the first two words
 * ('Cowboy Bebop' → 'CB'). Never derived from an id — labels are always titles.
 */
function posterMonogram(title: string): string {
  const words = title.trim().split(/\s+/).filter(Boolean);
  if (words.length === 0) return '?';
  return words
    .slice(0, 2)
    .map(w => w[0].toUpperCase())
    .join('');
}

export interface LangPill {
  lang: string;
  code: string;
  count: number;
}

export interface AppearsInEntry {
  /** The other anime's graph node (label = title, imageUrl = poster when known). */
  node: NexusNode;
  /** Dub languages this VA voiced in that anime (from the appearsIn edge label). */
  languages: string[];
}

export interface VaEntry {
  /** Stable key for the (character, VA) pair. */
  key: string;
  node: NexusNode;
  personId: number | null;
  /** Dub languages this VA covers for this character. */
  languages: string[];
  /** Cross-library constellation: other anime this VA appears in. */
  appearsIn: AppearsInEntry[];
}

export interface CharRow {
  char: NexusNode;
  isMain: boolean;
  vas: VaEntry[];
}

export interface VaRoleLine {
  characterName: string;
  language: string;
  isMain: boolean;
}

@Component({
  selector: 'app-seiyuu-nexus',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe],
  templateUrl: './seiyuu-nexus.component.html',
  styleUrl: './seiyuu-nexus.component.scss',
})
export class SeiyuuNexusComponent {
  protected readonly svc = inject(SeiyuuNexusService);
  private readonly destroyRef = inject(DestroyRef);

  /**
   * Emits the MAL id of a cross-library anime when its mini-card is clicked.
   * The host opens the app-level anime detail panel with it (same mechanism as
   * Franchise Constellation's nodeSelected).
   */
  readonly animeSelected = output<string>();

  /** Active language filter ('ALL' sentinel + language name). */
  protected readonly selectedLanguage = signal('ALL');
  /** True once the user has clicked any pill — blocks every auto-selection.
   *  (Spec name: `userInteractedWithFilter` — kept descriptive locally.) */
  protected readonly hasUserSelectedLanguage = signal(false);
  /** Image keys that failed to load — rendered as monogram fallbacks. */
  protected readonly failedImgs = signal<ReadonlySet<string>>(new Set());
  /** VA entry key whose cross-library constellation list is expanded. */
  protected readonly constellationOpenKey = signal<string | null>(null);

  protected readonly skeletonRows = [0, 1, 2, 3, 4, 5];

  // ── Language filter ────────────────────────────────────────────────────────

  /** Unique dub languages present in the data, most populated first. */
  protected readonly langPills = computed<LangPill[]>(() => {
    const counts = new Map<string, number>();
    for (const edge of this.svc.nexus()?.edges ?? []) {
      if (edge.relation !== 'voices' || !edge.language) continue;
      counts.set(edge.language, (counts.get(edge.language) ?? 0) + 1);
    }
    return [...counts.entries()]
      .map(([lang, count]) => ({ lang, code: langCode(lang), count }))
      .sort((a, b) => b.count - a.count || a.lang.localeCompare(b.lang));
  });

  /**
   * State persistence (critical): `hasUserSelectedLanguage` is reset ONLY when
   * the anime changes. Clicking "All" marks the flag and the selection never
   * auto-reverts to the first language afterwards.
   */
  private lastLangResetMalId: string | null = null;

  private readonly _resetLangOnAnimeChange = effect(() => {
    const malId = this.svc.animeMalId();
    if (malId === this.lastLangResetMalId) return;
    this.lastLangResetMalId = malId;
    this.hasUserSelectedLanguage.set(false);
    this.selectedLanguage.set('ALL');
    this.constellationOpenKey.set(null);
  });

  /** Auto-select the first language for a freshly loaded cast set (never over a user choice). */
  private autoSelectedFor: SeiyuuNexusResponse | null = null;

  private readonly _autoSelectLang = effect(() => {
    const nexus = this.svc.nexus();
    const pills = this.langPills();
    if (!nexus || pills.length === 0 || this.autoSelectedFor === nexus) return;
    if (this.hasUserSelectedLanguage()) return;
    this.autoSelectedFor = nexus;
    this.selectedLanguage.set(pills[0].lang);
  });

  protected selectLanguage(lang: string): void {
    this.selectedLanguage.set(lang);
    this.hasUserSelectedLanguage.set(true);
    this.constellationOpenKey.set(null);
  }

  // ── Cast table ─────────────────────────────────────────────────────────────

  /** Character rows with their VA entries, filtered by the active language pill. */
  protected readonly charRows = computed<CharRow[]>(() => {
    const nexus = this.svc.nexus();
    if (!nexus) return [];

    const nodeMap = new Map(nexus.nodes.map(n => [n.id, n]));
    const lang = this.selectedLanguage();
    const rows = new Map<string, CharRow>();
    const vaLangs = new Map<string, Set<string>>();

    for (const edge of nexus.edges) {
      if (edge.relation !== 'voices') continue;
      const char = nodeMap.get(edge.toId);
      const va = nodeMap.get(edge.fromId);
      if (!char || !va) continue;

      const key = `${char.id}|${va.id}`;
      let langs = vaLangs.get(key);
      if (!langs) {
        langs = new Set<string>();
        vaLangs.set(key, langs);
      }
      if (edge.language) langs.add(edge.language);

      let row = rows.get(char.id);
      if (!row) {
        row = { char, isMain: char.role?.toLowerCase() === 'main', vas: [] };
        rows.set(char.id, row);
      }
      if (!row.vas.some(v => v.key === key)) {
        row.vas.push({
          key,
          node: va,
          personId: va.personId ?? null,
          languages: [],
          appearsIn: this.constellationFor(nexus, nodeMap, va.id),
        });
      }
    }

    // Resolve language sets + apply the active filter (rows without any
    // matching VA are dropped entirely).
    const result: CharRow[] = [];
    for (const row of rows.values()) {
      const vas = row.vas
        .map(v => ({ ...v, languages: [...(vaLangs.get(v.key) ?? [])].sort() }))
        .filter(v => lang === 'ALL' || v.languages.includes(lang));
      if (vas.length > 0) result.push({ ...row, vas });
    }

    return result.sort((a, b) => {
      const roleOrder = (r: CharRow) => (r.isMain ? 0 : 1);
      return roleOrder(a) - roleOrder(b) || a.char.label.localeCompare(b.char.label);
    });
  });

  /** 200 with no cast data at all — the definitive empty state (never conflated with error). */
  protected readonly isEmpty = computed(() => {
    const nexus = this.svc.nexus();
    return !!nexus && !nexus.edges.some(e => e.relation === 'voices');
  });

  protected readonly lastUpdatedLocal = computed(() => {
    const raw = this.svc.nexus()?.lastUpdated;
    if (!raw) return null;
    const d = new Date(raw);
    if (Number.isNaN(d.getTime())) return null;
    const pad = (n: number) => String(n).padStart(2, '0');
    return `${pad(d.getDate())}/${pad(d.getMonth() + 1)}/${d.getFullYear()} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
  });

  // ── VA inspector (desktop drawer / mobile bottom sheet) ────────────────────

  protected readonly profileOpen = computed(() => this.svc.focusedVaPersonId() != null);

  protected readonly vaPortrait = computed(() => {
    const profile = this.svc.vaProfile();
    const node = this.focusedVaNode();
    return {
      name: profile?.name ?? node?.label ?? '',
      imageUrl: profile?.imageUrl ?? node?.imageUrl ?? null,
    };
  });

  /** Languages this VA dubs in across the current anime. */
  protected readonly vaLanguages = computed<string[]>(() => {
    const nexus = this.svc.nexus();
    const personId = this.svc.focusedVaPersonId();
    if (!nexus || personId == null) return [];
    const langs = new Set<string>();
    for (const edge of nexus.edges) {
      if (edge.relation !== 'voices' || !edge.language) continue;
      const va = nexus.nodes.find(n => n.id === edge.fromId);
      if (va?.personId === personId) langs.add(edge.language);
    }
    return [...langs].sort();
  });

  /** Their roles in this anime (character + role + dub language). */
  protected readonly vaRoles = computed<VaRoleLine[]>(() => {
    const nexus = this.svc.nexus();
    const personId = this.svc.focusedVaPersonId();
    if (!nexus || personId == null) return [];
    const lines = new Map<string, VaRoleLine>();
    for (const edge of nexus.edges) {
      if (edge.relation !== 'voices') continue;
      const va = nexus.nodes.find(n => n.id === edge.fromId);
      if (va?.personId !== personId) continue;
      const char = nexus.nodes.find(n => n.id === edge.toId);
      if (!char) continue;
      const key = `${char.id}|${edge.language ?? ''}`;
      if (!lines.has(key)) {
        lines.set(key, {
          characterName: char.label,
          language: edge.language ?? '',
          isMain: char.role?.toLowerCase() === 'main',
        });
      }
    }
    return [...lines.values()];
  });

  /** Cross-library constellation: other anime in the library this VA appears in. */
  protected readonly vaAppearsIn = computed<AppearsInEntry[]>(() => {
    const nexus = this.svc.nexus();
    const personId = this.svc.focusedVaPersonId();
    if (!nexus || personId == null) return [];
    const nodeMap = new Map(nexus.nodes.map(n => [n.id, n]));
    const va = nexus.nodes.find(n => n.type === 'va' && n.personId === personId);
    return va ? this.constellationFor(nexus, nodeMap, va.id) : [];
  });

  protected selectVa(entry: VaEntry): void {
    if (entry.personId == null) return;
    this.svc.focusVa(entry.personId);
  }

  protected closeProfile(): void {
    this.svc.closeProfile();
  }

  protected toggleConstellation(key: string): void {
    this.constellationOpenKey.set(this.constellationOpenKey() === key ? null : key);
  }

  /**
   * Opens the anime detail panel for a cross-library entry. The nexus overlay is a
   * top-layer <dialog>, which cannot sit *behind* a z-indexed panel, so the clean
   * z-order is: close the overlay (state stays cached), then let the host open the
   * app-level detail panel on top of whatever is underneath.
   */
  protected openAnime(anime: NexusNode): void {
    if (!anime.malId) return;
    this.svc.close();
    this.animeSelected.emit(anime.malId);
  }

  // ── Rendering helpers ──────────────────────────────────────────────────────

  /**
   * AniList birthdays may be partial ('--05-12', '1985--') or null. Render what
   * exists (empty segments dropped) and return null so the cell is hidden when
   * nothing is known — never a '—' placeholder.
   */
  protected formatBirthday(raw: string | null | undefined): string | null {
    if (!raw) return null;
    const parts = raw
      .split('-')
      .map(part => part.trim())
      .filter(part => part.length > 0);
    return parts.length > 0 ? parts.join('-') : null;
  }

  protected langCodeOf(language: string): string {
    return langCode(language);
  }

  protected monogramOf(name: string): string {
    return monogram(name);
  }

  /** Poster fallback: title initials ('Cowboy Bebop' → 'CB'), never an id. */
  protected posterMonogramOf(title: string): string {
    return posterMonogram(title);
  }

  protected imgOk(key: string, url: string | null | undefined): boolean {
    return !!url && !this.failedImgs().has(key);
  }

  protected markImgFailed(key: string): void {
    const next = new Set(this.failedImgs());
    next.add(key);
    this.failedImgs.set(next);
  }

  // ── Overlay shell (top-layer <dialog>) ─────────────────────────────────────

  private readonly dialogEl = viewChild<ElementRef<HTMLDialogElement>>('nexusDialog');

  /**
   * Drive the native <dialog> from the service state: showModal() puts the
   * overlay in the top layer (escapes every transform/filter/overflow ancestor
   * by construction). Escape is handled natively by the dialog (`close` event
   * syncs service state back).
   */
  private readonly _dialogSync = effect(() => {
    const el = this.dialogEl()?.nativeElement;
    if (!el) return;
    if (this.svc.isOpen()) {
      if (!el.open) el.showModal();
    } else if (el.open) {
      el.close();
    }
  });

  protected retry(): void {
    this.svc.retry();
  }

  protected closeOverlay(): void {
    this.svc.close();
  }

  /** Backdrop click (the dialog element itself, outside the shell) closes the overlay. */
  protected onOverlayClick(event: MouseEvent): void {
    if (event.target === event.currentTarget) this.svc.close();
  }

  /** Native dialog `close` (Escape, close()) — sync service state, no loop. */
  protected onDialogClosed(): void {
    if (this.svc.isOpen()) this.svc.close();
  }

  // ── Bottom-sheet swipe-to-close ────────────────────────────────────────────

  private sheetDragStartY = 0;
  private sheetDragging = false;

  protected onSheetTouchStart(event: TouchEvent): void {
    const el = event.currentTarget as HTMLElement;
    if (el.scrollTop > 0) return;
    this.sheetDragStartY = event.touches[0]?.clientY ?? 0;
    this.sheetDragging = true;
  }

  protected onSheetTouchMove(event: TouchEvent): void {
    if (!this.sheetDragging) return;
    const el = event.currentTarget as HTMLElement;
    const dy = (event.touches[0]?.clientY ?? 0) - this.sheetDragStartY;
    el.style.translate = dy > 0 ? `0 ${dy}px` : '';
  }

  protected onSheetTouchEnd(event: TouchEvent): void {
    if (!this.sheetDragging) return;
    const el = event.currentTarget as HTMLElement;
    const dy = (event.changedTouches[0]?.clientY ?? 0) - this.sheetDragStartY;
    el.style.translate = '';
    this.sheetDragging = false;
    if (dy > 80) this.svc.closeProfile();
  }

  // ── Body scroll lock while open ────────────────────────────────────────────

  private readonly _scrollLock = effect(() => {
    document.body.style.overflow = this.svc.isOpen() ? 'hidden' : '';
  });

  constructor() {
    this.destroyRef.onDestroy(() => {
      document.body.style.overflow = '';
    });
  }

  private focusedVaNode(): NexusNode | undefined {
    const nexus = this.svc.nexus();
    const personId = this.svc.focusedVaPersonId();
    if (!nexus || personId == null) return undefined;
    return nexus.nodes.find(n => n.type === 'va' && n.personId === personId);
  }

  private constellationFor(
    nexus: SeiyuuNexusResponse,
    nodeMap: Map<string, NexusNode>,
    vaNodeId: string,
  ): AppearsInEntry[] {
    const seen = new Set<string>();
    const anime: AppearsInEntry[] = [];
    for (const edge of nexus.edges) {
      if (edge.relation !== 'appearsIn' || edge.fromId !== vaNodeId) continue;
      const node = nodeMap.get(edge.toId);
      if (!node || node.type !== 'anime' || node.isRoot || seen.has(node.id)) continue;
      seen.add(node.id);
      anime.push({
        node,
        languages: (edge.language ?? '')
          .split(',')
          .map(lang => lang.trim())
          .filter(Boolean),
      });
    }
    return anime;
  }
}
