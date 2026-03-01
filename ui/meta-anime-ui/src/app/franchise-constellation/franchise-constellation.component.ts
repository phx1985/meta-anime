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

// ── Layout constants ──────────────────────────────────────────────────────────
const NODE_W = 144;
const NODE_H = 200;
/** Horizontal gap between nodes within a row. */
const H_GAP = 80;
/** Vertical gap between rows (bottom of tallest node to top of next row). */
const V_GAP = 100;
/** Gap between stacked parallel nodes within a single column step. */
const PARALLEL_GAP = 12;
/** Horizontal control-point offset for U-turn bezier curves at row ends. */
const UTURN_RADIUS = 80;
const PADDING = 80;
const CANVAS_W = 8000;
const CANVAS_H = 10000;
const FIT_MARGIN = 60;
const HEADER_APPROX_H = 90;
const DRAG_THRESHOLD = 5;
const MIN_SCALE = 0.05;
const MAX_SCALE = 3;
const MIN_NODES_PER_ROW = 2;
/** Milliseconds between each card appearing during the sequential render animation. */
const RENDER_INTERVAL_MS = 150;

const TRUNK_TYPES = new Set(['prequel', 'sequel']);

function isTrunkRelation(relationType: string | undefined): boolean {
  return TRUNK_TYPES.has(relationType ?? '');
}

interface LayoutMeta {
  /** Zero-based step index across all rows (used to determine arrow direction). */
  row: number;
  colStep: number;
  /** Reading direction of this node's row. */
  rowDir: 'ltr' | 'rtl';
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

  /** Snake-layout metadata for each node — drives directional edge routing. */
  private layoutMeta = signal<Map<string, LayoutMeta>>(new Map());

  /** IDs the user has manually repositioned — excluded from auto-layout. */
  private manuallyDraggedIds = new Set<string>();

  // ── Sequential render queue ────────────────────────────────────────────────
  /** Node IDs waiting to become visible; drained one at a time at RENDER_INTERVAL_MS. */
  private _renderQueue: string[] = [];
  private _renderTimer: ReturnType<typeof setTimeout> | null = null;
  /** Subset of graph nodes currently rendered in the DOM. */
  protected visibleNodeIds = signal<Set<string>>(new Set());

  // ── Quick Look modal ──────────────────────────────────────────────────────
  /** Node currently open in the Quick Look modal; null when closed. */
  protected quickLookNode = signal<FranchiseGraphNode | null>(null);
  /** True when the Quick Look synopsis text needs a "Read More" trigger. */
  protected readonly quickLookSynopsisLong = computed(
    () => (this.quickLookNode()?.synopsis?.length ?? 0) > 200,
  );

  private readonly nodeMap = computed<Map<string, FranchiseGraphNode>>(() => {
    const g = this.svc.graph();
    if (!g) return new Map();
    return new Map(g.nodes.map((n) => [n.malId, n]));
  });

  /** All graph nodes merged with their current canvas positions (visible only). */
  protected displayNodes = computed<Array<FranchiseGraphNode & { x: number; y: number }>>(() => {
    const g = this.svc.graph();
    const pos = this.nodePositions();
    const visible = this.visibleNodeIds();
    if (!g) return [];
    return g.nodes
      .filter((n) => visible.has(n.malId))
      .map((n) => ({
        ...n,
        x: pos.get(n.malId)?.x ?? 0,
        y: pos.get(n.malId)?.y ?? 0,
      }));
  });

