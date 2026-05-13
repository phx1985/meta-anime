import {
  ChangeDetectionStrategy,
  Component,
  HostListener,
  OnInit,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { TranslatePipe } from '@ngx-translate/core';
import { SeiyuuNexusService } from './seiyuu-nexus.service';
import { NexusNode, NexusEdge } from '../models/anime.models';

const CANVAS_SIZE = 8000;
const ORIGIN      = CANVAS_SIZE / 2;
const X_CHARS     = ORIGIN - 500;  // character column x
const X_VAS       = ORIGIN + 500;  // VA column x
const X_ROOT      = ORIGIN;        // root anime x
const NODE_W      = 88;            // node div half-width offset
const Y_SPACING_MIN = 72;
const Y_SPACING_MAX = 140;

interface PositionedNode extends NexusNode {
  x: number;
  y: number;
}

@Component({
  selector: 'app-seiyuu-nexus',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, TranslatePipe],
  templateUrl: './seiyuu-nexus.component.html',
  styleUrl: './seiyuu-nexus.component.scss',
})
export class SeiyuuNexusComponent implements OnInit {
  protected readonly svc = inject(SeiyuuNexusService);

  protected readonly translateX = signal(0);
  protected readonly translateY = signal(0);
  protected readonly scale      = signal(1);
  protected readonly focusedNodeId = signal<string | null>(null);

  /** Currently selected language filter. Empty string means "all". */
  protected readonly langFilter = signal<string>('');

  private isDragging  = false;
  private dragStartX  = 0;
  private dragStartY  = 0;
  private dragStartTx = 0;
  private dragStartTy = 0;
  private hasDragged  = false;

  /** Unique languages present in the nexus data (from "voices" edges). */
  protected readonly availableLangs = computed<string[]>(() => {
    const nexus = this.svc.nexus();
    if (!nexus) return [];
    const langs = [...new Set(
      nexus.edges
        .filter(e => e.relation === 'voices' && e.language)
        .map(e => e.language!)
    )].sort();
    return langs;
  });

  /** Initialise langFilter to the first available language (usually Japanese) once data arrives. */
  private readonly _initLang = effect(() => {
    const langs = this.availableLangs();
    if (langs.length > 0 && this.langFilter() === '') {
      const preferred = langs.find(l => l.toLowerCase() === 'japanese') ?? langs[0];
      this.langFilter.set(preferred);
    }
  });

  /** VA node IDs that are visible under the current language filter. */
  private readonly visibleVaNodeIds = computed<Set<string>>(() => {
    const nexus = this.svc.nexus();
    if (!nexus) return new Set();
    const lang = this.langFilter();
    const voicesEdges = nexus.edges.filter(e =>
      e.relation === 'voices' && (lang === '' || e.language === lang)
    );
    return new Set(voicesEdges.map(e => e.fromId));
  });

  protected readonly positionedNodes = computed<PositionedNode[]>(() => {
    const nexus = this.svc.nexus();
    if (!nexus) return [];

    const nodes = nexus.nodes;
    const root  = nodes.find(n => n.isRoot);
    const chars = nodes.filter(n => n.type === 'character');

    const visibleVaIds = this.visibleVaNodeIds();
    const vas = nodes.filter(n => n.type === 'va' && visibleVaIds.has(n.id));

    const charCount = Math.max(chars.length, 1);
    const vaCount   = Math.max(vas.length, 1);

    const ySpacing = Math.min(
      Y_SPACING_MAX,
      Math.max(Y_SPACING_MIN, 3200 / charCount)
    );

    // Place characters evenly in the left column
    const charTotalH = (chars.length - 1) * ySpacing;
    const charY0     = ORIGIN - charTotalH / 2;
    const charPositions = new Map<string, number>(); // nodeId → y

    chars.forEach((n, i) => {
      charPositions.set(n.id, charY0 + i * ySpacing);
    });

    // Sort VAs by gravity (mean y of their connected visible characters)
    const lang = this.langFilter();
    const voicesEdges = nexus.edges.filter(e =>
      e.relation === 'voices' && (lang === '' || e.language === lang)
    );

    const sortedVas = [...vas].sort((a, b) => {
      const gravity = (vaId: string) => {
        const connectedYs = voicesEdges
          .filter(e => e.fromId === vaId)
          .map(e => charPositions.get(e.toId) ?? ORIGIN);
        return connectedYs.length > 0
          ? connectedYs.reduce((s, y) => s + y, 0) / connectedYs.length
          : ORIGIN;
      };
      return gravity(a.id) - gravity(b.id);
    });

    const vaYSpacing = Math.min(
      Y_SPACING_MAX,
      Math.max(Y_SPACING_MIN, 3200 / vaCount)
    );
    const vaTotalH = (sortedVas.length - 1) * vaYSpacing;
    const vaY0 = ORIGIN - vaTotalH / 2;

    const placed: PositionedNode[] = [];

    // Root anime: above both columns
    if (root) {
      const topY = Math.min(charY0, vaY0) - 160;
      placed.push({ ...root, x: X_ROOT, y: topY });
    }

    chars.forEach((n, i) => {
      placed.push({ ...n, x: X_CHARS, y: charY0 + i * ySpacing });
    });

    sortedVas.forEach((n, i) => {
      placed.push({ ...n, x: X_VAS, y: vaY0 + i * vaYSpacing });
    });

    return placed;
  });

