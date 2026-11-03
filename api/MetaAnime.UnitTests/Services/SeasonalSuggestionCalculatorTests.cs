using FluentAssertions;
using MetaAnime.Application.Utilities;

namespace MetaAnime.UnitTests.Services;

/// <summary>
/// Pins the "For you" ranking rules. The deck is only trustworthy if it never recommends
/// something already watched, never leaks an unwatched sequel, still explains itself, and stays
/// deterministic for identical inputs.
/// </summary>
public class SeasonalSuggestionCalculatorTests
{
    // ── fixtures ────────────────────────────────────────────────────────────────
    private static TasteEntry Rated(string malId, string title, int score, params string[] tags) =>
        new(malId, title, score, "completed", tags);

    private static TasteEntry Status(string malId, string title, string status, params string[] tags) =>
        new(malId, title, 0, status, tags);

    private static SeasonalCandidate Candidate(
        string malId, string title, string[] tags, string? viewState = null, double mean = 8.0,
        bool inLibrary = false) =>
        new(malId, title, tags, mean, viewState, inLibrary);

    // ── 1. profile ──────────────────────────────────────────────────────────────
    [Fact]
    public void BuildsAPositiveProfileFromHighScores_AndNegativeFromLowOnes()
    {
        var history = new[]
        {
            Rated("1", "Loved Action", 9, "Action", "Shounen"),
            Rated("2", "Hated Comedy", 3, "Comedy"),
            Rated("3", "Neutral Action", 6, "Action"), // weight +0.11
        };

        var candidates = new[]
        {
            Candidate("100", "Action Thing", ["Action", "Shounen"], mean: 7.0),
            Candidate("200", "Comedy Thing", ["Comedy"], mean: 7.0),
        };

        var picks = SeasonalSuggestionCalculator.Compute(candidates, history, new SuggestionOptions(Limit: 10));

        // Comedy is a disliked tag → it carries no positive weight → filtered out entirely.
        picks.Select(p => p.Candidate.MalId).Should().Equal("100");
        picks[0].MatchScore.Should().BeGreaterThan(0);
    }

    // ── 2. ranking ──────────────────────────────────────────────────────────────
    [Fact]
    public void RanksCandidatesSharingTheTopTagsAboveAnUnrelatedBetterScoredTitle()
    {
        var history = new[] { Rated("1", "Loved", 10, "Drama", "Supernatural") };

        var candidates = new[]
        {
            Candidate("100", "Unrelated Masterpiece", ["Mecha", "Sci-Fi"], mean: 9.5),
            Candidate("200", "Matching", ["Drama", "Supernatural"], mean: 6.5),
        };

        var picks = SeasonalSuggestionCalculator.Compute(candidates, history, new SuggestionOptions(Limit: 10));

        picks.Should().HaveCount(1, "the unrelated title has no positive overlap with the profile");
        picks[0].Candidate.MalId.Should().Be("200");
    }

    // ── 3. consumed / planned ───────────────────────────────────────────────────
    [Fact]
    public void ExcludesEverythingAlreadyConsumed_AndDemotesPlanToWatch()
    {
        var history = new[]
        {
            Rated("1", "Loved", 9, "Action"),
            Status("2", "Dropped", "dropped", "Action"),
            Status("3", "Watching", "watching", "Action"),
        };

        var candidates = new[]
        {
            Candidate("2", "Dropped One", ["Action"], viewState: "dropped"),
            Candidate("3", "Watching One", ["Action"], viewState: "watching"),
            Candidate("4", "Completed One", ["Action"], viewState: "completed"),
            Candidate("5", "Planned One", ["Action"], viewState: "plan_to_watch"),
            Candidate("6", "Fresh One", ["Action"]),
        };

        var picks = SeasonalSuggestionCalculator.Compute(candidates, history, new SuggestionOptions(Limit: 10));

        picks.Select(p => p.Candidate.MalId).Should().BeEquivalentTo(["5", "6"]);
        picks.Select(p => p.Candidate.MalId).Should().NotContain(["2", "3", "4"]);

        // plan_to_watch is kept but ranked behind the fresh pick
        picks[0].Candidate.MalId.Should().Be("6");
        picks[0].MatchScore.Should().BeGreaterThan(picks[1].MatchScore);
    }

