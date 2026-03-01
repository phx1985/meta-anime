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
/**
 * Branch depth limit.
 * Trunk nodes (prequel/sequel) are always at depth 0.
 * A direct side branch is depth 1; a side-branch of a side-branch is depth 2.
 * Nodes at depth ≥ MAX_BRANCH_DEPTH will not have their own relations fetched.
 */
const MAX_BRANCH_DEPTH = 2;
/** Relation types that constitute the canonical main trunk. */
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

    // Seed the queue with the root node (branchDepth=0 for trunk)
    const queue: Array<{ malId: string; title: string; coverImageUrl?: string; branchDepth: number }> = [
      { malId: rootMalId, title: rootTitle, coverImageUrl: rootCoverUrl, branchDepth: 0 },
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

      // V9: Do not fetch relations for deep branch nodes (≥ MAX_BRANCH_DEPTH).
      // Mark such nodes as truncated so the UI can show a "..." badge.
      if (item.branchDepth >= MAX_BRANCH_DEPTH) {
        const existing = nodesMap.get(item.malId);
        if (existing) {
          nodesMap.set(item.malId, { ...existing, isTruncated: true });
        }
        this.loadingCount.set(visited.size);
        this.graph.set({
          rootMalId,
          nodes: Array.from(nodesMap.values()),
          edges: Array.from(edgesMap.values()),
        });
        if (queue.length > 0 && !signal.aborted) {
          await this._delay(200, signal);
        }
        continue;
      }

      try {
        const result = await this._fetchNode(item.malId, signal);
        if (signal.aborted) return;

        // Update the node with richer data from the API response
        const updatedNode: FranchiseGraphNode = {
          malId: item.malId,
          title: result.title || item.title,
          coverImageUrl: result.coverImageUrl ?? item.coverImageUrl,
          isRoot: item.malId === rootMalId,
        };

        // Track whether any relations were pruned for this node
        let hasHiddenRelations = false;

        // Process each relation returned by the API
        for (const rel of result.relations ?? []) {
          const isTrunkRelation = TRUNK_RELATION_TYPES.has(rel.relationType ?? '');

          // V9: 40-card kill switch — stop adding nodes once hard cap is reached
          if (nodesMap.size >= MAX_NODES && !nodesMap.has(rel.malId)) {
            hasHiddenRelations = true;
            continue;
          }

          // V9: Branch depth limit — non-trunk relations from depth ≥ MAX_BRANCH_DEPTH-1
          // are not followed. (We still allow the node to exist at depth MAX_BRANCH_DEPTH
          // so it renders, but we skip fetching ITS relations above.)
          const neighborBranchDepth = isTrunkRelation ? 0 : item.branchDepth + 1;

          // Ensure the related node exists in our map
          if (!nodesMap.has(rel.malId)) {
            nodesMap.set(rel.malId, {
              malId: rel.malId,
              title: rel.title,
              coverImageUrl: rel.coverImageUrl,
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
            queue.push({
              malId: rel.malId,
              title: rel.title,
              coverImageUrl: rel.coverImageUrl,
              branchDepth: neighborBranchDepth,
            });
          }
        }

        // Persist updated node (with truncation flag if needed)
        nodesMap.set(item.malId, { ...updatedNode, ...(hasHiddenRelations && { isTruncated: true }) });

        // Update progress counter and emit partial graph for UI feedback
        this.loadingCount.set(visited.size);
        this.graph.set({
          rootMalId,
          nodes: Array.from(nodesMap.values()),
          edges: Array.from(edgesMap.values()),
        });

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
      const finalGraph: FranchiseGraph = {
        rootMalId,
        nodes: Array.from(nodesMap.values()),
        edges: Array.from(edgesMap.values()),
      };
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
}
