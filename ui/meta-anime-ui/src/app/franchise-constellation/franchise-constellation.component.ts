import {
  ChangeDetectionStrategy,
  Component,
  HostListener,
  OnDestroy,
  OnInit,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { FranchiseConstellationService } from '../services/franchise-constellation.service';
import { PopupService } from '../services/popup.service';
import { FranchiseGraph, FranchiseGraphEdge, FranchiseGraphNode } from '../models/anime.models';

// ── Layout constants ─────────────────────────────────────────────────────────
/** Poster card dimensions for regular nodes */
const NODE_W = 144;
const NODE_H = 200;
/** Slightly larger card for the root (clicked) anime */
const CENTER_W = 160;
const CENTER_H = 224;
/** Horizontal gap between timeline columns */
const H_GAP = 80;
/** Vertical gap between nodes stacked in the same column */
const V_GAP = 24;
/** Canvas padding */
const PADDING = 60;
/** Fixed canvas size — large enough that dragged nodes never disappear */
const CANVAS_W = 4000;
const CANVAS_H = 3000;

/** How many pixels of pointer movement before we commit to a drag (vs click). */
const DRAG_THRESHOLD = 5;

/** Maps a MAL relation_type to a depth delta for timeline positioning.
 *  Positive = goes RIGHT (later in story), negative = goes LEFT (earlier). */
function relationDepthDelta(relationType: string | undefined): number {
  switch (relationType) {
    case 'prequel':
    case 'parent_story':
      return -1;
    case 'sequel':
    case 'spin_off':
      return 1;
    default:
      return 0;
  }
}

@Component({
  selector: 'app-franchise-constellation',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, TranslatePipe],
  templateUrl: './franchise-constellation.component.html',
  styleUrl: './franchise-constellation.component.scss',
})
export class FranchiseConstellationComponent implements OnInit, OnDestroy {
  protected readonly svc = inject(FranchiseConstellationService);
  private readonly popupService = inject(PopupService);
  protected readonly translate = inject(TranslateService);

  protected readonly CANVAS_W = CANVAS_W;
  protected readonly CANVAS_H = CANVAS_H;

  // ── Pan / zoom state ──────────────────────────────────────────────────────
  protected translateX = signal(0);
  protected translateY = signal(0);
  protected scale = signal(1);

  protected graphTransform = computed(
    () => `translate(${this.translateX()}px, ${this.translateY()}px) scale(${this.scale()})`,
  );

  // ── Per-node draggable positions ──────────────────────────────────────────
  /** Map<malId, {x, y}> — set once from layout algorithm, then updated by dragging. */
  protected nodePositions = signal<Map<string, { x: number; y: number }>>(new Map());

  /** All nodes with their current display positions merged in. */
  protected displayNodes = computed<Array<FranchiseGraphNode & { x: number; y: number }>>(() => {
    const g = this.svc.graph();
    const pos = this.nodePositions();
    if (!g) return [];
    return g.nodes.map((n) => ({
      ...n,
      x: pos.get(n.malId)?.x ?? 0,
      y: pos.get(n.malId)?.y ?? 0,
    }));
  });

  /** Edges as SVG path strings. Recomputed whenever node positions change (dragging). */
  protected edges = computed<Array<{ d: string; relationType?: string }>>(() => {
    const g = this.svc.graph();
    const pos = this.nodePositions();
    if (!g) return [];
    return g.edges
      .map((e) => ({ d: this._edgePath(e, g, pos), relationType: e.relationType }))
      .filter((e) => e.d !== '');
  });

  // ── Pointer interaction state ─────────────────────────────────────────────
  private isPanning = false;
  private panStartX = 0;
  private panStartY = 0;
  private panStartTx = 0;
  private panStartTy = 0;
  private lastPinchDist = 0;

  private dragNodeId: string | null = null;
  private nodeDragOffsetX = 0;
  private nodeDragOffsetY = 0;
  private pointerStartX = 0;
  private pointerStartY = 0;
  private nodeDragActive = false;

