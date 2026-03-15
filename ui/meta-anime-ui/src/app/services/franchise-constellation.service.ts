import { Injectable, signal, inject } from '@angular/core';
import { AnimeService } from './anime.service';
import {
  FranchiseConstellationData,
  FranchiseGraph,
  FranchiseGraphNode,
  FranchiseGraphEdge,
} from '../models/anime.models';

/** Hard cap: stop fetching when the graph reaches this many nodes. */
const MAX_NODES = 40;
/** Relation types that constitute the canonical main trunk (prequel/sequel only). */
const TRUNK_RELATION_TYPES = new Set(['prequel', 'sequel']);

@Injectable({ providedIn: 'root' })
export class FranchiseConstellationService {
  private animeService = inject(AnimeService);

  /** Whether the constellation overlay is visible. */
  readonly isOpen = signal(false);
  /** True while the BFS queue is not yet empty. */
  readonly loading = signal(false);
  /** Non-null when the initial fetch failed. */
  readonly error = signal<string | null>(null);
  /** The full franchise graph, populated progressively by BFS. */
  readonly graph = signal<FranchiseGraph | null>(null);
  /** Number of nodes whose relations have been fetched so far (for progress label). */
  readonly loadingCount = signal(0);

  /** Session-scoped cache keyed by root malId → complete FranchiseGraph. */
  private readonly cache = new Map<string, FranchiseGraph>();

  /** AbortController for cancelling an ongoing BFS when a new one starts or overlay closes. */
  private abortCtrl: AbortController | null = null;

  /**
   * Opens the constellation overlay and starts a BFS crawl of the entire franchise.
   */
  open(rootMalId: string, rootTitle: string, rootCoverUrl?: string): void {
    // Cancel any ongoing BFS first
    this.abortCtrl?.abort();
    this.abortCtrl = new AbortController();

    this.error.set(null);
    this.graph.set(null);
    this.loadingCount.set(0);
    this.isOpen.set(true);

    if (this.cache.has(rootMalId)) {
      this.graph.set(this.cache.get(rootMalId)!);
      this.loading.set(false);
      return;
    }

    this.loading.set(true);
    void this._runBfs(rootMalId, rootTitle, rootCoverUrl, this.abortCtrl.signal);
  }

  close(): void {
    this.isOpen.set(false);
    this.abortCtrl?.abort();
  }

  // ── BFS crawler ──────────────────────────────────────────────────────────────

  private async _runBfs(
    rootMalId: string,
    rootTitle: string,
    rootCoverUrl: string | undefined,
    signal: AbortSignal,
  ): Promise<void> {
    /** IDs whose relations have already been fetched (prevents re-fetching). */
    const visited = new Set<string>();
    /** All discovered nodes, keyed by malId. */
    const nodesMap = new Map<string, FranchiseGraphNode>();
    /** All discovered edges, keyed by "fromId→toId" for deduplication. */
    const edgesMap = new Map<string, FranchiseGraphEdge>();

    // Seed the queue with the root node
    const queue: Array<{ malId: string; title: string; coverImageUrl?: string; mediaType?: string; airingStatus?: string; watchingStatus?: string }> = [
      { malId: rootMalId, title: rootTitle, coverImageUrl: rootCoverUrl },
    ];
    nodesMap.set(rootMalId, {
      malId: rootMalId,
      title: rootTitle,
      coverImageUrl: rootCoverUrl,
      isRoot: true,
    });

    while (queue.length > 0 && !signal.aborted) {
      const item = queue.shift()!;

      // Skip if already visited (cycle detection)
      if (visited.has(item.malId)) continue;
      visited.add(item.malId);

      try {
        const result = await this._fetchNode(item.malId, signal);
        if (signal.aborted) return;

        // Update the node with richer data from the API response
        const updatedNode: FranchiseGraphNode = {
          malId: item.malId,
          title: result.title || item.title,
          coverImageUrl: result.coverImageUrl ?? item.coverImageUrl,
          synopsis: result.synopsis,
          startDate: result.startDate,
          endDate: result.endDate,
          mediaType: result.mediaType ?? item.mediaType,
          airingStatus: result.airingStatus,
          watchingStatus: result.watchingStatus,
          isRoot: item.malId === rootMalId,
        };

        // Track whether any relations were pruned by the 40-card kill switch
        let hasHiddenRelations = false;

        // Process ONLY prequel/sequel relations — ignore spin-offs, side stories, etc.
        for (const rel of result.relations ?? []) {
          if (!TRUNK_RELATION_TYPES.has(rel.relationType ?? '')) continue;

          // 40-card kill switch — stop adding nodes once hard cap is reached
          if (nodesMap.size >= MAX_NODES && !nodesMap.has(rel.malId)) {
            hasHiddenRelations = true;
            continue;
          }

          // Ensure the related node exists in our map
          if (!nodesMap.has(rel.malId)) {
            nodesMap.set(rel.malId, {
              malId: rel.malId,
              title: rel.title,
              coverImageUrl: rel.coverImageUrl,
              mediaType: rel.mediaType,
              airingStatus: rel.airingStatus,
              watchingStatus: rel.watchingStatus,
              isRoot: false,
            });
          }

          // Add edge (avoid duplicates by checking both directions)
          const fwd = `${item.malId}→${rel.malId}`;
          const rev = `${rel.malId}→${item.malId}`;
          if (!edgesMap.has(fwd) && !edgesMap.has(rev)) {
            edgesMap.set(fwd, {
              fromMalId: item.malId,
              toMalId: rel.malId,
              relationType: rel.relationType,
            });
          }

          // Enqueue if not yet visited
          if (!visited.has(rel.malId)) {
            queue.push({ malId: rel.malId, title: rel.title, coverImageUrl: rel.coverImageUrl, mediaType: rel.mediaType, airingStatus: rel.airingStatus, watchingStatus: rel.watchingStatus });
          }
        }

        // Persist updated node (with truncation flag if needed)
        nodesMap.set(item.malId, { ...updatedNode, ...(hasHiddenRelations && { isTruncated: true }) });

        // Update progress counter only — full graph emitted once BFS completes
        this.loadingCount.set(visited.size);

        // Respect MAL API rate limit — 200 ms between calls
        if (queue.length > 0 && !signal.aborted) {
          await this._delay(200, signal);
        }
      } catch (err) {
        if (signal.aborted) return;
        // A single node failure is non-fatal — log and continue BFS
        console.warn(`[FranchiseConstellation] Failed to fetch relations for ${item.malId}:`, err);
      }
    }

    if (!signal.aborted) {
      const finalGraph = this._buildFinalGraph(rootMalId, nodesMap, edgesMap);
      this.cache.set(rootMalId, finalGraph);
      this.graph.set(finalGraph);
      this.loading.set(false);
    }
  }

