import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  HostListener,
  OnDestroy,
  OnInit,
  ViewChild,
  computed,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
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
  relationType?: string;
}

interface GraphEdge {
  x1: number;
  y1: number;
  x2: number;
  y2: number;
}

/** Width/height constants for node layout. */
const CENTER_W = 120;
const CENTER_H = 170;
const NODE_W = 100;
const NODE_H = 60;
const H_GAP = 160; // horizontal gap between center and related column
const V_GAP = 20; // vertical gap between related nodes

@Component({
  selector: 'app-franchise-constellation',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule],
  templateUrl: './franchise-constellation.component.html',
  styleUrl: './franchise-constellation.component.scss',
})
export class FranchiseConstellationComponent implements OnInit, OnDestroy {
  protected readonly svc = inject(FranchiseConstellationService);
  private readonly popupService = inject(PopupService);

  @ViewChild('graphContainer') graphContainerRef?: ElementRef<HTMLElement>;
  @ViewChild('graphInner') graphInnerRef?: ElementRef<HTMLElement>;

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

  protected edges = computed<GraphEdge[]>(() => {
    const ns = this.nodes();
    if (!ns.length) return [];
    const center = ns.find((n) => n.isCenter);
    if (!center) return [];
    const related = ns.filter((n) => !n.isCenter);
    return related.map((n) => ({
      x1: center.x + CENTER_W,
      y1: center.y + CENTER_H / 2,
      x2: n.x,
      y2: n.y + NODE_H / 2,
    }));
  });

  protected svgViewBox = computed(() => {
    const ns = this.nodes();
    if (!ns.length) return '0 0 600 400';
    const padding = 30;
    const minX = Math.min(...ns.map((n) => n.x)) - padding;
    const minY = Math.min(...ns.map((n) => n.y)) - padding;
    const maxX = Math.max(...ns.map((n) => n.x + (n.isCenter ? CENTER_W : NODE_W))) + padding;
    const maxY = Math.max(...ns.map((n) => n.y + (n.isCenter ? CENTER_H : NODE_H))) + padding;
    return `${minX} ${minY} ${maxX - minX} ${maxY - minY}`;
  });

  protected graphWidth = computed(() => {
    const ns = this.nodes();
    if (!ns.length) return 600;
    const padding = 60;
    return (
      Math.max(...ns.map((n) => n.x + (n.isCenter ? CENTER_W : NODE_W))) +
      padding
    );
  });

  protected graphHeight = computed(() => {
    const ns = this.nodes();
    if (!ns.length) return 400;
    const padding = 60;
    return (
      Math.max(...ns.map((n) => n.y + (n.isCenter ? CENTER_H : NODE_H))) +
      padding
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

  // ── Pan interactions ──────────────────────────────────────────────────────

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
    const dx = event.clientX - this.panStartX;
    const dy = event.clientY - this.panStartY;
    this.translateX.set(this.panStartTx + dx);
    this.translateY.set(this.panStartTy + dy);
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

  // ── Layout algorithm ──────────────────────────────────────────────────────

  private _computeNodes(
    malId: string,
    title: string,
    coverImageUrl: string | undefined,
    relations: FranchiseRelation[],
  ): GraphNode[] {
    const nodes: GraphNode[] = [];

    // Sort relations: prequels/sequels first, then others — giving a
    // roughly chronological Left-to-Right reading order
    const ORDER: Record<string, number> = {
      prequel: 0,
      sequel: 1,
      alternative_version: 2,
      alternative_setting: 3,
      side_story: 4,
      summary: 5,
      other: 99,
    };
    const sorted = [...relations].sort((a, b) => {
      const ao = ORDER[a.relationType ?? 'other'] ?? 98;
      const bo = ORDER[b.relationType ?? 'other'] ?? 98;
      return ao - bo;
    });

    // Total height of related-nodes column
    const totalRelH =
      sorted.length > 0
        ? sorted.length * NODE_H + (sorted.length - 1) * V_GAP
        : 0;

    const centerY = Math.max(0, (totalRelH - CENTER_H) / 2);
    const leftPad = 40;

    // Center node
    nodes.push({
      malId,
      title,
      coverImageUrl,
      x: leftPad,
      y: centerY,
      isCenter: true,
    });

    // Related nodes in a vertical column to the right
    const relX = leftPad + CENTER_W + H_GAP;
    const relStartY = Math.max(0, (CENTER_H - totalRelH) / 2);
    sorted.forEach((rel, i) => {
      nodes.push({
        malId: rel.malId,
        title: rel.title,
        coverImageUrl: rel.coverImageUrl,
        x: relX,
        y: relStartY + i * (NODE_H + V_GAP),
        isCenter: false,
        relationType: rel.relationTypeFormatted ?? rel.relationType ?? '',
      });
    });

    return nodes;
  }

  private _touchDist(event: TouchEvent): number {
    const [t1, t2] = [event.touches[0], event.touches[1]];
    return Math.hypot(t2.clientX - t1.clientX, t2.clientY - t1.clientY);
  }

  openMalPage(malId: string): void {
    window.open(`https://myanimelist.net/anime/${malId}`, '_blank', 'noopener,noreferrer');
  }
}