  constructor() {
    // Whenever loading finishes and a graph is available, compute initial positions.
    effect(() => {
      const g = this.svc.graph();
      const loading = this.svc.loading();
      if (g && !loading) {
        this.nodePositions.set(this._computeInitialPositions(g));
        // Center the view on the root node
        this.translateX.set(0);
        this.translateY.set(0);
        this.scale.set(1);
      }
    });
  }

  ngOnInit(): void {
    this.popupService.franchiseConstellationOpen.set(true);
  }

  ngOnDestroy(): void {
    this.popupService.franchiseConstellationOpen.set(false);
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    this.svc.close();
  }

  close(): void {
    this.svc.close();
  }

  // ── Pointer events (wrap handles pan + node drag) ─────────────────────────

  onPointerDown(event: PointerEvent): void {
    const nodeEl = (event.target as HTMLElement).closest<HTMLElement>('[data-mal-id]');
    this.pointerStartX = event.clientX;
    this.pointerStartY = event.clientY;
    this.nodeDragActive = false;

    if (nodeEl) {
      // Potential node drag
      const malId = nodeEl.dataset['malId']!;
      this.dragNodeId = malId;
      const pos = this.nodePositions().get(malId) ?? { x: 0, y: 0 };
      const canvasX = (event.clientX - this.translateX()) / this.scale();
      const canvasY = (event.clientY - this.translateY()) / this.scale();
      this.nodeDragOffsetX = pos.x - canvasX;
      this.nodeDragOffsetY = pos.y - canvasY;
    } else {
      // Pan the canvas
      this.isPanning = true;
      this.panStartX = event.clientX;
      this.panStartY = event.clientY;
      this.panStartTx = this.translateX();
      this.panStartTy = this.translateY();
    }
    (event.currentTarget as HTMLElement).setPointerCapture(event.pointerId);
  }

  onPointerMove(event: PointerEvent): void {
    if (this.dragNodeId) {
      const dist = Math.hypot(event.clientX - this.pointerStartX, event.clientY - this.pointerStartY);
      if (dist >= DRAG_THRESHOLD) this.nodeDragActive = true;
      if (this.nodeDragActive) {
        const canvasX = (event.clientX - this.translateX()) / this.scale();
        const canvasY = (event.clientY - this.translateY()) / this.scale();
        const newPos = new Map(this.nodePositions());
        newPos.set(this.dragNodeId, {
          x: canvasX + this.nodeDragOffsetX,
          y: canvasY + this.nodeDragOffsetY,
        });
        this.nodePositions.set(newPos);
      }
      return;
    }
    if (this.isPanning) {
      this.translateX.set(this.panStartTx + event.clientX - this.panStartX);
      this.translateY.set(this.panStartTy + event.clientY - this.panStartY);
    }
  }

  onPointerUp(): void {
    // Treat as a click on a node only if there was no significant drag
    if (this.dragNodeId && !this.nodeDragActive) {
      this.openMalPage(this.dragNodeId);
    }
    this.dragNodeId = null;
    this.nodeDragActive = false;
    this.isPanning = false;
  }

  onWheel(event: WheelEvent): void {
    event.preventDefault();
    const factor = event.deltaY < 0 ? 1.1 : 0.9;
    this.scale.set(Math.min(3, Math.max(0.2, this.scale() * factor)));
  }

  onTouchStart(event: TouchEvent): void {
    if (event.touches.length === 2) {
      this.lastPinchDist = this._touchDist(event);
    }
  }

  onTouchMove(event: TouchEvent): void {
    if (event.touches.length === 2) {
      event.preventDefault();
      const dist = this._touchDist(event);
      if (this.lastPinchDist > 0) {
        const factor = dist / this.lastPinchDist;
        this.scale.set(Math.min(3, Math.max(0.2, this.scale() * factor)));
      }
      this.lastPinchDist = dist;
    }
  }

  openMalPage(malId: string): void {
    window.open(`https://myanimelist.net/anime/${malId}`, '_blank', 'noopener,noreferrer');
  }

  /** Title of the root node (for the header). */
  protected get rootTitle(): string | undefined {
    return this.svc.graph()?.nodes.find((n) => n.isRoot)?.title;
  }

  /** Returns the i18n key for a given raw MAL relation_type. */
  relationI18nKey(typeKey: string | undefined): string {
    return `franchise.relationType.${typeKey ?? 'other'}`;
  }