  /** Timeline edges — only edges where both endpoints are visible. */
  protected edges = computed<DisplayEdge[]>(() => {
    const g = this.svc.graph();
    const pos = this.nodePositions();
    const meta = this.layoutMeta(); // track as dependency for edge redraw on layout change
    const visible = this.visibleNodeIds();
    if (!g) return [];
    return g.edges
      .filter((e) => visible.has(e.fromMalId) && visible.has(e.toMalId))
      .map((e) => ({
        d: this._edgePath(e, g, pos, meta),
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

      const { positions: newPositions, layoutInfo } = this._computeSnakePositions(g);

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

      // Update layout metadata for all nodes (including manual ones keep old meta)
      this.layoutMeta.update((current) => {
        const m = new Map<string, LayoutMeta>(current);
        for (const [malId, info] of layoutInfo) {
          m.set(malId, info);
        }
        return m;
      });

      // V6: auto-fit camera after every node arrival (pass data to avoid
      // reading the just-written signal and creating a re-entrant effect).
      if (merged) this._fitCameraToNodes(merged, g);

      // Queue any newly arrived nodes for sequential visual rendering.
      // Nodes already visible or already queued are skipped (deduplication).
      const alreadyQueued = new Set(this._renderQueue);
      const visible = this.visibleNodeIds();
      for (const node of g.nodes) {
        if (!visible.has(node.malId) && !alreadyQueued.has(node.malId)) {
          this._renderQueue.push(node.malId);
        }
      }
      this._startRenderDrain();
    });

    // ── Reset effect ────────────────────────────────────────────────────────
    // When a new franchise is opened (graph resets to null), clear all local state.
    effect(() => {
      const g = this.svc.graph();
      if (!g) {
        this._stopRenderDrain();
        this.visibleNodeIds.set(new Set());
        this.nodePositions.set(new Map());
        this.layoutMeta.set(new Map());
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
    this._stopRenderDrain();
    this.popupService.franchiseConstellationOpen.set(false);
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.quickLookNode()) {
      this.closeQuickLook();
    } else {
      this.svc.close();
    }
  }

  @HostListener('window:resize')
  onWindowResize(): void {
    const g = this.svc.graph();
    if (!g) return;
    // Recalculate snake layout for the new viewport width.
    // Manual drags are cleared because the column structure may shift entirely.
    this.manuallyDraggedIds.clear();
    const { positions, layoutInfo } = this._computeSnakePositions(g);
    this.nodePositions.set(positions);
    this.layoutMeta.set(layoutInfo);
    this._fitCameraToNodes(positions, g);
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
      this.openQuickLook(this.dragNodeId);
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

  // ── Quick Look modal ──────────────────────────────────────────────────────

  openQuickLook(malId: string): void {
    const node = this.nodeMap().get(malId);
    if (node) this.quickLookNode.set(node);
  }

  closeQuickLook(): void {
    this.quickLookNode.set(null);
  }

  onQuickLookReadMore(): void {
    const node = this.quickLookNode();
    if (!node?.synopsis) return;
    this.popupService.openSynopsis(node.title, node.synopsis);
  }

  openNodeOnMal(): void {
    const node = this.quickLookNode();
    if (!node) return;
    this.openMalPage(node.malId);
  }

  protected get rootTitle(): string | undefined {
    return this.svc.graph()?.nodes.find((n) => n.isRoot)?.title;
  }

  /**
   * Returns a CSS modifier class for the media type of a node.
   * @param mediaType  Raw media type string from the API (e.g. 'tv', 'movie').
   * @param prefix     CSS class prefix (default: 'node-card-inner').
   * Returns an empty string when no media type is available.
   */
  protected mediaTypeClass(mediaType: string | undefined, prefix = 'node-card-inner'): string {
    if (!mediaType) return '';
    return `${prefix}--${mediaType.toLowerCase().replace(/\s+/g, '_')}`;
  }

  /** Formats an ISO date string for display; returns the i18n "N/A" fallback when absent or invalid. */
  protected formatDateOrUnknown(rawDate: string | undefined): string {
    if (!rawDate) return this.translate.instant('franchise.quickLook.unknownDate');
    const d = new Date(rawDate);
    if (isNaN(d.getTime())) return rawDate;
    return d.toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' });
  }

  // ── Render queue (sequential appearance animation) ────────────────────────

  /** Starts draining the render queue if not already running. */
  private _startRenderDrain(): void {
    if (this._renderTimer !== null || this._renderQueue.length === 0) return;
    this._scheduleNextNode();
  }

  /**
   * Immediately makes the next queued node visible, then schedules itself
   * again after RENDER_INTERVAL_MS (150 ms) so nodes appear one at a time.
   */
  private _scheduleNextNode(): void {
    if (this._renderQueue.length === 0) {
      this._renderTimer = null;
      return;
    }
    const malId = this._renderQueue.shift()!;
    this.visibleNodeIds.update((s) => {
      const n = new Set(s);
      n.add(malId);
      return n;
    });
    this._renderTimer = setTimeout(() => this._scheduleNextNode(), RENDER_INTERVAL_MS);
  }

  /** Cancels any pending drain and empties the queue. */
  private _stopRenderDrain(): void {
    if (this._renderTimer !== null) {
      clearTimeout(this._renderTimer);
      this._renderTimer = null;
    }
    this._renderQueue = [];
  }

  // ── Snake (Boustrophedon) Layout ──────────────────────────────────────────

  /**
   * Places all nodes in a snake (boustrophedon) wrapping pattern:
   *   - Row 0 (even): nodes flow LEFT → RIGHT
   *   - Row 1 (odd):  nodes flow RIGHT → LEFT
   *   - Row 2 (even): nodes flow LEFT → RIGHT  … and so on.
   *
   * The number of columns per row is calculated dynamically from the current
   * viewport width so the timeline always fits horizontally.
   *
   * Parallel nodes (same chronological column, e.g. a TV arc and a movie)
   * are stacked vertically and treated as a single column step, so they
   * never break the snake pattern.
   */
  private _computeSnakePositions(graph: FranchiseGraph): {
    positions: Map<string, { x: number; y: number }>;
    layoutInfo: Map<string, LayoutMeta>;
  } {
    const positions = new Map<string, { x: number; y: number }>();
    const layoutInfo = new Map<string, LayoutMeta>();
    if (!graph.nodes.length) return { positions, layoutInfo };

    // ── Determine columns per row from current viewport ─────────────────────
    const stepW = NODE_W + H_GAP;
    const maxNodesPerRow = Math.max(
      MIN_NODES_PER_ROW,
      Math.floor((window.innerWidth - 2 * PADDING) / stepW),
    );

    // ── Build column groups (handles parallel canon) ─────────────────────────
    const trunkCols = this._computeTrunkCols(graph);
    const colValues = Array.from(trunkCols.values());
    const minCol = Math.min(...colValues);

    const colGroups = new Map<number, string[]>();
    for (const [malId, rawCol] of trunkCols) {
      const col = rawCol - minCol;
      if (!colGroups.has(col)) colGroups.set(col, []);
      colGroups.get(col)!.push(malId);
    }
    // Sort nodes within each parallel group for deterministic ordering
    for (const [, ids] of colGroups) ids.sort();

    const sortedSteps = Array.from(colGroups.entries()).sort((a, b) => a[0] - b[0]);
    const totalRows = Math.ceil(sortedSteps.length / maxNodesPerRow);

    // ── Pass 1: compute max stack height per row ─────────────────────────────
    const rowMaxH: number[] = new Array(totalRows).fill(NODE_H);
    sortedSteps.forEach(([, malIds], stepIdx) => {
      const row = Math.floor(stepIdx / maxNodesPerRow);
      const stackH = malIds.length * NODE_H + (malIds.length - 1) * PARALLEL_GAP;
      rowMaxH[row] = Math.max(rowMaxH[row], stackH);
    });

    // ── Pass 2: compute top-Y for each row ───────────────────────────────────
    const rowYStart: number[] = [PADDING];
    for (let r = 0; r < totalRows - 1; r++) {
      rowYStart.push(rowYStart[r] + rowMaxH[r] + V_GAP);
    }

    // ── Pass 3: assign positions ─────────────────────────────────────────────
    sortedSteps.forEach(([, malIds], stepIdx) => {
      const row = Math.floor(stepIdx / maxNodesPerRow);
      const colWithinRow = stepIdx % maxNodesPerRow;
      const isLtr = row % 2 === 0;
      // In RTL rows the rightmost canvas position corresponds to colWithinRow=0
      const xIndex = isLtr ? colWithinRow : maxNodesPerRow - 1 - colWithinRow;
      const x = PADDING + xIndex * stepW;
      const rowDir: 'ltr' | 'rtl' = isLtr ? 'ltr' : 'rtl';
      const rowY = rowYStart[row];
      const stackH = malIds.length * NODE_H + (malIds.length - 1) * PARALLEL_GAP;
      // Centre the parallel stack within the row's height
      const startY = rowY + (rowMaxH[row] - stackH) / 2;

      malIds.forEach((malId, i) => {
        positions.set(malId, { x, y: startY + i * (NODE_H + PARALLEL_GAP) });
        layoutInfo.set(malId, { row, colStep: stepIdx, rowDir });
      });
    });

    return { positions, layoutInfo };
  }

  /**
   * Runs BFS following only prequel/sequel edges from root.
   * Returns Map<malId, rawColumn> where column 0 = root's column,
   * prequel neighbours get column − 1, sequel neighbours get column + 1.
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

  // ── Camera auto-fit ────────────────────────────────────────────────────────

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

  /**
   * Computes an SVG path for one edge, aware of the snake layout:
   *
   * • Same row, LTR: straight line from right edge of earlier node →
   *                  left edge of later node.  Arrow points RIGHT.
   * • Same row, RTL: straight line from left edge of earlier node →
   *                  right edge of later node.  Arrow points LEFT.
   * • Cross-row (U-turn): cubic bezier from the bottom-centre of the last
   *                        node in row N to the top-centre of the first node
   *                        in row N+1, curving away from the centre so the
   *                        path forms a smooth "swoop".
   */
  private _edgePath(
    edge: FranchiseGraphEdge,
    graph: FranchiseGraph,
    pos: Map<string, { x: number; y: number }>,
    meta: Map<string, LayoutMeta>,
  ): string {
    const fromNode = graph.nodes.find((n) => n.malId === edge.fromMalId);
    const toNode = graph.nodes.find((n) => n.malId === edge.toMalId);
    if (!fromNode || !toNode) return '';

    const fromPos = pos.get(edge.fromMalId);
    const toPos = pos.get(edge.toMalId);
    if (!fromPos || !toPos) return '';

    const fromMeta = meta.get(edge.fromMalId);
    const toMeta = meta.get(edge.toMalId);

    if (!fromMeta || !toMeta) {
      // Fallback: simple horizontal line (should not normally occur)
      const leftPos = fromPos.x <= toPos.x ? fromPos : toPos;
      const rightPos = fromPos.x <= toPos.x ? toPos : fromPos;
      const y = leftPos.y + NODE_H / 2;
      return `M ${leftPos.x + NODE_W} ${y} L ${rightPos.x} ${y}`;
    }

    // Identify which node is chronologically earlier (lower colStep)
    const fromIsEarlier = fromMeta.colStep <= toMeta.colStep;
    const earlierPos = fromIsEarlier ? fromPos : toPos;
    const laterPos = fromIsEarlier ? toPos : fromPos;
    const earlierMeta = fromIsEarlier ? fromMeta : toMeta;
    const laterMeta = fromIsEarlier ? toMeta : fromMeta;

    // ── Same row: straight horizontal edge ───────────────────────────────────
    if (earlierMeta.row === laterMeta.row) {
      const fromCy = earlierPos.y + NODE_H / 2;
      const toCy = laterPos.y + NODE_H / 2;
      if (earlierMeta.rowDir === 'ltr') {
        // Earlier is to the LEFT → arrow points RIGHT
        return `M ${earlierPos.x + NODE_W} ${fromCy} L ${laterPos.x} ${toCy}`;
      } else {
        // Earlier is to the RIGHT (RTL row) → arrow points LEFT
        return `M ${earlierPos.x} ${fromCy} L ${laterPos.x + NODE_W} ${toCy}`;
      }
    }

    // ── Cross-row: U-turn bezier ─────────────────────────────────────────────
    // The earlier node sits at the end of its row; the later node starts the
    // next row.  Both share the same canvas X (rightmost or leftmost column).
    // A cubic bezier with control points offset horizontally creates the swoop.
    const fromCx = earlierPos.x + NODE_W / 2; // bottom-centre of earlier node
    const fromCy = earlierPos.y + NODE_H;
    const toCx = laterPos.x + NODE_W / 2;     // top-centre of later node
    const toCy = laterPos.y;
    const r = UTURN_RADIUS;

    if (earlierMeta.row % 2 === 0) {
      // LTR → RTL: turn curves to the RIGHT
      return (
        `M ${fromCx} ${fromCy} ` +
        `C ${fromCx + r} ${fromCy} ${toCx + r} ${toCy} ${toCx} ${toCy}`
      );
    } else {
      // RTL → LTR: turn curves to the LEFT
      return (
        `M ${fromCx} ${fromCy} ` +
        `C ${fromCx - r} ${fromCy} ${toCx - r} ${toCy} ${toCx} ${toCy}`
      );
    }
  }

  private _touchDist(event: TouchEvent): number {
    const [t1, t2] = [event.touches[0], event.touches[1]];
    return Math.hypot(t2.clientX - t1.clientX, t2.clientY - t1.clientY);
  }
}
