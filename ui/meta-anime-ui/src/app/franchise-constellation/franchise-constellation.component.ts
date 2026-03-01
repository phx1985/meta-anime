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
const NODE_W = 144;
const NODE_H = 200;
const CENTER_W = 160;
const CENTER_H = 224;
const H_GAP = 88;
const V_GAP = 28;
const PADDING = 60;
const CANVAS_W = 4000;
const CANVAS_H = 3000;
const DRAG_THRESHOLD = 5;

/** Relation types that represent direct canonical story progression. */
const CANONICAL_TYPES = new Set(['prequel', 'sequel', 'parent_story']);

/** Maps a MAL relation_type to a depth delta (column offset from root). */
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

/** True when the relation_type represents a canonical story link. */
function isCanonicalRelation(relationType: string | undefined): boolean {
  return CANONICAL_TYPES.has(relationType ?? '');
}

export interface DisplayEdge {
  d: string;
  isCanonical: boolean;
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
  /** Map<malId, {x, y}> — updated by layout algorithm and user drags. */
  protected nodePositions = signal<Map<string, { x: number; y: number }>>(new Map());

  /** IDs of nodes the user has manually repositioned — layout won't touch these. */
  private manuallyDraggedIds = new Set<string>();

  /** All nodes merged with their current display positions. */
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