    // ── 4. sequels ──────────────────────────────────────────────────────────────
    [Fact]
    public void ExcludesASequelWhosePrequelWasNeverWatched_AndKeepsItWhenThePrequelIsCompleted()
    {
        // A dropped prequel plus one liked title, so "Action" carries a positive weight.
        var unwatched = new[]
        {
            Status("1", "Jujutsu Kaisen", "dropped", "Action"),
            Rated("9", "Loved Action Show", 9, "Action"),
        };
        var watched = new[]
        {
            Status("1", "Jujutsu Kaisen", "completed", "Action"),
            Rated("9", "Loved Action Show", 9, "Action"),
        };

        var candidates = new[]
        {
            Candidate("2", "Jujutsu Kaisen 2nd Season", ["Action"]),
            Candidate("3", "Completely Different Show", ["Action"]),
        };

        SeasonalSuggestionCalculator.Compute(candidates, unwatched, new SuggestionOptions(Limit: 10))
            .Select(p => p.Candidate.MalId).Should().Equal("3");

        SeasonalSuggestionCalculator.Compute(candidates, watched, new SuggestionOptions(Limit: 10))
            .Select(p => p.Candidate.MalId).Should().BeEquivalentTo(["2", "3"]);
    }

    [Theory]
    [InlineData("Frieren Season 2", true)]
    [InlineData("Mushishi 2nd Season", true)]
    [InlineData("Attack on Titan Final Season", true)]
    [InlineData("Kaguya-sama: Love Is War? Part 2", true)]
    [InlineData("Cowboy Bebop", false)]
    [InlineData("Mob Psycho 100", false)]
    public void DetectsSequelMarkersInTitles(string title, bool expected) =>
        SeasonalSuggestionCalculator.HasSequelMarker(title).Should().Be(expected);

    [Theory]
    [InlineData("Jujutsu Kaisen 2nd Season", "jujutsu kaisen")]
    [InlineData("Kaguya-sama: Love Is War? Part 2", "kaguya sama")]
    [InlineData("  Made in Abyss  ", "made in abyss")]
    public void NormalisesTheFranchiseKey(string title, string expected) =>
        SeasonalSuggestionCalculator.FranchiseKey(title).Should().Be(expected);

    // ── 5. diversity ────────────────────────────────────────────────────────────
    [Fact]
    public void CapsTwoTitlesPerFranchiseAndThreePerDominantTag()
    {
        var history = new[] { Rated("1", "Loved", 10, "Action") };

        var candidates = Enumerable.Range(0, 6)
            .Select(i => Candidate($"{100 + i}", $"Action Thing {i}", ["Action"]))
            .Concat(Enumerable.Range(0, 3)
                .Select(i => Candidate($"{200 + i}", $"Bleach {i}", ["Action"])))
            .ToArray();

        var picks = SeasonalSuggestionCalculator.Compute(candidates, history, new SuggestionOptions(Limit: 10));

        picks.Should().HaveCount(4, "the tag cap gives 3, then the starvation floor fills the 4-slot deck");
    }

    // ── 6. cold start ───────────────────────────────────────────────────────────
    [Fact]
    public void ColdStartWithoutHistoryFallsBackToCommunityScoreWithAPopularReason()
    {
        var candidates = new[]
        {
            Candidate("100", "Quiet Season", ["Slice of Life"], mean: 6.9),
            Candidate("200", "Hyped Show", ["Action"], mean: 9.0),
        };

        var picks = SeasonalSuggestionCalculator.Compute(candidates, [], new SuggestionOptions(Limit: 10));

        picks.Should().NotBeEmpty();
        picks[0].Candidate.MalId.Should().Be("200");
        picks.Should().OnlyContain(p => p.Reasons[0].Kind == "popular");
        picks.Should().OnlyContain(p => p.Reasons[0].Tags.Count == 0);
    }

    // ── 7. limit + determinism + explanations ───────────────────────────────────
    [Fact]
    public void RespectsTheLimit_AndIsStableForIdenticalInputs()
    {
        var history = new[] { Rated("1", "Loved Mushishi", 9, "Supernatural"), Rated("2", "Other", 8, "Supernatural") };

        var candidates = Enumerable.Range(0, 12)
            .Select(i => Candidate($"{300 + i}", $"Show {i}", ["Supernatural"]))
            .ToArray();

        SeasonalSuggestionCalculator.Compute(candidates, history, new SuggestionOptions(Limit: 2))
            .Should().HaveCount(2, "the limit itself is respected");

        var first = SeasonalSuggestionCalculator.Compute(candidates, history, new SuggestionOptions(Limit: 10));
        var second = SeasonalSuggestionCalculator.Compute(candidates, history, new SuggestionOptions(Limit: 4));

        // 12 candidates share one dominant tag → the cap yields 3, the deck floor tops it to 4.
        first.Should().HaveCount(4);
        first.Select(p => p.Candidate.MalId).Should().Equal(second.Select(p => p.Candidate.MalId));

        // Explainability: the reason points at real rated titles sharing the tag.
        var reason = first[0].Reasons[0];
        reason.Kind.Should().Be("taste");
        reason.Tags.Should().Contain("Supernatural");
        reason.Because.Select(b => b.MalId).Should().Contain("1");
    }

