using System.Globalization;
using System.Text.Json;
using MetaAnime.Core;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Domain.Entities;
using MetaAnime.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace MetaAnime.Application.Services;

/// <summary>
/// Reads the Plex server's watch history and records <see cref="ActivityType.EpisodeWatched"/>
/// entries. Plex has no webhook here, so this is a delta poll over the server's history feed.
/// <para>
/// Every poll re-reads the newest <see cref="FetchLimit"/> history rows and relies on
/// <c>ExternalEventKey</c> ("plex-hist-{historyKey}") for idempotency — no "last polled at"
/// watermark to drift out of sync after a restart or a clock change.
/// </para>
/// </summary>
public sealed class PlexWatchHistoryPoller(
    IHttpClientFactory httpClientFactory,
    IPropertyBagRepository propertyBagRepository,
    ICacheEntryRepository cacheEntryRepository,
    IKometaAnimeMappingRepository kometaRepository,
    ActivityLogService activityLog,
    ILogger<PlexWatchHistoryPoller> logger)
{
    public const string HttpClientName = "Plex";

    private const int FetchLimit = 100;

    /// <summary>Safety cap: 10 pages = 1 000 history rows per poll.</summary>
    private const int MaxPages = 10;

    /// <summary>History older than this is ignored: the hub is a recent-activity feed.</summary>
    private static readonly TimeSpan Lookback = TimeSpan.FromDays(365);

    /// <summary>
    /// Polls the configured Plex server. Never throws: an unreachable server must not fail the
    /// download-stats job that called it.
    /// </summary>
    public async Task<int> PollAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var host = (await propertyBagRepository.GetByKeyAsync(SettingsKeys.PlexHost))?.Value;
            var token = (await propertyBagRepository.GetByKeyAsync(SettingsKeys.PlexToken))?.Value;
            var sectionsValue = (await propertyBagRepository.GetByKeyAsync(SettingsKeys.PlexAnimeSections))?.Value;

            // History is server-wide: without this the feed fills with the kids' cartoons and
            // everything else that shares the server. Only anime libraries count here.
            var animeSections = ParseSections(sectionsValue);

            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(token))
            {
                logger.LogDebug("Plex watch history poll skipped: Plex host/token not configured.");
                return 0;
            }

            var entries = await FetchHistoryAsync(host.TrimEnd('/'), token, cancellationToken);
            var kometa = await LoadKometaIndexAsync(cancellationToken);
            var library = await LoadPlexIndexAsync();
            var since = DateTimeOffset.UtcNow - Lookback;
            var logged = 0;
            var skipped = 0;

            foreach (var entry in entries)
            {
                if (!string.Equals(entry.Type, "episode", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.IsNullOrWhiteSpace(entry.GrandparentTitle)) continue;

                // Configured sections win; a row that does not report a section is kept so a
                // Plex version that omits the field cannot empty the whole feed.
                if (animeSections.Count > 0 && entry.LibrarySectionId is { } sectionId &&
                    !animeSections.Contains(sectionId))
                {
                    continue;
                }
                if (entry.ViewedAt is { } viewedAt && viewedAt < since) continue;

                var key = entry.HistoryKey ?? entry.RatingKey;
                if (string.IsNullOrWhiteSpace(key)) continue;

                // This is an anime app: a watch only counts when Kometa maps the item's TVDB /
                // IMDB id to an AniDB entry that itself has a MAL id. Everything else on the
                // Plex server (Miraculous, films, kids' shows) is dropped here.
                // History rows often carry no Guid[]: take the ids (or the title) from the
                // Plex library cache instead, which was fetched with includeGuids=1.
                var tvdbId = entry.TvdbId;
                var imdbId = entry.ImdbId;
                if (tvdbId is null && imdbId is null)
                {
                    var cached = library.ById(entry.GrandparentRatingKey) ?? library.ByTitle(entry.GrandparentTitle!);
                    if (cached is { } ids)
                    {
                        tvdbId = ids.TvdbId;
                        imdbId = ids.ImdbId;
                    }
                }

                var malId = kometa.Resolve(tvdbId, imdbId);
                if (malId is null)
                {
                    skipped++;
                    continue;
                }

                var season = entry.ParentIndex ?? 0;
                var episode = entry.Index ?? 0;
                var subtitle = season > 0 && episode > 0
                    ? $"S{season:D2}E{episode:D2} watched on Plex"
                    : "Watched on Plex";

                await activityLog.LogAsync(
                    ActivityType.EpisodeWatched,
                    entry.GrandparentTitle!,
                    subtitle,
                    malId: malId,
                    tvdbId: tvdbId,
                    provider: "plex",
                    severity: "info",
                    externalEventKey: $"plex-hist-{key}",
                    // Timestamp the row when it was watched, not when the poll noticed it —
                    // otherwise every scrobble counts as "today" and the range filters lie.
                    occurredAt: entry.ViewedAt,
                    metadata: new
                    {
                        season,
                        episode,
                        ratingKey = entry.RatingKey,
                        grandparentRatingKey = entry.GrandparentRatingKey,
                        viewedAt = entry.ViewedAt?.ToUnixTimeSeconds(),
                    },
                    cancellationToken: cancellationToken);

                logged++;
            }

            if (logged > 0)
                logger.LogDebug("Plex watch history polled: {Count} watched episode(s) seen", logged);
            if (skipped > 0)
                logger.LogDebug("Plex watch history: {Count} item(s) skipped as non-anime", skipped);

            return logged;
        }
        catch (Exception ex)
        {
            // Graceful degradation: the settings page still says whether Plex is reachable.
            logger.LogWarning(ex, "Plex watch history poll failed; continuing without scrobbles");
            return 0;
        }
    }

    /// <summary>
    /// Kometa's match table, indexed for the three ids Plex gives us. Kometa is the single source
    /// of truth for "is this an anime": a TVDB/IMDB id that never reaches a MAL id is not anime.
    /// </summary>
    private sealed record KometaIndex(
        Dictionary<string, string> MalByAniDb,
        Dictionary<string, string> AniDbByTvdb,
        Dictionary<string, string> AniDbByImdb)
    {
        public int? Resolve(int? tvdbId, string? imdbId)
        {
            string? aniDb = null;

            if (tvdbId is not null) AniDbByTvdb.TryGetValue(tvdbId.Value.ToString(), out aniDb);
            if (aniDb is null && !string.IsNullOrWhiteSpace(imdbId)) AniDbByImdb.TryGetValue(imdbId, out aniDb);
            if (aniDb is null) return null;

            if (MalByAniDb.TryGetValue(aniDb, out var mal) && int.TryParse(mal, out var parsed)) return parsed;
            return null;
        }
    }

    /// <summary>Plex library cache: rating key / title → the external ids Plex knows.</summary>
    private sealed record PlexLibraryIndex(
        Dictionary<string, (int? TvdbId, string? ImdbId)> ByRatingKey,
        Dictionary<string, (int? TvdbId, string? ImdbId)> ByTitleIndex)
    {
        public (int? TvdbId, string? ImdbId)? ById(string? ratingKey)
            => ratingKey is not null && ByRatingKey.TryGetValue(ratingKey, out var value) ? value : null;

        public (int? TvdbId, string? ImdbId)? ByTitle(string title)
            => title is not null && ByTitleIndex.TryGetValue(title.Trim().ToLowerInvariant(), out var value) ? value : null;
    }

    private async Task<PlexLibraryIndex> LoadPlexIndexAsync()
    {
        var byId = new Dictionary<string, (int? TvdbId, string? ImdbId)>(StringComparer.Ordinal);
        var byTitle = new Dictionary<string, (int? TvdbId, string? ImdbId)>(StringComparer.Ordinal);

        try
        {
            foreach (var entry in await cacheEntryRepository.GetAsync(AnimeSource.Plex))
            {
                try
                {
                    using var document = JsonDocument.Parse(entry.RawData);
                    var root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) continue;

                    var id = Property(root, "id", "Id") ?? entry.SourceId;
                    var title = Property(root, "title", "Title");

                    int? tvdb = null;
                    string? imdb = null;
                    if (TryGet(root, out var guids, "externalGuilds", "ExternalGuilds") && guids.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var guid in guids.EnumerateArray())
                        {
                            var raw = guid.GetString();
                            if (string.IsNullOrWhiteSpace(raw)) continue;

                            if (raw.StartsWith("tvdb://", StringComparison.OrdinalIgnoreCase) &&
                                int.TryParse(raw["tvdb://".Length..], out var parsedTvdb))
                            {
                                tvdb = parsedTvdb;
                            }
                            else if (raw.StartsWith("imdb://", StringComparison.OrdinalIgnoreCase))
                            {
                                imdb = raw["imdb://".Length..];
                            }
                        }
                    }

                    if (tvdb is null && imdb is null) continue;

                    if (!string.IsNullOrWhiteSpace(id)) byId[id] = (tvdb, imdb);
                    if (!string.IsNullOrWhiteSpace(title)) byTitle[title.Trim().ToLowerInvariant()] = (tvdb, imdb);
                }
                catch (JsonException) { /* unreadable cache row */ }
            }
        }
        catch { /* cache unavailable → rely on the history guids */ }

        return new PlexLibraryIndex(byId, byTitle);
    }

    private static bool TryGet(JsonElement root, out JsonElement value, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.TryGetProperty(name, out value)) return true;
        }

        value = default;
        return false;
    }

    private static string? Property(JsonElement root, string lower, string pascal)
        => TryGet(root, out var value, lower, pascal) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    // Platform names as Kometa stores them.
    private const string MalPlatform = "mal_id";
    private const string TvdbPlatform = "tvdb_id";
    private const string ImdbPlatform = "imdb_id";

    private async Task<KometaIndex> LoadKometaIndexAsync(CancellationToken cancellationToken)
    {
        try
        {
            var rows = await kometaRepository.GetAllAsync();
            var malByAniDb = new Dictionary<string, string>(StringComparer.Ordinal);
            var aniDbByTvdb = new Dictionary<string, string>(StringComparer.Ordinal);
            var aniDbByImdb = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var row in rows)
            {
                switch (row.PlatformName)
                {
                    case MalPlatform:
                        malByAniDb[row.AniDbId] = row.PlatformId;
                        break;
                    case TvdbPlatform:
                        aniDbByTvdb[row.PlatformId] = row.AniDbId;
                        break;
                    case ImdbPlatform:
                        aniDbByImdb[row.PlatformId] = row.AniDbId;
                        break;
                }
            }

            return new KometaIndex(malByAniDb, aniDbByTvdb, aniDbByImdb);
        }
        catch (Exception ex)
        {
            // Without the table nothing can be verified as anime; keep the feed empty rather
            // than logging every Plex item on the server.
            logger.LogWarning(ex, "Kometa match table unavailable; Plex watch history skipped this cycle.");
            return new KometaIndex([], [], []);
        }
    }

    /// <summary>"1,3" — the comma-separated library ids stored in Plex:AnimeSections.</summary>
    private static HashSet<int> ParseSections(string? value)
    {
        var sections = new HashSet<int>();
        if (string.IsNullOrWhiteSpace(value)) return sections;

        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(part, out var id)) sections.Add(id);
        }

        return sections;
    }

    private async Task<IReadOnlyList<PlexHistoryEntry>> FetchHistoryAsync(
        string host, string token, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        var results = new List<PlexHistoryEntry>();
        var since = DateTimeOffset.UtcNow - Lookback;

        // Plex pages with X-Plex-Container-Start. One page (100 rows) covered barely a month of
        // watching; the tile then read "82 watched" for a whole year. Walk pages until the
        // lookback edge or the safety cap.
        for (var page = 0; page < MaxPages; page++)
        {
            var start = page * FetchLimit;
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{host}/status/sessions/history/all?sort=viewedAt:desc&limit={FetchLimit}&X-Plex-Container-Start={start}");

            // The token travels as a header, never in the URL, so it cannot end up in a log line.
            request.Headers.Add("X-Plex-Token", token);
            request.Headers.Add("Accept", "application/json");

            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                // Visible at the default log level: an invalid token or a server that disables
                // history is exactly what makes the "Watched on Plex" tile sit at zero forever.
                logger.LogWarning(
                    "Plex history returned HTTP {StatusCode}; no scrobbles recorded this cycle.", (int)response.StatusCode);
                break;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (!document.RootElement.TryGetProperty("MediaContainer", out var container) ||
                !container.TryGetProperty("Metadata", out var metadata) || metadata.ValueKind != JsonValueKind.Array)
            {
                break;
            }

            var page_ = metadata.EnumerateArray().Select(Parse).OfType<PlexHistoryEntry>().ToList();
            if (page_.Count == 0) break;

            results.AddRange(page_);

            // Oldest row on the page already outside the lookback → nothing newer is left.
            var oldest = page_.Min(e => e.ViewedAt);
            if (oldest is not null && oldest < since) break;
            if (page_.Count < FetchLimit) break;
        }

        return results;
    }


    private static PlexHistoryEntry? Parse(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;

        string? Text(string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        int? Number(string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt32(out var parsed)
                ? parsed
                : null;

        DateTimeOffset? viewedAt = null;
        if (element.TryGetProperty("viewedAt", out var viewedElement))
        {
            if (viewedElement.ValueKind == JsonValueKind.Number && viewedElement.TryGetInt64(out var epoch))
                viewedAt = DateTimeOffset.FromUnixTimeSeconds(epoch);
            else if (viewedElement.ValueKind == JsonValueKind.String &&
                     DateTimeOffset.TryParse(viewedElement.GetString(), CultureInfo.InvariantCulture,
                         DateTimeStyles.AssumeUniversal, out var parsed))
                viewedAt = parsed;
        }

        int? tvdbId = null;
        string? imdbId = null;
        if (element.TryGetProperty("Guid", out var guids) && guids.ValueKind == JsonValueKind.Array)
        {
            foreach (var guid in guids.EnumerateArray())
            {
                var raw = guid.ValueKind == JsonValueKind.Object && guid.TryGetProperty("id", out var idElement)
                    ? idElement.GetString()
                    : guid.GetString();
                if (string.IsNullOrWhiteSpace(raw)) continue;

                if (raw.StartsWith("tvdb://", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(raw["tvdb://".Length..], out var parsedTvdb))
                {
                    tvdbId = parsedTvdb;
                }
                else if (raw.StartsWith("imdb://", StringComparison.OrdinalIgnoreCase))
                {
                    imdbId = raw["imdb://".Length..];
                }
            }
        }

        return new PlexHistoryEntry(
            Text("type"),
            Text("historyKey"),
            Text("ratingKey"),
            Text("grandparentRatingKey"),
            Text("grandparentTitle"),
            Number("parentIndex"),
            Number("index"),
            Number("librarySectionID"),
            tvdbId,
            imdbId,
            viewedAt);
    }

    private sealed record PlexHistoryEntry(
        string? Type,
        string? HistoryKey,
        string? RatingKey,
        string? GrandparentRatingKey,
        string? GrandparentTitle,
        int? ParentIndex,
        int? Index,
        int? LibrarySectionId,
        int? TvdbId,
        string? ImdbId,
        DateTimeOffset? ViewedAt);
}