  protected readonly positionedEdges = computed<Array<{
    edge: NexusEdge;
    path: string;
    dashed: boolean;
  }>>(() => {
    const nexus = this.svc.nexus();
    if (!nexus) return [];

    const nodeMap = new Map<string, PositionedNode>();
    for (const n of this.positionedNodes()) nodeMap.set(n.id, n);

    const lang = this.langFilter();

    return nexus.edges
      .filter(e => {
        if (e.relation === 'appearsIn') return false; // hidden in bipartite view
        if (e.relation === 'voices') return lang === '' || e.language === lang;
        return true; // hasCharacter edges always shown
      })
      .map(edge => {
        const from = nodeMap.get(edge.fromId);
        const to   = nodeMap.get(edge.toId);
        if (!from || !to) return null;
        return { edge, path: this.hBezierPath(from, to), dashed: false };
      })
      .filter((e): e is NonNullable<typeof e> => e !== null);
  });

  protected readonly lastUpdatedLocal = computed(() => {
    const raw = this.svc.nexus()?.lastUpdated;
    if (!raw) return null;
    const d   = new Date(raw);
    const pad = (n: number) => String(n).padStart(2, '0');
    return `${pad(d.getDate())}/${pad(d.getMonth() + 1)}/${d.getFullYear()} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
  });

  protected readonly nodeCount = computed(() => {
    const nexus = this.svc.nexus();
    if (!nexus) return { chars: 0, vas: 0 };
    return {
      chars: nexus.nodes.filter(n => n.type === 'character').length,
      vas:   this.visibleVaNodeIds().size,
    };
  });

  readonly canvasSize = CANVAS_SIZE;

  constructor() {
    effect(() => {
      const nexus = this.svc.nexus();
      if (nexus && nexus.nodes.length > 0) this.centerView();
    });
  }

  ngOnInit(): void {
    this.centerView();
  }

  private centerView(): void {
    const vw = window.innerWidth;
    const vh = window.innerHeight;
    const s  = Math.min(vw / 1200, vh / 900, 0.75);
    this.scale.set(s);
    this.translateX.set((vw - CANVAS_SIZE * s) / 2);
    this.translateY.set((vh - CANVAS_SIZE * s) / 2);
  }

  /** Horizontal S-curve bezier between two positioned nodes. */
  private hBezierPath(from: PositionedNode, to: PositionedNode): string {
    // Edge starts at right edge of from-node, ends at left edge of to-node
    const x1 = from.x + NODE_W / 2;
    const y1 = from.y;
    const x2 = to.x   - NODE_W / 2;
    const y2 = to.y;
    const cx = (x1 + x2) / 2;
    return `M ${x1} ${y1} C ${cx} ${y1}, ${cx} ${y2}, ${x2} ${y2}`;
  }

  protected get canvasTransform(): string {
    return `translate(${this.translateX()}px, ${this.translateY()}px) scale(${this.scale()})`;
  }

  protected setLangFilter(lang: string): void {
    this.langFilter.set(lang);
  }

  protected onNodeClick(node: PositionedNode): void {
    if (this.hasDragged) return;
    if (node.type === 'va' && node.personId != null) {
      this.focusedNodeId.set(node.id);
      this.svc.focusVa(node.personId);
    }
  }

  protected onMouseDown(event: MouseEvent): void {
    if ((event.target as HTMLElement).closest('.nexus__node')) return;
    this.isDragging  = true;
    this.hasDragged  = false;
    this.dragStartX  = event.clientX;
    this.dragStartY  = event.clientY;
    this.dragStartTx = this.translateX();
    this.dragStartTy = this.translateY();
  }

  @HostListener('mousemove', ['$event'])
  onMouseMove(event: MouseEvent): void {
    if (!this.isDragging) return;
    const dx = event.clientX - this.dragStartX;
    const dy = event.clientY - this.dragStartY;
    if (Math.abs(dx) > 3 || Math.abs(dy) > 3) this.hasDragged = true;
    this.translateX.set(this.dragStartTx + dx);
    this.translateY.set(this.dragStartTy + dy);
  }

  @HostListener('mouseup')
  onMouseUp(): void {
    this.isDragging = false;
  }

  @HostListener('wheel', ['$event'])
  onWheel(event: WheelEvent): void {
    event.preventDefault();
    const factor   = event.deltaY < 0 ? 1.1 : 0.9;
    const newScale = Math.min(3, Math.max(0.05, this.scale() * factor));
    const rect     = (event.currentTarget as HTMLElement).getBoundingClientRect();
    const cx = event.clientX - rect.left;
    const cy = event.clientY - rect.top;
    this.translateX.set(cx - (cx - this.translateX()) * (newScale / this.scale()));
    this.translateY.set(cy - (cy - this.translateY()) * (newScale / this.scale()));
    this.scale.set(newScale);
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.svc.isOpen()) this.svc.close();
  }

  protected close(): void {
    this.svc.close();
  }
}
