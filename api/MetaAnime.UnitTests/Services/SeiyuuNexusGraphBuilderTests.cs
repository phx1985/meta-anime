using FluentAssertions;
using MetaAnime.Application.Services;
using MetaAnime.Domain.Entities;

namespace MetaAnime.UnitTests.Services;

public class SeiyuuNexusGraphBuilderTests
{
    private static AnimeCast CastRow(int characterId, int vaPersonId, string language = "Japanese") => new()
    {
        AnimeMalId = "123",
        CharacterId = characterId,
        CharacterName = $"Char {characterId}",
        CharacterRole = "Main",
        CharacterImageUrl = $"https://img/char{characterId}.jpg",
        VaPersonId = vaPersonId,
        VaName = $"VA {vaPersonId}",
        VaImageUrl = $"https://img/va{vaPersonId}.jpg",
        Language = language,
        SyncedAt = DateTime.UtcNow,
    };

    private static readonly SeiyuuNexusGraphBuilder.CrossLibraryInfo NoCross =
        new(new Dictionary<int, List<SeiyuuNexusGraphBuilder.CrossAppearance>>(),
            new Dictionary<string, SeiyuuNexusGraphBuilder.CrossAnimeInfo>());

    [Fact]
    public void Build_ProducesRootCharacterAndVaNodes_WithTypedEdges()
    {
        var entries = new List<AnimeCast> { CastRow(10, 20), CastRow(11, 20) };
        var (nodes, edges) = SeiyuuNexusGraphBuilder.Build("123", "Toradora!", entries, NoCross);

        // root anime + 2 characters + 1 shared VA
        nodes.Should().HaveCount(4);

        var root = nodes.Single(n => n.Id == "anime:123");
        root.Type.Should().Be("anime");
        root.Label.Should().Be("Toradora!");
        root.IsRoot.Should().BeTrue();
        root.MalId.Should().Be("123");

        var character = nodes.Single(n => n.Id == "char:10");
        character.Type.Should().Be("character");
        character.Label.Should().Be("Char 10");
        character.Role.Should().Be("Main");
        character.IsRoot.Should().BeFalse();
        character.MalId.Should().BeNull();

        var va = nodes.Single(n => n.Id == "va:20");
        va.Type.Should().Be("va");
        va.PersonId.Should().Be(20);
        va.MalId.Should().BeNull();

        // 2 hasCharacter + 2 voices
        edges.Should().HaveCount(4);
        edges.Should().Contain(e => e.FromId == "anime:123" && e.ToId == "char:10" && e.Relation == "hasCharacter" && e.Language == null);
        edges.Should().Contain(e => e.FromId == "va:20" && e.ToId == "char:10" && e.Relation == "voices" && e.Language == "Japanese");
        edges.Should().Contain(e => e.FromId == "va:20" && e.ToId == "char:11" && e.Relation == "voices" && e.Language == "Japanese");
    }

    [Fact]
    public void Build_FillsRootImageUrl_WhenProvided()
    {
        var entries = new List<AnimeCast> { CastRow(10, 20) };
        var (nodes, _) = SeiyuuNexusGraphBuilder.Build("123", "Toradora!", entries, NoCross, "https://img/root.jpg");

        nodes.Single(n => n.Id == "anime:123").ImageUrl.Should().Be("https://img/root.jpg");
    }

    [Fact]
    public void Build_DeduplicatesCharacterAndVaNodes_ButKeepsEveryVoicesEdge()
    {
        var entries = new List<AnimeCast>
        {
            CastRow(10, 20, "Japanese"),
            CastRow(10, 21, "English"), // same character, different VA/language
        };
        var (nodes, edges) = SeiyuuNexusGraphBuilder.Build("123", "Toradora!", entries, NoCross);
        nodes.Count(n => n.Id == "char:10").Should().Be(1);
        nodes.Count(n => n.Id == "va:20").Should().Be(1);
        nodes.Count(n => n.Id == "va:21").Should().Be(1);
        edges.Count(e => e.Relation == "voices").Should().Be(2);
        edges.Count(e => e.Relation == "hasCharacter").Should().Be(1); // deduped per character
    }

    [Fact]
    public void Build_AddsCrossLibraryAppearsInEdges_WithBatchTitlesAndPosters()
    {
        var entries = new List<AnimeCast> { CastRow(10, 20), CastRow(11, 21) };
        var cross = new SeiyuuNexusGraphBuilder.CrossLibraryInfo(
            new Dictionary<int, List<SeiyuuNexusGraphBuilder.CrossAppearance>>
            {
                [20] = [new("456", "Japanese"), new("789", "English, Japanese")],
                [21] = [new("456", "Japanese")], // shared cross anime — node added once
            },
            new Dictionary<string, SeiyuuNexusGraphBuilder.CrossAnimeInfo>
            {
                ["456"] = new("Cross Title", "https://img/456.jpg"),
                ["789"] = new("Second Title", null), // resolved title, no poster available
            });

        var (nodes, edges) = SeiyuuNexusGraphBuilder.Build("123", "Toradora!", entries, cross);

        var cross456 = nodes.Single(n => n.Id == "anime:456");
        cross456.Type.Should().Be("anime");
        cross456.Label.Should().Be("Cross Title");
        cross456.ImageUrl.Should().Be("https://img/456.jpg");
        cross456.IsRoot.Should().BeFalse();
        cross456.MalId.Should().Be("456");

        // Resolved title without a poster still renders (frontend monogram falls back on the title)
        var cross789 = nodes.Single(n => n.Id == "anime:789");
        cross789.Label.Should().Be("Second Title");
        cross789.ImageUrl.Should().BeNull();

        nodes.Count(n => n.Id == "anime:456").Should().Be(1);

        // appearsIn edges carry the VA's dub language(s) in that anime as the edge label
        edges.Should().Contain(e => e.FromId == "va:20" && e.ToId == "anime:456" && e.Relation == "appearsIn" && e.Language == "Japanese");
        edges.Should().Contain(e => e.FromId == "va:20" && e.ToId == "anime:789" && e.Relation == "appearsIn" && e.Language == "English, Japanese");
        edges.Should().Contain(e => e.FromId == "va:21" && e.ToId == "anime:456" && e.Relation == "appearsIn" && e.Language == "Japanese");
    }

    [Fact]
    public void Build_DropsCrossAnimeWithoutResolvedTitle_NeverRendersRawId()
    {
        var entries = new List<AnimeCast> { CastRow(10, 20) };
        var cross = new SeiyuuNexusGraphBuilder.CrossLibraryInfo(
            new Dictionary<int, List<SeiyuuNexusGraphBuilder.CrossAppearance>>
            {
                [20] = [new("456", null), new("789", "Japanese")], // "789" has no resolved title
            },
            new Dictionary<string, SeiyuuNexusGraphBuilder.CrossAnimeInfo>
            {
                ["456"] = new("Cross Title", "https://img/456.jpg"),
            });

        var (nodes, edges) = SeiyuuNexusGraphBuilder.Build("123", "Toradora!", entries, cross);

        // Unresolvable anime is dropped entirely — no node with the raw id as label, no dangling edge
        nodes.Should().NotContain(n => n.Id == "anime:789");
        nodes.Should().NotContain(n => n.Label == "789");
        edges.Should().NotContain(e => e.ToId == "anime:789");

        edges.Should().Contain(e => e.FromId == "va:20" && e.ToId == "anime:456" && e.Relation == "appearsIn" && e.Language == null);
    }
}