  // ── Timeline layout algorithm ─────────────────────────────────────────────

  private _computeInitialPositions(graph: FranchiseGraph): Map<string, { x: number; y: number }> {
    const positions = new Map<string, { x: number; y: number }>();
    const depthMap = new Map<string, number>(); // malId → timeline column index

    // BFS from root to assign depths along the timeline
    depthMap.set(graph.rootMalId, 0);
    const bfsQueue = [graph.rootMalId];
    const processed = new Set<string>([graph.rootMalId]);

    while (bfsQueue.length > 0) {
      const currentId = bfsQueue.shift()!;
      const currentDepth = depthMap.get(currentId) ?? 0;

      for (const edge of graph.edges) {
        let neighborId: string;
        let delta: number;

        if (edge.fromMalId === currentId) {
          neighborId = edge.toMalId;
          delta = relationDepthDelta(edge.relationType);
        } else if (edge.toMalId === currentId) {
          neighborId = edge.fromMalId;
          // Invert: if current is the "to" end of a prequel edge, current is newer → from is older
          delta = -relationDepthDelta(edge.relationType);
        } else {
          continue;
        }

        if (!processed.has(neighborId)) {
          processed.add(neighborId);
          depthMap.set(neighborId, currentDepth + delta);
          bfsQueue.push(neighborId);
        }
      }
    }

    // Fall back: any node not reachable via edges stays at depth 0
    for (const node of graph.nodes) {
      if (!depthMap.has(node.malId)) {
        depthMap.set(node.malId, 0);
      }
    }

    // Group nodes by depth
    const depthGroups = new Map<number, string[]>();
    for (const [malId, depth] of depthMap) {
      if (!depthGroups.has(depth)) depthGroups.set(depth, []);
      depthGroups.get(depth)!.push(malId);
    }

    // Translate depths to pixel columns (normalize so minimum depth = 0)
    const depths = Array.from(depthGroups.keys()).sort((a, b) => a - b);
    const minDepth = depths[0] ?? 0;

    depths.forEach((depth) => {
      const col = depth - minDepth;
      const colNodes = depthGroups.get(depth)!;
      const colX = PADDING + col * (NODE_W + H_GAP);

      colNodes.forEach((malId, rowIdx) => {
        const isRoot = graph.nodes.find((n) => n.malId === malId)?.isRoot ?? false;
        const nodeH = isRoot ? CENTER_H : NODE_H;
        positions.set(malId, {
          x: colX,
          y: PADDING + rowIdx * (nodeH + V_GAP),
        });
      });
    });

    return positions;
  }

  // ── Edge drawing ──────────────────────────────────────────────────────────

  private _edgePath(
    edge: FranchiseGraphEdge,
    graph: FranchiseGraph,
    pos: Map<string, { x: number; y: number }>,
  ): string {
    const fromNode = graph.nodes.find((n) => n.malId === edge.fromMalId);
    const toNode = graph.nodes.find((n) => n.malId === edge.toMalId);
    if (!fromNode || !toNode) return '';

    const fromPos = pos.get(edge.fromMalId);
    const toPos = pos.get(edge.toMalId);
    if (!fromPos || !toPos) return '';

    const fromW = fromNode.isRoot ? CENTER_W : NODE_W;
    const fromH = fromNode.isRoot ? CENTER_H : NODE_H;
    const toW = toNode.isRoot ? CENTER_W : NODE_W;
    const toH = toNode.isRoot ? CENTER_H : NODE_H;

    // Connect center-to-center of the two nodes
    const x1 = fromPos.x + fromW / 2;
    const y1 = fromPos.y + fromH / 2;
    const x2 = toPos.x + toW / 2;
    const y2 = toPos.y + toH / 2;

    // Bezier with horizontal control points for a smooth S-curve
    const cx = (x1 + x2) / 2;
    return `M ${x1} ${y1} C ${cx} ${y1} ${cx} ${y2} ${x2} ${y2}`;
  }

  private _touchDist(event: TouchEvent): number {
    const [t1, t2] = [event.touches[0], event.touches[1]];
    return Math.hypot(t2.clientX - t1.clientX, t2.clientY - t1.clientY);
  }
}
