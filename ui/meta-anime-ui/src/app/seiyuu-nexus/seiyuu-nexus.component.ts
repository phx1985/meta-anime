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
const ORIGIN = CANVAS_SIZE / 2;
const MIN_RING_CHAR  = 260;
const MIN_RING_VA    = 500;
const MIN_RING_OTHER = 780;
const NODE_ARC_SPAN  = 22; // px of arc per node — drives auto-scaling

type LangFilter = 'Japanese' | 'English' | 'all';

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
  protected readonly scale = signal(1);
  protected readonly focusedNodeId = signal<string | null>(null);
  protected readonly langFilter = signal<LangFilter>('Japanese');

  private isDragging = false;
  private dragStartX = 0;
  private dragStartY = 0;
  private dragStartTx = 0;
  private dragStartTy = 0;
  private hasDragged = false;

  /** VA IDs that are visible under the current language filter. */
  private readonly visibleVaIds = computed<Set<number>>(() => {
    const nexus = this.svc.nexus();
    if (!nexus) return new Set();
    const lang = this.langFilter();
    if (lang === 'all') return new Set(nexus.nodes.filter(n => n.type === 'va').map(n => n.personId!).filter(Boolean));
    // Filter cast edges: keep only VA nodes that voice a character in the chosen language.
    // The `voices` edges from a VA node reference a character node; language context is on the
    // edge itself. We derive it from the raw nexus node list since language metadata lives on the
    // AnimeCast record. As a practical approximation: a VA node ID is "va:{personId}" and Jikan
    // returns separate VA entries per language, so we keep VA nodes whose label language matches
    // by checking character-cast membership. The backend `GetSeiyuuNexusQueryHandler` inserts
    // cast entries per language (one VA per character per language), so each language already has
    // distinct VA nodes with distinct person IDs from different casts. However, the current
    // handler doesn't filter by language when building the nexus. Until server-side filtering is
    // added, we simply show all VAs (language filter remains a UX affordance for future work).
    return new Set(nexus.nodes.filter(n => n.type === 'va').map(n => n.personId!).filter(Boolean));
  });

  protected readonly positionedNodes = computed<PositionedNode[]>(() => {
    const nexus = this.svc.nexus();
    if (!nexus) return [];

    const nodes = nexus.nodes;
    const root = nodes.find(n => n.isRoot);
    const chars = nodes.filter(n => n.type === 'character');
    const vas   = nodes.filter(n => n.type === 'va');
    const others = nodes.filter(n => n.type === 'anime' && !n.isRoot);

    // Auto-scale ring radii so nodes don't overlap
    const ringChar  = Math.max(MIN_RING_CHAR,  (chars.length  * NODE_ARC_SPAN) / (2 * Math.PI));
    const ringVa    = Math.max(MIN_RING_VA,    (vas.length    * NODE_ARC_SPAN) / (2 * Math.PI));
    const ringOther = Math.max(MIN_RING_OTHER, (others.length * NODE_ARC_SPAN) / (2 * Math.PI));

    const placed: PositionedNode[] = [];
    if (root) placed.push({ ...root, x: ORIGIN, y: ORIGIN });

    chars.forEach((n, i) => {
      const angle = (2 * Math.PI * i) / Math.max(chars.length, 1) - Math.PI / 2;
      placed.push({ ...n, x: ORIGIN + ringChar * Math.cos(angle), y: ORIGIN + ringChar * Math.sin(angle) });
    });

    vas.forEach((n, i) => {
      const angle = (2 * Math.PI * i) / Math.max(vas.length, 1) - Math.PI / 2;
      placed.push({ ...n, x: ORIGIN + ringVa * Math.cos(angle), y: ORIGIN + ringVa * Math.sin(angle) });
    });

    others.forEach((n, i) => {
      const angle = (2 * Math.PI * i) / Math.max(others.length, 1) - Math.PI / 2;
      placed.push({ ...n, x: ORIGIN + ringOther * Math.cos(angle), y: ORIGIN + ringOther * Math.sin(angle) });
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

    return nexus.edges
      .map(edge => {
        const from = nodeMap.get(edge.fromId);
        const to   = nodeMap.get(edge.toId);
        if (!from || !to) return null;
        const path = this.quadraticPath(from.x, from.y, to.x, to.y);
        return { edge, path, dashed: edge.relation === 'appearsIn' };
      })
      .filter((e): e is NonNullable<typeof e> => e !== null);
  });

  protected readonly lastUpdatedLocal = computed(() => {
    const raw = this.svc.nexus()?.lastUpdated;
    if (!raw) return null;
    const d = new Date(raw);
    const pad = (n: number) => String(n).padStart(2, '0');
    return `${pad(d.getDate())}/${pad(d.getMonth() + 1)}/${d.getFullYear()} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
  });

  protected readonly nodeCount = computed(() => {
    const nexus = this.svc.nexus();
    if (!nexus) return { chars: 0, vas: 0 };
    return {
      chars: nexus.nodes.filter(n => n.type === 'character').length,
      vas:   nexus.nodes.filter(n => n.type === 'va').length,
    };
  });

  readonly canvasSize = CANVAS_SIZE;
  readonly langOptions: LangFilter[] = ['Japanese', 'English', 'all'];

  constructor() {
    // Re-center whenever nexus data arrives so the graph is always in view
    effect(() => {
      const nexus = this.svc.nexus();
      if (nexus && nexus.nodes.length > 0) {
        this.centerView();
      }
    });
  }

  ngOnInit(): void {
    this.centerView();
  }

  private centerView(): void {
    const vw = window.innerWidth;
    const vh = window.innerHeight;
    const s = Math.min(vw / 1200, vh / 900, 0.8);
    this.scale.set(s);
    this.translateX.set((vw - CANVAS_SIZE * s) / 2);
    this.translateY.set((vh - CANVAS_SIZE * s) / 2);
  }

  private quadraticPath(x1: number, y1: number, x2: number, y2: number): string {
    const mx = (x1 + x2) / 2;
    const my = (y1 + y2) / 2;
    // Slight perpendicular offset for a gentle curve
    const dx = x2 - x1;
    const dy = y2 - y1;
    const len = Math.sqrt(dx * dx + dy * dy) || 1;
    const bend = Math.min(len * 0.18, 60);
    const cx = mx - (dy / len) * bend;
    const cy = my + (dx / len) * bend;
    return `M ${x1} ${y1} Q ${cx} ${cy} ${x2} ${y2}`;
  }

  protected get canvasTransform(): string {
    return `translate(${this.translateX()}px, ${this.translateY()}px) scale(${this.scale()})`;
  }

  protected setLangFilter(lang: LangFilter): void {
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
    this.isDragging = true;
    this.hasDragged = false;
    this.dragStartX = event.clientX;
    this.dragStartY = event.clientY;
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
    const factor = event.deltaY < 0 ? 1.1 : 0.9;
    const newScale = Math.min(3, Math.max(0.05, this.scale() * factor));
    const rect = (event.currentTarget as HTMLElement).getBoundingClientRect();
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
