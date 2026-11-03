namespace MetaAnime.Core.Settings;

/// <summary>AniList GraphQL upstream for cast/VA-profile data.</summary>
public record AniListSettings
{
    public const string SectionName = "AniList";

    /// <summary>Public endpoint; self-hostable proxies override it via config/env <c>AniList__ApiUrl</c>.</summary>
    public const string DefaultApiUrl = "https://graphql.anilist.co";

    public string ApiUrl { get; init; } = DefaultApiUrl;
}