  /** Edges with path string + canonical flag. Recomputed on position or graph changes. */
  protected edges = computed<DisplayEdge[]>(() => {
    const g = this.svc.graph();
    const pos = this.nodePositions();
    if (!g) return [];
    return g.edges
      .map((e) => ({
        d: this._edgePath(e, g, pos),
        isCanonical: isCanonicalRelation(e.relationType),
      }))
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
    // Re-layout on every BFS graph update (live-building).
    // Only positions for nodes that don't yet have a position are computed,
    // preventing existing auto-positioned (and manually-dragged) nodes from jumping.
    effect(() => {
      const g = this.svc.graph();
      if (!g) return;
      const newPositions = this._computePositions(g);
      this.nodePositions.update((current) => {
        const merged = new Map(current);
        for (const [malId, pos] of newPositions) {
          // Only place nodes that have no existing position (new arrivals)
          // and have not been manually repositioned by the user.
          if (!merged.has(malId) && !this.manuallyDraggedIds.has(malId)) {
            merged.set(malId, pos);
          }
        }
        return merged;
      });
    });

    // Reset local canvas state whenever a new franchise is opened (graph cleared).
    effect(() => {
      const g = this.svc.graph();
      if (!g) {
        this.nodePositions.set(new Map());
        this.manuallyDraggedIds.clear();
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

  // ── Pointer events ────────────────────────────────────────────────────────

  onPointerDown(event: PointerEvent): void {
    const nodeEl = (event.target as HTMLElement).closest<HTMLElement>('[data-mal-id]');
    this.pointerStartX = event.clientX;
    this.pointerStartY = event.clientY;
    this.nodeDragActive = false;

    if (nodeEl) {
      const malId = nodeEl.dataset['malId']!;
      this.dragNodeId = malId;
      const pos = this.nodePositions().get(malId) ?? { x: 0, y: 0 };
      const canvasX = (event.clientX - this.translateX()) / this.scale();
      const canvasY = (event.clientY - this.translateY()) / this.scale();
      this.nodeDragOffsetX = pos.x - canvasX;
      this.nodeDragOffsetY = pos.y - canvasY;
    } else {
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
    if (this.dragNodeId && this.nodeDragActive) {
      // Mark this node as manually repositioned
      this.manuallyDraggedIds.add(this.dragNodeId);
    } else if (this.dragNodeId && !this.nodeDragActive) {
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

  protected get rootTitle(): string | undefined {
    return this.svc.graph()?.nodes.find((n) => n.isRoot)?.title;
  }

  // ── Layout algorithm ──────────────────────────────────────────────────────
  /**
   * Computes balanced node positions.
   * The canonical trunk (root + prequels/sequels) is vertically centered in the canvas.
   * Indirect nodes (spin-offs, side stories, etc.) fan above/below the trunk.
   */
  private _computePositions(graph: FranchiseGraph): Map<string, { x: number; y: number }> {
    const positions = new Map<string, { x: number; y: number }>();
    const depthMap = new Map<string, number>();

    // BFS from root to assign timeline depths
    depthMap.set(graph.rootMalId, 0);
    const bfsQ = [graph.rootMalId];
    const processed = new Set<string>([graph.rootMalId]);

    while (bfsQ.length > 0) {
      const cur = bfsQ.shift()!;
      const curDepth = depthMap.get(cur) ?? 0;

      for (const edge of graph.edges) {
        let neighborId: string;
        let delta: number;
        if (edge.fromMalId === cur) {
          neighborId = edge.toMalId;
          delta = relationDepthDelta(edge.relationType);
        } else if (edge.toMalId === cur) {
          neighborId = edge.fromMalId;
          delta = -relationDepthDelta(edge.relationType);
        } else {
          continue;
        }
        if (!processed.has(neighborId)) {
          processed.add(neighborId);
          depthMap.set(neighborId, curDepth + delta);
          bfsQ.push(neighborId);
        }
      }
    }

    // Fall back: unreachable nodes stay at depth 0
    for (const node of graph.nodes) {
      if (!depthMap.has(node.malId)) depthMap.set(node.malId, 0);
    }

    // Group by depth column
    const depthGroups = new Map<number, string[]>();
    for (const [malId, depth] of depthMap) {
      if (!depthGroups.has(depth)) depthGroups.set(depth, []);
      depthGroups.get(depth)!.push(malId);
    }

    // Normalize so min depth = 0
    const depths = Array.from(depthGroups.keys()).sort((a, b) => a - b);
    const minDepth = depths[0] ?? 0;
    const canvasCenterY = CANVAS_H / 2;

    depths.forEach((depth) => {
      const col = depth - minDepth;
      const colX = PADDING + col * (NODE_W + H_GAP);
      // Sort nodes in each column by malId for stable, deterministic ordering
      const colNodes = depthGroups.get(depth)!.sort();

      // Separate canonical trunk nodes from indirect/side-story nodes for this column.
      // A node is considered "trunk" if ALL edges connecting it to this column are canonical.
      // The root node is always trunk.
      const trunkIds: string[] = [];
      const sideIds: string[] = [];

      for (const malId of colNodes) {
        const node = graph.nodes.find((n) => n.malId === malId);
        if (!node) continue;
        if (node.isRoot) {
          trunkIds.push(malId);
          continue;
        }
        // Check if this node is connected to an adjacent column via a canonical edge
        const hasCanonicalEdge = graph.edges.some(
          (e) =>
            (e.fromMalId === malId || e.toMalId === malId) &&
            isCanonicalRelation(e.relationType),
        );
        if (hasCanonicalEdge) {
          trunkIds.push(malId);
        } else {
          sideIds.push(malId);
        }
      }

      // Layout trunk: centered around canvasCenterY
      const trunkH = trunkIds.length * NODE_H + Math.max(0, trunkIds.length - 1) * V_GAP;
      const trunkStartY = canvasCenterY - trunkH / 2;
      trunkIds.forEach((malId, i) => {
        const isRoot = graph.nodes.find((n) => n.malId === malId)?.isRoot ?? false;
        const nodeH = isRoot ? CENTER_H : NODE_H;
        positions.set(malId, {
          x: colX,
          y: trunkStartY + i * (nodeH + V_GAP),
        });
      });

      // Layout side nodes: alternate above/below the trunk
      const trunkTopY = trunkStartY;
      const trunkBottomY = trunkStartY + trunkH;
      sideIds.forEach((malId, i) => {
        const above = i % 2 === 0;
        const slotIdx = Math.floor(i / 2);
        const y = above
          ? trunkTopY - (slotIdx + 1) * (NODE_H + V_GAP)
          : trunkBottomY + slotIdx * (NODE_H + V_GAP);
        positions.set(malId, { x: colX, y });
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

    const x1 = fromPos.x + fromW / 2;
    const y1 = fromPos.y + fromH / 2;
    const x2 = toPos.x + toW / 2;
    const y2 = toPos.y + toH / 2;

    const cx = (x1 + x2) / 2;
    return `M ${x1} ${y1} C ${cx} ${y1} ${cx} ${y2} ${x2} ${y2}`;
  }

  private _touchDist(event: TouchEvent): number {
    const [t1, t2] = [event.touches[0], event.touches[1]];
    return Math.hypot(t2.clientX - t1.clientX, t2.clientY - t1.clientY);
  }
}