    // ── 8. strict library exclusion (MAL / Plex / Sonarr) ───────────────────────
    [Fact]
    public void ExcludesEveryCandidateAlreadyInTheLibrary_WhateverTheProvider()
    {
        var history = new[] { Rated("1", "Loved", 10, "Action") };

        var candidates = new[]
        {
            Candidate("10", "Owned In Plex", ["Action"], inLibrary: true),      // InPlex
            Candidate("11", "Tracked In MAL", ["Action"], inLibrary: true),     // InMal
            Candidate("12", "Monitored In Sonarr", ["Action"], inLibrary: true),// InSonarr
            Candidate("13", "Planned Already", ["Action"], inLibrary: true, viewState: "plan_to_watch"),
            Candidate("14", "Genuine Discovery", ["Action"]),
        };

        var picks = SeasonalSuggestionCalculator.Compute(candidates, history, new SuggestionOptions(Limit: 10));

        picks.Select(p => p.Candidate.MalId).Should().Equal(["14"]);
    }

    // ── 9. scope windows (custom ranges are capped to 4 seasons) ────────────────
    [Theory]
    [InlineData("current", 1, "fall", "2026")]
    [InlineData("last2", 2, "summer", "2026")]
    [InlineData("last4", 4, "winter", "2026")]
    public void ResolvesRelativeScopesChronologically(string scope, int count, string firstSeason, string firstYear)
    {
        var windows = SeasonalScopeResolver.ResolveSeasons(2026, "fall", scope);

        windows.Should().HaveCount(count);
        windows[0].Season.Should().Be(firstSeason);
        windows[0].Year.Should().Be(int.Parse(firstYear));
        windows[^1].Should().Be((2026, "fall"));
    }

    [Fact]
    public void CapsACustomRangeToFourSeasonsEndingAtTheToWindow()
    {
        var windows = SeasonalScopeResolver.ResolveSeasons(
            2026, "fall", "custom", fromYear: 2024, fromSeason: "winter", toYear: 2026, toSeason: "fall");

        windows.Should().HaveCount(4);
        windows[^1].Should().Be((2026, "fall"));
    }

    // ── 10. nearest-neighbour attribution (tag specificity) ─────────────────────
    [Fact]
    public void CitesTheActualNearestNeighbour_NotTheBroadDramaOverlap()
    {
        var history = new[]
        {
            Rated("1", "Elfen Lied", 9, "Drama", "Horror", "Gore", "Psychological", "Seinen"),
            Rated("2", "Lovely Complex", 9, "Drama", "Romance", "Comedy", "Shoujo"),
        };

        var candidates = new[]
        {
            Candidate("100", "Romance Show", ["Romance", "Shoujo", "Drama"]),
            Candidate("200", "Gore Show", ["Horror", "Gore", "Drama"]),
        };

        var picks = SeasonalSuggestionCalculator.Compute(candidates, history, new SuggestionOptions(Limit: 10));

        var romance = picks.Single(p => p.Candidate.MalId == "100");
        var gore = picks.Single(p => p.Candidate.MalId == "200");

        romance.Reasons[0].Because.Single().Title.Should().Be("Lovely Complex");
        gore.Reasons[0].Because.Single().Title.Should().Be("Elfen Lied");
    }

    [Fact]
    public void AgenericTagAloneNeverJustifiesACitation()
    {
        var history = new[] { Rated("1", "Action Thing", 10, "Action") };
        var candidates = new[] { Candidate("100", "Another Action Show", ["Action"]) };

        var picks = SeasonalSuggestionCalculator.Compute(candidates, history, new SuggestionOptions(Limit: 10));

        picks.Should().HaveCount(1, "it is still a legitimate pick through the taste vector");
        picks[0].Reasons[0].Kind.Should().Be("popular", "\"Action\" is too generic to cite a title");
        picks[0].Reasons[0].Because.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Action", 0.35)]
    [InlineData("Drama", 0.35)]
    [InlineData("Cyberpunk", 1.0)]
    [InlineData("Psychological", 1.0)]
    [InlineData("School", 0.6)]
    public void WeighsTagsBySpecificity(string tag, double expected) =>
        SeasonalSuggestionCalculator.TagWeight(tag).Should().Be(expected);
}