  /** Wraps the HTTP observable as a Promise so we can use async/await. */
  private _fetchNode(malId: string, signal: AbortSignal): Promise<FranchiseConstellationData> {
    return new Promise((resolve, reject) => {
      const sub = this.animeService.getFranchiseConstellation(malId).subscribe({
        next: resolve,
        error: reject,
      });
      signal.addEventListener('abort', () => {
        sub.unsubscribe();
        reject(new DOMException('BFS aborted', 'AbortError'));
      });
    });
  }

  /** Returns a promise that resolves after `ms` milliseconds, or rejects if aborted. */
  private _delay(ms: number, signal: AbortSignal): Promise<void> {
    return new Promise((resolve, reject) => {
      const id = setTimeout(resolve, ms);
      signal.addEventListener('abort', () => {
        clearTimeout(id);
        reject(new DOMException('BFS aborted', 'AbortError'));
      });
    });
  }

  // ── Graph finalisation helpers ────────────────────────────────────────────────

  /**
   * Assigns a chronological column number to every node reachable via
   * prequel/sequel edges from `startId`.
   * sequel neighbour → current col + 1, prequel neighbour → current col − 1.
   */
  private _computeTrunkCols(
    edges: FranchiseGraphEdge[],
    startId: string,
  ): Map<string, number> {
    const cols = new Map<string, number>([[startId, 0]]);
    const queue = [startId];
    const visited = new Set<string>([startId]);

    while (queue.length > 0) {
      const cur = queue.shift()!;
      const curCol = cols.get(cur) ?? 0;

      for (const edge of edges) {
        if (!TRUNK_RELATION_TYPES.has(edge.relationType ?? '')) continue;

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
          cols.set(neighborId, curCol + delta);
          queue.push(neighborId);
        }
      }
    }
    return cols;
  }

  /**
   * Produces the definitive FranchiseGraph from the BFS-collected maps:
   * 1. Computes trunk columns to determine every node's chronological position.
   * 2. Identifies the true root — the node with the lowest (oldest) column value.
   * 3. Sorts nodes in ascending chronological order (oldest at index 0).
   * 4. Updates `isRoot` flags and `rootMalId` to reflect the true root.
   */
  private _buildFinalGraph(
    clickedRootMalId: string,
    nodesMap: Map<string, FranchiseGraphNode>,
    edgesMap: Map<string, FranchiseGraphEdge>,
  ): FranchiseGraph {
    const edges = Array.from(edgesMap.values());
    const trunkCols = this._computeTrunkCols(edges, clickedRootMalId);

    // True root = node with the smallest (most-negative) column value
    let trueRootId = clickedRootMalId;
    let minCol = trunkCols.get(clickedRootMalId) ?? 0;
    for (const [malId, col] of trunkCols) {
      if (col < minCol) {
        minCol = col;
        trueRootId = malId;
      }
    }

    // Sort nodes chronologically; malId is a stable tiebreaker within the same column
    const sortedNodes = Array.from(nodesMap.values()).sort((a, b) => {
      const colA = trunkCols.get(a.malId) ?? 0;
      const colB = trunkCols.get(b.malId) ?? 0;
      if (colA !== colB) return colA - colB;
      return a.malId.localeCompare(b.malId);
    });

    return {
      rootMalId: trueRootId,
      nodes: sortedNodes.map((n) => ({ ...n, isRoot: n.malId === trueRootId })),
      edges,
    };
  }
}
