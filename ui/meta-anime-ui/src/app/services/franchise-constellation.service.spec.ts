import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { FranchiseConstellationService } from './franchise-constellation.service';
import { AnimeService } from './anime.service';
import { FranchiseConstellationData } from '../models/anime.models';

// ── Helpers ──────────────────────────────────────────────────────────────────

function makeApiNode(
  malId: string,
  relations: Array<{ malId: string; relationType: string }>,
): FranchiseConstellationData {
  return {
    malId,
    title: `Anime ${malId}`,
    relations: relations.map((r) => ({
      malId: r.malId,
      title: `Anime ${r.malId}`,
      relationType: r.relationType,
    })),
  };
}

// ── Suite ─────────────────────────────────────────────────────────────────────

describe('FranchiseConstellationService', () => {
  let service: FranchiseConstellationService;
  let animeServiceMock: { getFranchiseConstellation: ReturnType<typeof vi.fn> };

  beforeEach(() => {
    vi.useFakeTimers();
    animeServiceMock = { getFranchiseConstellation: vi.fn() };
    TestBed.configureTestingModule({
      providers: [
        FranchiseConstellationService,
        { provide: AnimeService, useValue: animeServiceMock },
      ],
    });
    service = TestBed.inject(FranchiseConstellationService);
  });

  afterEach(() => {
    vi.useRealTimers();
    TestBed.resetTestingModule();
  });

  // ── Node deduplication ─────────────────────────────────────────────────────

  describe('node deduplication', () => {
    it('stores each malId only once when both nodes reference each other', async () => {
      // A ←prequel→ B (each side reports the other as its counterpart)
      animeServiceMock.getFranchiseConstellation.mockImplementation((id: string) =>
        of(
          id === 'A'
            ? makeApiNode('A', [{ malId: 'B', relationType: 'prequel' }])
            : makeApiNode('B', [{ malId: 'A', relationType: 'sequel' }]),
        ),
      );

      service.open('A', 'Anime A');
      await vi.runAllTimersAsync();

      const nodes = service.graph()!.nodes;
      const malIds = nodes.map((n) => n.malId);
      expect(new Set(malIds).size).toBe(malIds.length); // no duplicates
      expect(malIds).toContain('A');
      expect(malIds).toContain('B');
      expect(malIds.length).toBe(2);
    });

    it('marks the opening node as root', async () => {
      animeServiceMock.getFranchiseConstellation.mockReturnValue(of(makeApiNode('A', [])));

      service.open('A', 'Anime A');
      await vi.runAllTimersAsync();

      const root = service.graph()!.nodes.find((n) => n.isRoot);
      expect(root?.malId).toBe('A');
    });
  });

  // ── Edge deduplication ─────────────────────────────────────────────────────

  describe('edge deduplication', () => {
    it('stores only one edge for a bidirectional prequel/sequel pair', async () => {
      animeServiceMock.getFranchiseConstellation.mockImplementation((id: string) =>
        of(
          id === 'A'
            ? makeApiNode('A', [{ malId: 'B', relationType: 'prequel' }])
            : makeApiNode('B', [{ malId: 'A', relationType: 'sequel' }]),
        ),
      );

      service.open('A', 'Anime A');
      await vi.runAllTimersAsync();

      const edges = service.graph()!.edges;
      const between = edges.filter(
        (e) =>
          (e.fromMalId === 'A' && e.toMalId === 'B') ||
          (e.fromMalId === 'B' && e.toMalId === 'A'),
      );
      expect(between.length).toBe(1);
    });
  });

  // ── Relation-type filtering ────────────────────────────────────────────────

  describe('relation type filtering', () => {
    it('ignores spin-off, side story, summary, and alternative relations', async () => {
      animeServiceMock.getFranchiseConstellation.mockImplementation((id: string) => {
        if (id === 'A') {
          return of(
            makeApiNode('A', [
              { malId: 'B', relationType: 'prequel' },
              { malId: 'X', relationType: 'spin_off' },
              { malId: 'Y', relationType: 'side_story' },
              { malId: 'Z', relationType: 'summary' },
              { malId: 'W', relationType: 'alternative_setting' },
            ]),
          );
        }
        return of(makeApiNode(id, []));
      });

      service.open('A', 'Anime A');
      await vi.runAllTimersAsync();

      const nodeIds = service.graph()!.nodes.map((n) => n.malId);
      expect(nodeIds).toContain('A');
      expect(nodeIds).toContain('B');
      expect(nodeIds).not.toContain('X');
      expect(nodeIds).not.toContain('Y');
      expect(nodeIds).not.toContain('Z');
      expect(nodeIds).not.toContain('W');
    });

    it('processes sequel relations', async () => {
      animeServiceMock.getFranchiseConstellation.mockImplementation((id: string) => {
        if (id === 'A') return of(makeApiNode('A', [{ malId: 'C', relationType: 'sequel' }]));
        return of(makeApiNode(id, []));
      });

      service.open('A', 'Anime A');
      await vi.runAllTimersAsync();

      const nodeIds = service.graph()!.nodes.map((n) => n.malId);
      expect(nodeIds).toContain('A');
      expect(nodeIds).toContain('C');
    });
  });
});
