import {
  ChangeDetectionStrategy,
  Component,
  HostListener,
  OnDestroy,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { FranchiseConstellationService } from '../services/franchise-constellation.service';
import { PopupService } from '../services/popup.service';
import { FranchiseRelation } from '../models/anime.models';

interface GraphNode {
  malId: string;
  title: string;
  coverImageUrl?: string;
  x: number;
  y: number;
  isCenter: boolean;
  /** Raw MAL relation_type key (e.g. 'prequel', 'sequel') used for i18n lookup. */
  relationTypeKey?: string;
  /** Column zone: 'left' | 'center' | 'right' | 'other' */
  zone: 'left' | 'center' | 'right' | 'other';
}

interface GraphEdge {
  /** SVG path data string */
  d: string;
  /** Arrow direction: 'right' | 'left' | 'down' */
  direction: 'right' | 'left' | 'down';
}

// ── Layout constants ─────────────────────────────────────────────────────────
/** Poster card dimensions for related nodes */
const NODE_W = 144;
const NODE_H = 200;
/** Slightly larger card for the center (clicked) anime */
const CENTER_W = 160;
const CENTER_H = 224;
/** Horizontal gap between columns */
const H_GAP = 72;
/** Vertical gap between nodes stacked in the same column */
const V_GAP = 20;
/** Padding on all sides of the canvas */
const PADDING = 40;

/** Relation types that sit in the LEFT zone (came before) */
const LEFT_TYPES = new Set(['prequel', 'parent_story']);
/** Relation types that sit in the RIGHT zone (came after) */
const RIGHT_TYPES = new Set(['sequel', 'spin_off']);

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

  // ── Pan/zoom state ────────────────────────────────────────────────────────
  protected translateX = signal(0);
  protected translateY = signal(0);
  protected scale = signal(1);

  protected graphTransform = computed(
    () =>
      `translate(${this.translateX()}px, ${this.translateY()}px) scale(${this.scale()})`,
  );

  private isPanning = false;
  private panStartX = 0;
  private panStartY = 0;
  private panStartTx = 0;
  private panStartTy = 0;
  private lastPinchDist = 0;

  // ── Graph computed layout ─────────────────────────────────────────────────
  protected nodes = computed<GraphNode[]>(() => {
    const d = this.svc.data();
    if (!d) return [];
    return this._computeNodes(d.malId, d.title, d.coverImageUrl, d.relations);
  });

  protected edges = computed<GraphEdge[]>(() => this._computeEdges(this.nodes()));

  protected graphWidth = computed(() => {
    const ns = this.nodes();
    if (!ns.length) return 600;
    return (
      Math.max(...ns.map((n) => n.x + (n.isCenter ? CENTER_W : NODE_W))) + PADDING
    );
  });

  protected graphHeight = computed(() => {
    const ns = this.nodes();
    if (!ns.length) return 400;
    return (
      Math.max(...ns.map((n) => n.y + (n.isCenter ? CENTER_H : NODE_H))) + PADDING
    );
  });

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

  // ── Pan / zoom interactions ───────────────────────────────────────────────

  onPointerDown(event: PointerEvent): void {
    if ((event.target as HTMLElement).closest('.constellation-node')) return;
    this.isPanning = true;
    this.panStartX = event.clientX;
    this.panStartY = event.clientY;
    this.panStartTx = this.translateX();
    this.panStartTy = this.translateY();
    (event.currentTarget as HTMLElement).setPointerCapture(event.pointerId);
  }

  onPointerMove(event: PointerEvent): void {
    if (!this.isPanning) return;
    this.translateX.set(this.panStartTx + event.clientX - this.panStartX);
    this.translateY.set(this.panStartTy + event.clientY - this.panStartY);
  }

  onPointerUp(): void {
    this.isPanning = false;
  }

  onWheel(event: WheelEvent): void {
    event.preventDefault();
    const factor = event.deltaY < 0 ? 1.1 : 0.9;
    this.scale.set(Math.min(3, Math.max(0.25, this.scale() * factor)));
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
        this.scale.set(Math.min(3, Math.max(0.25, this.scale() * factor)));
      }
      this.lastPinchDist = dist;
    }
  }

  openMalPage(malId: string): void {
    window.open(`https://myanimelist.net/anime/${malId}`, '_blank', 'noopener,noreferrer');
  }

  /** Returns the i18n key for a given raw MAL relation_type. */
  relationI18nKey(typeKey: string | undefined): string {
    const key = typeKey ?? 'other';
    return `franchise.relationType.${key}`;
  }

  // ── Layout algorithm ──────────────────────────────────────────────────────

  private _computeNodes(
    malId: string,
    title: string,
    coverImageUrl: string | undefined,
    relations: FranchiseRelation[],
  ): GraphNode[] {
    const nodes: GraphNode[] = [];

    const prequels = relations.filter((r) => LEFT_TYPES.has(r.relationType ?? ''));
    const sequels = relations.filter((r) => RIGHT_TYPES.has(r.relationType ?? ''));
    const others = relations.filter(
      (r) => !LEFT_TYPES.has(r.relationType ?? '') && !RIGHT_TYPES.has(r.relationType ?? ''),
    );

    // Helper: total column height for N items
    const colH = (count: number, h: number) =>
      count > 0 ? count * h + (count - 1) * V_GAP : 0;

    const leftColH = colH(prequels.length, NODE_H);
    const rightColH = colH(sequels.length, NODE_H);
    // Main row height is the tallest of the three center-row zones
    const mainRowH = Math.max(leftColH, CENTER_H, rightColH);

    // X positions for each zone
    const leftX = PADDING;
    const centerX = PADDING + (prequels.length > 0 ? NODE_W + H_GAP : 0);
    const rightX = centerX + CENTER_W + H_GAP;

    // Vertically center each column within the main row
    const leftStartY = PADDING + (mainRowH - leftColH) / 2;
    const centerY = PADDING + (mainRowH - CENTER_H) / 2;
    const rightStartY = PADDING + (mainRowH - rightColH) / 2;

    // Y position for "others" row — just below the main row
    const othersRowY = PADDING + mainRowH + H_GAP;

    // Center node
    nodes.push({ malId, title, coverImageUrl, x: centerX, y: centerY, isCenter: true, zone: 'center' });

    // Prequel nodes (left column)
    prequels.forEach((rel, i) => {
      nodes.push({
        malId: rel.malId,
        title: rel.title,
        coverImageUrl: rel.coverImageUrl,
        x: leftX,
        y: leftStartY + i * (NODE_H + V_GAP),
        isCenter: false,
        relationTypeKey: rel.relationType ?? 'other',
        zone: 'left',
      });
    });

    // Sequel nodes (right column)
    sequels.forEach((rel, i) => {
      nodes.push({
        malId: rel.malId,
        title: rel.title,
        coverImageUrl: rel.coverImageUrl,
        x: rightX,
        y: rightStartY + i * (NODE_H + V_GAP),
        isCenter: false,
        relationTypeKey: rel.relationType ?? 'other',
        zone: 'right',
      });
    });

    // Other nodes (bottom row, centered beneath center node)
    const othersRowW = colH(others.length, NODE_W); // reuse helper for width
    const othersStartX = centerX + CENTER_W / 2 - othersRowW / 2;
    others.forEach((rel, i) => {
      nodes.push({
        malId: rel.malId,
        title: rel.title,
        coverImageUrl: rel.coverImageUrl,
        x: othersStartX + i * (NODE_W + H_GAP),
        y: othersRowY,
        isCenter: false,
        relationTypeKey: rel.relationType ?? 'other',
        zone: 'other',
      });
    });

    return nodes;
  }

  private _computeEdges(nodes: GraphNode[]): GraphEdge[] {
    const center = nodes.find((n) => n.isCenter);
    if (!center) return [];

    return nodes
      .filter((n) => !n.isCenter)
      .map((n) => {
        const cx = center.x + CENTER_W / 2;
        const cy = center.y + CENTER_H / 2;

        if (n.zone === 'left') {
          // Arrow: node right edge → center left edge
          const x1 = n.x + NODE_W;
          const y1 = n.y + NODE_H / 2;
          const x2 = center.x;
          const y2 = cy;
          const mx = (x1 + x2) / 2;
          return { d: `M ${x1} ${y1} C ${mx} ${y1} ${mx} ${y2} ${x2} ${y2}`, direction: 'right' as const };
        } else if (n.zone === 'right') {
          // Arrow: center right edge → node left edge
          const x1 = center.x + CENTER_W;
          const y1 = cy;
          const x2 = n.x;
          const y2 = n.y + NODE_H / 2;
          const mx = (x1 + x2) / 2;
          return { d: `M ${x1} ${y1} C ${mx} ${y1} ${mx} ${y2} ${x2} ${y2}`, direction: 'right' as const };
        } else {
          // Arrow: center bottom → node top
          const x1 = cx;
          const y1 = center.y + CENTER_H;
          const x2 = n.x + NODE_W / 2;
          const y2 = n.y;
          const my = (y1 + y2) / 2;
          return { d: `M ${x1} ${y1} C ${x1} ${my} ${x2} ${my} ${x2} ${y2}`, direction: 'down' as const };
        }
      });
  }

  private _touchDist(event: TouchEvent): number {
    const [t1, t2] = [event.touches[0], event.touches[1]];
    return Math.hypot(t2.clientX - t1.clientX, t2.clientY - t1.clientY);
  }
}
