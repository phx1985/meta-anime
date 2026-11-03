using FluentAssertions;
using MetaAnime.Application.Utilities;

namespace MetaAnime.UnitTests.Services;

public class TitleNormalizerTests
{
    [Theory]
    [InlineData("Fate/stay night: Unlimited Blade Works", "fate stay night unlimited blade works")]
    [InlineData("The Melancholy of Haruhi Suzumiya", "melancholy of haruhi suzumiya")]
    [InlineData("A Silent Voice", "silent voice")]
    [InlineData("Re:Zero kara Hajimeru Isekai Seikatsu", "re zero kara hajimeru isekai seikatsu")]
    [InlineData("Mega Man X", "mega man x")] // single-letter numerals are never translated
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Normalize_AppliesCaseFoldPunctuationArticlesAndNfkc(string? input, string expected)
        => TitleNormalizer.Normalize(input).Should().Be(expected);

    [Theory]
    [InlineData("Final Fantasy VII", "final fantasy 7")]
    [InlineData("Gundam Wing: Endless Waltz II", "gundam wing endless waltz 2")]
    [InlineData("Title IV", "title 4")]
    public void Normalize_TranslatesMultiCharacterRomanNumerals(string input, string expected)
        => TitleNormalizer.Normalize(input).Should().Be(expected);

    [Theory]
    [InlineData("Attack on Titan Season 2", "attack on titan", 2)]
    [InlineData("Shingeki no Kyojin 2nd Season", "shingeki no kyojin", 2)]
    [InlineData("One Piece Season 2", "one piece", 2)]
    [InlineData("Fate/stay night II", "fate stay night", 2)]
    [InlineData("Demon Slayer", "demon slayer", null)]
    [InlineData("86 Eighty-Six", "86 eighty six", null)] // trailing 2+ digit numbers are not markers
    [InlineData("Title 1", "title", null)] // season 1 ≡ unmarked
    public void NormalizeWithSeason_StripsMarkerAndExtractsIt(string input, string expectedBase, int? expectedSeason)
    {
        var (baseTitle, season) = TitleNormalizer.NormalizeWithSeason(input);

        baseTitle.Should().Be(expectedBase);
        season.Should().Be(expectedSeason);
    }

    [Fact]
    public void ComputeWordLevelFuzzySimilarity_HandlesSingleCharacterTypos()
    {
        var similarity = TitleSimilarity.ComputeWordLevelFuzzySimilarity("Dragon Ball Z", "dargon ball");

        similarity.Should().BeGreaterThan(0.8);
    }

    [Fact]
    public void ComputeWordLevelFuzzySimilarity_UnrelatedTitlesScoreLow()
    {
        var similarity = TitleSimilarity.ComputeWordLevelFuzzySimilarity("Cowboy Bebop", "dargon ball");

        similarity.Should().BeLessThan(0.5);
    }

    [Fact]
    public void ComputeWordLevelFuzzySimilarity_ShortCircuitsOnLengthRatio()
    {
        // "x" can never reach the 0.2 length ratio against a long title.
        var similarity = TitleSimilarity.ComputeWordLevelFuzzySimilarity(
            "the melancholy of haruhi suzumiya", "x");

        similarity.Should().Be(0.0);
    }
}
