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

// ── Layout constants (Final Pivot: uniform cards, 1D horizontal timeline) ────
const NODE_W = 144;
const NODE_H = 200;
/** Generous horizontal gap so directional arrows are clearly visible. */
const H_GAP = 200;
const PADDING = 120;
const CANVAS_W = 8000;
const CANVAS_H = 6000;
const TRUNK_CENTER_Y = CANVAS_H / 2;
const FIT_MARGIN = 60;
const HEADER_APPROX_H = 90;
const DRAG_THRESHOLD = 5;
const MIN_SCALE = 0.05;
const MAX_SCALE = 3;

const TRUNK_TYPES = new Set(['prequel', 'sequel']);

function isTrunkRelation(relationType: string | undefined): boolean {
  return TRUNK_TYPES.has(relationType ?? '');
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

  // ── Node positions ────────────────────────────────────────────────────────
  /** Map<malId, {x, y}> — top-left corner of each card in canvas space. */
  protected nodePositions = signal<Map<string, { x: number; y: number }>>(new Map());

  /** IDs the user has manually repositioned — excluded from auto-layout. */
  private manuallyDraggedIds = new Set<string>();

  /** All graph nodes merged with their current canvas positions. */
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

  /** Timeline edges — all are canonical (prequel/sequel solid arrows). */
  protected edges = computed<DisplayEdge[]>(() => {
    const g = this.svc.graph();
    const pos = this.nodePositions();
    if (!g) return [];
    return g.edges
      .map((e) => ({
        d: this._edgePath(e, g, pos),
        isCanonical: true,
      }))
      .filter((e) => e.d !== '');
  });

  // ── Pointer state ─────────────────────────────────────────────────────────
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
    // ── Live-build layout effect ────────────────────────────────────────────
    // Triggered on every BFS graph update. New nodes are placed; existing nodes
    // (including manually-dragged ones) are NOT moved. Camera auto-fits after
    // every placement so no card is ever off-screen.
    effect(() => {
      const g = this.svc.graph();
      if (!g) return;

      const newPositions = this._computeTimelinePositions(g);

      // Replace all auto-layout positions so that column shifts caused by newly-
      // discovered prequels are reflected immediately (prevents coordinate sharing).
      // Manually-dragged nodes are the only exception — their positions are preserved.
      let merged: Map<string, { x: number; y: number }> | undefined;
      this.nodePositions.update((current) => {
        const m = new Map<string, { x: number; y: number }>();
        // Preserve manually-dragged positions as-is
        for (const malId of this.manuallyDraggedIds) {
          const p = current.get(malId);
          if (p) m.set(malId, p);
        }
        // Apply fresh computed positions for every auto-layout node
        for (const [malId, pos] of newPositions) {
          if (!this.manuallyDraggedIds.has(malId)) {
            m.set(malId, pos);
          }
        }
        merged = m;
        return m;
      });

      // V6: auto-fit camera after every node arrival (pass data to avoid
      // reading the just-written signal and creating a re-entrant effect).
      if (merged) this._fitCameraToNodes(merged, g);
    });

    // ── Reset effect ────────────────────────────────────────────────────────
    // When a new franchise is opened (graph resets to null), clear all local state.
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

  // ── Pointer events ─────────────────────────────────────────────────────────
  // V6: ALL interactions are disabled while the tree is being built.

  onPointerDown(event: PointerEvent): void {
    // V6: Observe-then-play lock — no interaction while loading.
    if (this.svc.loading()) return;

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
    if (this.svc.loading()) return;

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
      this.manuallyDraggedIds.add(this.dragNodeId);
    } else if (this.dragNodeId && !this.nodeDragActive) {
      this.openMalPage(this.dragNodeId);
    }
    this.dragNodeId = null;
    this.nodeDragActive = false;
    this.isPanning = false;
  }

  onWheel(event: WheelEvent): void {
    // V6: Observe-then-play lock.
    if (this.svc.loading()) return;
    event.preventDefault();
    const factor = event.deltaY < 0 ? 1.1 : 0.9;
    this.scale.set(Math.min(MAX_SCALE, Math.max(MIN_SCALE, this.scale() * factor)));
  }

  onTouchStart(event: TouchEvent): void {
    if (this.svc.loading()) return;
    if (event.touches.length === 2) {
      this.lastPinchDist = this._touchDist(event);
    }
  }

  onTouchMove(event: TouchEvent): void {
    if (this.svc.loading()) return;
    if (event.touches.length === 2) {
      event.preventDefault();
      const dist = this._touchDist(event);
      if (this.lastPinchDist > 0) {
        const factor = dist / this.lastPinchDist;
        this.scale.set(Math.min(MAX_SCALE, Math.max(MIN_SCALE, this.scale() * factor)));
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

  // ── Final Pivot: 1D Horizontal Timeline layout ────────────────────────────

  /**
   * Runs BFS following only prequel/sequel edges from root.
   * Returns Map<malId, rawColumn> where column 0 = root's column,
   * prequel neighbours get column − 1, sequel neighbours get column + 1.
   * Used to determine left-to-right relationship order (prequels left, sequels right).
   */
  private _computeTrunkCols(graph: FranchiseGraph): Map<string, number> {
    const trunkCols = new Map<string, number>();
    trunkCols.set(graph.rootMalId, 0);
    const bfsQ = [graph.rootMalId];
    const visited = new Set<string>([graph.rootMalId]);

    while (bfsQ.length > 0) {
      const cur = bfsQ.shift()!;
      const curCol = trunkCols.get(cur) ?? 0;

      for (const edge of graph.edges) {
        if (!isTrunkRelation(edge.relationType)) continue;

        let neighborId: string;
        let delta: number;

        if (edge.fromMalId === cur) {
          neighborId = edge.toMalId;
          delta = edge.relationType === 'sequel' ? 1 : -1;
        } else if (edge.toMalId === cur) {
          neighborId = edge.fromMalId;
          delta = edge.relationType === 'sequel' ? -1 : 1;
        } else {
          continue;
        }

        if (!visited.has(neighborId)) {
          visited.add(neighborId);
          trunkCols.set(neighborId, curCol + delta);
          bfsQ.push(neighborId);
        }
      }
    }
    return trunkCols;
  }

  /**
   * Places all nodes in a single straight horizontal line ordered by
   * relationship: prequels to the left, sequels to the right of the root.
   * Since the service only fetches prequel/sequel relations, every node
   * in the graph is part of this single timeline — no branches exist.
   */
  private _computeTimelinePositions(graph: FranchiseGraph): Map<string, { x: number; y: number }> {
    const positions = new Map<string, { x: number; y: number }>();
    const trunkCols = this._computeTrunkCols(graph);
    const colValues = Array.from(trunkCols.values());
    const minCol = Math.min(...colValues);

    for (const [malId, rawCol] of trunkCols) {
      const col = rawCol - minCol;
      const x = PADDING + col * (NODE_W + H_GAP);
      const y = TRUNK_CENTER_Y - NODE_H / 2;
      positions.set(malId, { x, y });
    }

    return positions;
  }

  // ── V6 Camera auto-fit ─────────────────────────────────────────────────────

  /**
   * Fits the camera so that every placed node is visible in the viewport.
   * Called after every BFS graph update so no card is ever off-screen.
   *
   * @param pos     - the merged positions map (passed directly to avoid
   *                  re-reading the just-written signal inside the effect)
   * @param graph   - the current FranchiseGraph
   */
  private _fitCameraToNodes(
    pos: Map<string, { x: number; y: number }>,
    _graph: FranchiseGraph,
  ): void {
    if (pos.size === 0) return;

    let minX = Infinity,
      minY = Infinity,
      maxX = -Infinity,
      maxY = -Infinity;

    for (const [, p] of pos) {
      if (p.x < minX) minX = p.x;
      if (p.y < minY) minY = p.y;
      if (p.x + NODE_W > maxX) maxX = p.x + NODE_W;
      if (p.y + NODE_H > maxY) maxY = p.y + NODE_H;
    }

    // Add breathing room around the bounding box
    minX -= FIT_MARGIN;
    minY -= FIT_MARGIN;
    maxX += FIT_MARGIN;
    maxY += FIT_MARGIN;

    const bboxW = maxX - minX;
    const bboxH = maxY - minY;
    if (bboxW <= 0 || bboxH <= 0) return;

    const vpW = window.innerWidth;
    const vpH = Math.max(1, window.innerHeight - HEADER_APPROX_H);

    // Scale to fit the entire bounding box, capped so a single node doesn't blow up
    const newScale = Math.min(vpW / bboxW, vpH / bboxH, 1.2);
    const clampedScale = Math.max(MIN_SCALE, newScale);

    // Translate so the scaled bbox is centred in the viewport
    const scaledW = bboxW * clampedScale;
    const scaledH = bboxH * clampedScale;
    const tx = (vpW - scaledW) / 2 - minX * clampedScale;
    const ty = HEADER_APPROX_H + (vpH - scaledH) / 2 - minY * clampedScale;

    this.scale.set(clampedScale);
    this.translateX.set(tx);
    this.translateY.set(ty);
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

    // All nodes share the same y row.  Draw a left-to-right horizontal line
    // connecting the right edge of the left card to the left edge of the right card
    // so arrows never overlap a card and always point in the reading direction.
    const leftPos = fromPos.x <= toPos.x ? fromPos : toPos;
    const rightPos = fromPos.x <= toPos.x ? toPos : fromPos;
    const lx = leftPos.x + NODE_W;   // right edge of left card
    const rx = rightPos.x;           // left edge of right card
    const y = leftPos.y + NODE_H / 2;
    return `M ${lx} ${y} L ${rx} ${y}`;
  }

  private _touchDist(event: TouchEvent): number {
    const [t1, t2] = [event.touches[0], event.touches[1]];
    return Math.hypot(t2.clientX - t1.clientX, t2.clientY - t1.clientY);
  }
}
