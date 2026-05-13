import {
  ChangeDetectionStrategy,
  Component,
  HostListener,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { TranslatePipe } from '@ngx-translate/core';
import { SeiyuuNexusService } from './seiyuu-nexus.service';
import { NexusNode } from '../models/anime.models';

const LANG_FLAGS: Record<string, string> = {
  japanese:   '🇯🇵',
  english:    '🇺🇸',
  french:     '🇫🇷',
  german:     '🇩🇪',
  spanish:    '🇪🇸',
  portuguese: '🇧🇷',
  italian:    '🇮🇹',
  korean:     '🇰🇷',
  chinese:    '🇨🇳',
};

export interface DubEntry {
  va:       NexusNode;
  language: string;
  flag:     string;
}

export interface CharacterEntry {
  char: NexusNode;
  dubs: DubEntry[];
}

@Component({
  selector: 'app-seiyuu-nexus',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, TranslatePipe],
  templateUrl: './seiyuu-nexus.component.html',
  styleUrl: './seiyuu-nexus.component.scss',
})
export class SeiyuuNexusComponent {
  protected readonly svc = inject(SeiyuuNexusService);

  protected readonly focusedVaId = signal<string | null>(null);
  protected readonly langFilter  = signal<string>('');

  /** Unique dub languages present in the data, sorted. */
  protected readonly availableLangs = computed<string[]>(() => {
    const nexus = this.svc.nexus();
    if (!nexus) return [];
    return [...new Set(
      nexus.edges
        .filter(e => e.relation === 'voices' && e.language)
        .map(e => e.language!)
    )].sort();
  });

  /** Auto-select Japanese (or first available) when data arrives. */
  private readonly _initLang = effect(() => {
    const langs = this.availableLangs();
    if (langs.length > 0 && this.langFilter() === '') {
      this.langFilter.set(langs.find(l => l.toLowerCase() === 'japanese') ?? langs[0]);
    }
  });

  /** Returns the emoji flag for a language name, or a short 2-letter fallback. */
  protected getFlag(language: string): string {
    return LANG_FLAGS[language.toLowerCase()] ?? language.slice(0, 2).toUpperCase();
  }

  /**
   * Cast table rows grouped by character.
   * Each entry has one character + all their VAs that match the active language filter.
   * Sorted: Main characters first, then alphabetical by character name.
   */
  protected readonly charRows = computed<CharacterEntry[]>(() => {
    const nexus = this.svc.nexus();
    if (!nexus) return [];

    const lang    = this.langFilter();
    const nodeMap = new Map(nexus.nodes.map(n => [n.id, n]));

    // Build a map: charId → DubEntry[]
    const charMap = new Map<string, { char: NexusNode; dubs: DubEntry[] }>();

    nexus.edges
      .filter(e => e.relation === 'voices' && (lang === '' || e.language === lang))
      .forEach(e => {
        const char = nodeMap.get(e.toId);
        const va   = nodeMap.get(e.fromId);
        if (!char || !va) return;

        if (!charMap.has(char.id)) {
          charMap.set(char.id, { char, dubs: [] });
        }
        charMap.get(char.id)!.dubs.push({
          va,
          language: e.language ?? '',
          flag:     this.getFlag(e.language ?? ''),
        });
      });

    return [...charMap.values()].sort((a, b) => {
      const roleOrder = (e: CharacterEntry) => e.char.role?.toLowerCase() === 'main' ? 0 : 1;
      return roleOrder(a) - roleOrder(b) || a.char.label.localeCompare(b.char.label);
    });
  });

  protected readonly lastUpdatedLocal = computed(() => {
    const raw = this.svc.nexus()?.lastUpdated;
    if (!raw) return null;
    const d   = new Date(raw);
    const pad = (n: number) => String(n).padStart(2, '0');
    return `${pad(d.getDate())}/${pad(d.getMonth() + 1)}/${d.getFullYear()} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
  });

  protected setLangFilter(lang: string): void {
    this.langFilter.set(lang);
    this.focusedVaId.set(null);
    this.svc.focusedVa.set(null);
  }

  protected selectVa(va: NexusNode): void {
    if (va.personId == null) return;
    this.focusedVaId.set(va.id);
    this.svc.focusVa(va.personId);
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.svc.isOpen()) this.svc.close();
  }

  protected close(): void {
    this.svc.close();
  }
}

