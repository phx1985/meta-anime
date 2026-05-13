import {
  ChangeDetectionStrategy,
  Component,
  HostListener,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { TranslatePipe } from '@ngx-translate/core';
import { SeiyuuNexusService } from './seiyuu-nexus.service';
import { NexusNode, NexusEdge } from '../models/anime.models';

const CANVAS_SIZE = 8000;
const RING_CHAR = 280;
const RING_VA = 560;
const RING_OTHER = 840;
const ORIGIN = CANVAS_SIZE / 2;

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

  private isDragging = false;
  private dragStartX = 0;
  private dragStartY = 0;
  private dragStartTx = 0;
  private dragStartTy = 0;
  private hasDragged = false;

  protected readonly positionedNodes = computed<PositionedNode[]>(() => {
    const nexus = this.svc.nexus();
    if (!nexus) return [];

    const nodes = nexus.nodes;
    const root = nodes.find(n => n.isRoot);
    const chars = nodes.filter(n => n.type === 'character');
    const vas = nodes.filter(n => n.type === 'va');
    const others = nodes.filter(n => n.type === 'anime' && !n.isRoot);

    const placed: PositionedNode[] = [];

    if (root) placed.push({ ...root, x: ORIGIN, y: ORIGIN });

    chars.forEach((n, i) => {
      const angle = (2 * Math.PI * i) / Math.max(chars.length, 1) - Math.PI / 2;
      placed.push({ ...n, x: ORIGIN + RING_CHAR * Math.cos(angle), y: ORIGIN + RING_CHAR * Math.sin(angle) });
    });

    vas.forEach((n, i) => {
      const angle = (2 * Math.PI * i) / Math.max(vas.length, 1) - Math.PI / 2;
      placed.push({ ...n, x: ORIGIN + RING_VA * Math.cos(angle), y: ORIGIN + RING_VA * Math.sin(angle) });
    });

    others.forEach((n, i) => {
      const angle = (2 * Math.PI * i) / Math.max(others.length, 1) - Math.PI / 2;
      placed.push({ ...n, x: ORIGIN + RING_OTHER * Math.cos(angle), y: ORIGIN + RING_OTHER * Math.sin(angle) });
    });

    return placed;
  });

  protected readonly positionedEdges = computed<Array<{
    edge: NexusEdge;
    x1: number; y1: number;
    x2: number; y2: number;
    dashed: boolean;
  }>>(() => {
    const nexus = this.svc.nexus();
    if (!nexus) return [];

    const nodeMap = new Map<string, PositionedNode>();
    for (const n of this.positionedNodes()) nodeMap.set(n.id, n);

    return nexus.edges
      .map(edge => {
        const from = nodeMap.get(edge.fromId);
        const to = nodeMap.get(edge.toId);
        return { edge, x1: from?.x ?? 0, y1: from?.y ?? 0, x2: to?.x ?? 0, y2: to?.y ?? 0, dashed: edge.relation === 'appearsIn' };
      })
      .filter(e => e.x1 !== 0 || e.y1 !== 0 || e.x2 !== 0 || e.y2 !== 0);
  });

  protected readonly lastUpdatedLocal = computed(() => {
    const raw = this.svc.nexus()?.lastUpdated;
    if (!raw) return null;
    const d = new Date(raw);
    const pad = (n: number) => String(n).padStart(2, '0');
    return `${pad(d.getDate())}/${pad(d.getMonth() + 1)}/${d.getFullYear()} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
  });

  readonly canvasSize = CANVAS_SIZE;

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

  protected get canvasTransform(): string {
    return `translate(${this.translateX()}px, ${this.translateY()}px) scale(${this.scale()})`;
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
