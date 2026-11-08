using System.Text.Json;
using System.Text.Json.Nodes;
using MetaAnime.Application.Contracts;
using MetaAnime.Application.Utilities;
using MetaAnime.Core;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Domain.Entities;
using MetaAnime.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace MetaAnime.Application.Services;

/// <summary>
/// Writes and reads Activity Hub entries. Logging is deliberately fire-and-forget from the
/// caller's point of view: a failed activity write must never fail an add or a sync.
/// </summary>
public sealed class ActivityLogService(
    IActivityLogRepository repository,
    IPropertyBagRepository propertyBag,
    ICacheEntryRepository cacheEntryRepository,
    IAnimeMappingRepository animeMappingRepository,
    ISonarrHistoryEventRepository sonarrHistoryRepository,
    ILogger<ActivityLogService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async Task LogAsync(
        ActivityType type,
        string title,
        string? subtitle = null,
        int? malId = null,
        int? tvdbId = null,
        string? posterUrl = null,
        string? provider = null,
        object? metadata = null,
        string severity = "info",
        int? sonarrSeriesId = null,
        string? externalEventKey = null,
        bool isReverted = false,
        DateTimeOffset? occurredAt = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Idempotent ingestion: a poller that re-reads the provider's last N events must not
            // duplicate them on every cycle.
            if (!string.IsNullOrWhiteSpace(externalEventKey) &&
                await repository.ExistsByExternalEventKeyAsync(externalEventKey, cancellationToken))
            {
                return;
            }

            var payload = BuildMetadata(provider, metadata);

            if (type == ActivityType.AnimeAdded &&
                await TryConsolidateAddAsync(title, malId, tvdbId, posterUrl, payload, cancellationToken))
            {
                return;
            }

            var entry = new ActivityLog
            {
                Type = type,
                Title = title,
                Subtitle = subtitle,
                MalId = malId,
                TvdbId = tvdbId,
                PosterUrl = posterUrl,
                Severity = severity,
                SonarrSeriesId = sonarrSeriesId,
                ExternalEventKey = externalEventKey,
                IsReverted = isReverted,
                MetadataJson = payload is null ? null : payload.ToJsonString(JsonOptions),
                // Providers report when the event actually happened (Plex `viewedAt`, Sonarr import
                // date); fall back to now for events we observe live.
                Timestamp = occurredAt ?? DateTimeOffset.UtcNow,
            };
            await repository.AddAsync(entry, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Activity log write failed for {Type} '{Title}'", type, title);
        }
    }

    /// <summary>How far apart two provider adds may be and still read as one event.</summary>
    private static readonly TimeSpan ConsolidationWindow = TimeSpan.FromMinutes(10);

    private static readonly Dictionary<string, string> ProviderLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sonarr"] = "Sonarr",
        ["mal"] = "MyAnimeList",
        ["plex"] = "Plex",
        ["kometa"] = "Kometa",
    };

    /// <summary>Normalizes the metadata blob so <c>providers[]</c> always exists when known.</summary>
    private static JsonObject? BuildMetadata(string? provider, object? metadata)
    {
        JsonObject? payload = metadata is null
            ? null
            : JsonSerializer.SerializeToNode(metadata, JsonOptions) as JsonObject;

        if (string.IsNullOrWhiteSpace(provider)) return payload;

        payload ??= new JsonObject();
        var slug = provider.Trim().ToLowerInvariant();
        var providers = payload["providers"] as JsonArray ?? new JsonArray();
        if (!providers.Any(p => string.Equals(p?.GetValue<string>(), slug, StringComparison.OrdinalIgnoreCase)))
            providers.Add(slug);
        payload["providers"] = providers;
        return payload;
    }

    /// <summary>
    /// Folds "added to Sonarr" and "added to MyAnimeList" for the same anime into one card.
    /// Returns true when an existing row absorbed the event (nothing new is written).
    /// </summary>
    private async Task<bool> TryConsolidateAddAsync(
        string title, int? malId, int? tvdbId, string? posterUrl, JsonObject? payload, CancellationToken cancellationToken)
    {
        var candidate = await repository.FindRecentCandidateAsync(
            ActivityType.AnimeAdded, malId, tvdbId, title, ConsolidationWindow, cancellationToken);

        if (candidate is null) return false;

        var merged = (candidate.MetadataJson is null ? null : JsonNode.Parse(candidate.MetadataJson) as JsonObject)
                     ?? new JsonObject();
        var providers = merged["providers"] as JsonArray ?? new JsonArray();

        // Legacy rows predate the providers array — recover the first provider from the subtitle.
        if (providers.Count == 0)
        {
            if ((candidate.Subtitle ?? string.Empty).Contains("Sonarr", StringComparison.OrdinalIgnoreCase)) providers.Add("sonarr");
            if ((candidate.Subtitle ?? string.Empty).Contains("MyAnimeList", StringComparison.OrdinalIgnoreCase)) providers.Add("mal");
        }

        foreach (var slug in payload?["providers"] as JsonArray ?? new JsonArray())
        {
            var value = slug?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(value)) continue;
            if (!providers.Any(p => string.Equals(p?.GetValue<string>(), value, StringComparison.OrdinalIgnoreCase)))
                providers.Add(value);
        }

        merged["providers"] = providers;
        candidate.MetadataJson = merged.ToJsonString(JsonOptions);
        candidate.Subtitle = $"Added to {string.Join(" & ", providers.Select(p => ProviderLabels.GetValueOrDefault(p!.GetValue<string>()!, p!.GetValue<string>()!)))}";
        candidate.MalId ??= malId;
        candidate.TvdbId ??= tvdbId;
        candidate.PosterUrl ??= posterUrl;

        await repository.UpdateAsync(candidate, cancellationToken);
        return true;
    }

    /// <summary>Flags the matching "added" card as reverted so the net delta drops back to 0.</summary>
    public async Task RevertAsync(int? malId, int? tvdbId, CancellationToken cancellationToken = default)
    {
        try
        {
            await repository.RevertAddedAsync(malId, tvdbId, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Activity revert failed for mal {MalId} / tvdb {TvdbId}", malId, tvdbId);
        }
    }

    /// <summary>
    /// Multi-series trend for the hub chart: episodes ingested, anime added, Plex watches and
    /// sync runs per bucket of the selected range. All four come from the activity log itself,
    /// so every line lines up with the feed and the KPI tiles.
    /// </summary>
    public async Task<ActivityTrendResponse> GetTrendAsync(
        ActivityStatsPeriod period, int tzOffsetMinutes, int? year = null, CancellationToken cancellationToken = default)
    {
        var buckets = ActivityStatsCalculator.TrendBuckets(period, DateTime.UtcNow, tzOffsetMinutes, year);
        var points = buckets
            .Select(b => new MutablePoint(b.Label))
            .ToList();

        if (buckets.Count == 0)
            return new ActivityTrendResponse(period.ToString(), []);

        var fromUtc = DateTime.SpecifyKind(buckets[0].StartLocal.AddMinutes(tzOffsetMinutes), DateTimeKind.Utc);
        var since = new DateTimeOffset(fromUtc, TimeSpan.Zero);

        var rows = await repository.GetSinceAsync(since, cancellationToken);
        var imports = await sonarrHistoryRepository.GetSinceAsync(fromUtc, cancellationToken) ?? [];

        foreach (var row in rows)
        {
            var local = row.Timestamp.UtcDateTime.AddMinutes(-tzOffsetMinutes);

            // Buckets are contiguous, so a linear scan of ≤ 31 entries per row is fine.
            for (var i = 0; i < buckets.Count; i++)
            {
                if (local < buckets[i].StartLocal || local >= buckets[i].EndLocal) continue;

                switch (row.Type)
                {
                    case ActivityType.EpisodeDownloaded: points[i].Ingested++; break;
                    case ActivityType.AnimeAdded: points[i].Added++; break;
                    case ActivityType.EpisodeWatched: points[i].Watched++; break;
                    case ActivityType.SyncCompleted: points[i].Syncs++; break;
                }

                break;
            }
        }

        // Imports are plotted from the history mirror, exactly like the "Episodes Ingested" tile.
        foreach (var import in imports)
        {
            var local = import.OccurredAt.AddMinutes(-tzOffsetMinutes);

            for (var i = 0; i < buckets.Count; i++)
            {
                if (local < buckets[i].StartLocal || local >= buckets[i].EndLocal) continue;
                points[i].Ingested++;
                break;
            }
        }

        return new ActivityTrendResponse(
            period.ToString(),
            points.Select(p => new ActivityTrendPoint(p.Label, p.Ingested, p.Added, p.Watched, p.Syncs)).ToList());
    }

    private async Task<int> CountImportsAsync(DateTimeOffset from, CancellationToken cancellationToken)
    {
        try
        {
            var events = await sonarrHistoryRepository.GetSinceAsync(from.UtcDateTime, cancellationToken);
            return events?.Count ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    private sealed class MutablePoint(string label)
    {
        public string Label { get; } = label;
        public int Ingested { get; set; }
        public int Added { get; set; }
        public int Watched { get; set; }
        public int Syncs { get; set; }
    }

    /// <summary>Net KPI counters for the hub, over a rolling window of <paramref name="days"/>.</summary>
    public async Task<ActivitySummaryResponse> GetSummaryAsync(int days, CancellationToken cancellationToken = default)
    {
        days = Math.Clamp(days, 1, 3650);
        return await BuildSummaryAsync(DateTimeOffset.UtcNow.AddDays(-days), days, cancellationToken);
    }

    /// <summary>
    /// Net KPI counters for the hub over a CALENDAR window — the same
    /// <see cref="ActivityStatsCalculator.PeriodStartUtc"/> boundary the trend chart and the
    /// download tiles use. The range pills are global, so a rolling window here would make the
    /// tiles disagree with the chart ("Week" = last 7 days vs. the current Mon→Sun week).
    /// </summary>
    public async Task<ActivitySummaryResponse> GetSummaryAsync(
        ActivityStatsPeriod period, int tzOffsetMinutes, int? year = null, CancellationToken cancellationToken = default)
    {
        var startUtc = ActivityStatsCalculator.PeriodStartUtc(period, DateTime.UtcNow, tzOffsetMinutes, year);
        var from = new DateTimeOffset(startUtc, TimeSpan.Zero);

        var days = period switch
        {
            ActivityStatsPeriod.Today => 1,
            ActivityStatsPeriod.ThisWeek => 7,
            ActivityStatsPeriod.ThisMonth => 30,
            _ => 365,
        };

        return await BuildSummaryAsync(from, days, cancellationToken);
    }

    private async Task<ActivitySummaryResponse> BuildSummaryAsync(
        DateTimeOffset from, int days, CancellationToken cancellationToken)
    {
        var counts = await repository.CountByTypeAsync(from, cancellationToken);

        int Count(ActivityType type) => counts.TryGetValue(type, out var value) ? value : 0;

        var added = Count(ActivityType.AnimeAdded);
        var removed = Count(ActivityType.AnimeRemoved);

        return new ActivitySummaryResponse(
            added,
            removed,
            added - removed,
            // Imports come from the mirrored Sonarr history, not from the activity log: the log
            // only keeps the most recent events, so counting those showed "15 episodes" for a
            // whole year of imports.
            await CountImportsAsync(from, cancellationToken),
            Count(ActivityType.EpisodeWatched),
            Count(ActivityType.SeasonCompleted),
            Count(ActivityType.SyncCompleted),
            days);
    }

    /// <param name="from">
    /// Window start of the selected range — the timeline follows the same pills as the tiles and
    /// the chart; <c>null</c> means "no range filter".
    /// </param>
    public async Task<ActivityFeedResponse> GetFeedAsync(
        IReadOnlyCollection<ActivityType>? types, int page, int pageSize, string? search = null,
        DateTimeOffset? from = null, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var total = await repository.CountAsync(types, from, cancellationToken);
        var rows = await repository.GetRecentAsync(types, (page - 1) * pageSize, pageSize, search, from, cancellationToken);

        var links = await LoadProviderBasesAsync(cancellationToken);
        var index = await LoadLinkIndexAsync(cancellationToken);

        var items = rows
            .Select(a =>
            {
                // Rows written by the Sonarr/Plex producers carry no MAL id and often no poster
                // (the caches have both) — resolve them at read time so the card is complete
                // and the detail panel can open.
                var malId = a.MalId is > 0
                    ? a.MalId
                    : index.ResolveMalId(a.Title, a.SonarrSeriesId, a.TvdbId);

                var poster = !string.IsNullOrWhiteSpace(a.PosterUrl)
                    ? a.PosterUrl
                    : index.ResolvePoster(a.TvdbId, a.SonarrSeriesId, malId, a.Title);

                var animeId = index.ResolveAnimeId(malId, a.SonarrSeriesId);

                return new ActivityLogDto(
                    a.Id, a.Type.ToString(), a.Title, a.Subtitle, malId, a.TvdbId,
                    a.SonarrSeriesId, poster, a.Severity, a.IsReverted,
                    BuildProviderLinks(a, links, index, malId), a.MetadataJson, a.Timestamp, animeId);
            })
            // Last line of defence: this hub is anime-only. A media event that cannot be tied to
            // a MAL entry (Miraculous and friends) is not shown, whatever produced it.
            .Where(d => d.AnimeId is not null || d.MalId is not null || !IsMediaEvent(d.Type))
            .ToList();

        return new ActivityFeedResponse(items, total, page, pageSize);
    }

    private sealed record ProviderBases(string? Sonarr, string? Plex);

    /// <summary>Base URLs are read once per feed request, not per row.</summary>
    private async Task<ProviderBases> LoadProviderBasesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var sonarr = await propertyBag.GetByKeyAsync(SettingsKeys.SonarrRemoteUrl)
                         ?? await propertyBag.GetByKeyAsync(SettingsKeys.SonarrHost);
            var plex = await propertyBag.GetByKeyAsync(SettingsKeys.PlexHost);

            return new ProviderBases(NormaliseBase(sonarr?.Value), NormaliseBase(plex?.Value));
        }
        catch
        {
            return new ProviderBases(null, null);
        }
    }

    /// <summary>Events that describe a show; sync runs are not tied to any anime.</summary>
    private static bool IsMediaEvent(string type)
        => type is nameof(ActivityType.AnimeAdded) or nameof(ActivityType.AnimeRemoved)
            or nameof(ActivityType.EpisodeDownloaded) or nameof(ActivityType.EpisodeWatched)
            or nameof(ActivityType.SeasonCompleted);

    /// <summary>Loose title key: case, punctuation and spacing must not break the match.</summary>
    private static string NormaliseTitle(string title)
        => new(title.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    /// <summary>Accepts "host:8989", "http://host:8989/" and yields "http://host:8989".</summary>
    private static string? NormaliseBase(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim().TrimEnd('/');
        if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = "http://" + trimmed;
        }
        return trimmed;
    }

    /// <summary>
    /// Deep links shown on each tile. Unconfigured providers are omitted entirely, so the UI can
    /// never render a broken "http:///series/…" button.
    /// </summary>
    private sealed record LinkIndex(
        Dictionary<string, string> SlugByTvdbId,
        Dictionary<string, string> SlugBySeriesId,
        Dictionary<string, string> SlugByTitle,
        Dictionary<string, string> MalIdByTitle,
        Dictionary<string, string> MalIdBySeriesId,
        Dictionary<string, string> SeriesIdByTvdbId,
        Dictionary<string, Guid> MalAnimeIds,
        Dictionary<string, Guid> SonarrAnimeIds,
        Dictionary<string, string> PlexKeyByTitle,
        string? PlexMachineId,
        Dictionary<string, string> PosterByTvdbId,
        Dictionary<string, string> PosterBySeriesId,
        Dictionary<string, string> PosterByTitle,
        Dictionary<string, string> MalPosterByTitle)
    {
        /// <summary>Library entry for a row: MAL id first, then the Sonarr series id.</summary>
        public Guid? ResolveAnimeId(int? malId, int? seriesId)
        {
            if (malId is > 0 && MalAnimeIds.TryGetValue(malId.Value.ToString(), out var byMal)) return byMal;
            if (seriesId is not null && SonarrAnimeIds.TryGetValue(seriesId.Value.ToString(), out var bySeries)) return bySeries;
            return null;
        }

        /// <summary>
        /// MAL id for a row written without one: the Sonarr series id (mapping table) is far more
        /// reliable than the title, which drifts with season names and punctuation.
        /// </summary>
        public int? ResolveMalId(string title, int? seriesId = null, int? tvdbId = null)
        {
            // 1) the row's Sonarr series id, 2) via the TVDB id the Sonarr cache knows,
            // 3) the MAL title index — each step is weaker than the previous one.
            if (seriesId is null && tvdbId is > 0 &&
                SeriesIdByTvdbId.TryGetValue(tvdbId.Value.ToString(), out var seriesFromTvdb) &&
                int.TryParse(seriesFromTvdb, out var parsedFromTvdb))
            {
                seriesId = parsedFromTvdb;
            }

            if (seriesId is not null && MalIdBySeriesId.TryGetValue(seriesId.Value.ToString(), out var bySeries) &&
                int.TryParse(bySeries, out var parsedBySeries))
            {
                return parsedBySeries;
            }

            return MalIdByTitle.TryGetValue(NormaliseTitle(title), out var id) && int.TryParse(id, out var parsed)
                ? parsed
                : null;
        }

        /// <summary>
        /// First usable poster for a row: TVDB → Sonarr series id → MAL id → title. Sonarr wins
        /// because its `remotePoster` is a TMDB image already sized for cards.
        /// </summary>
        public string? ResolvePoster(int? tvdbId, int? seriesId, int? malId, string title)
        {
            var key = NormaliseTitle(title);

            if (tvdbId is > 0 && PosterByTvdbId.TryGetValue(tvdbId.Value.ToString(), out var byTvdb)) return byTvdb;
            if (seriesId is not null && PosterBySeriesId.TryGetValue(seriesId.Value.ToString(), out var bySeries)) return bySeries;
            if (malId is > 0 && MalIdByTitle.TryGetValue(key, out _) && MalPosterByTitle.TryGetValue(key, out var byMal)) return byMal;
            if (PosterByTitle.TryGetValue(key, out var byTitle)) return byTitle;
            if (MalPosterByTitle.TryGetValue(key, out var byMalTitle)) return byMalTitle;
            return null;
        }
    }

    /// <summary>Sonarr stores `remotePoster` (TMDB) and/or an images[] list with coverType "poster".</summary>
    private static string? ExtractSonarrPoster(JsonElement root)
    {
        if (root.TryGetProperty("remotePoster", out var remote) &&
            remote.ValueKind == JsonValueKind.String &&
            remote.GetString() is { Length: > 0 } direct)
        {
            return direct;
        }

        if (root.TryGetProperty("images", out var images) && images.ValueKind == JsonValueKind.Array)
        {
            foreach (var image in images.EnumerateArray())
            {
                var coverType = image.TryGetProperty("coverType", out var cover) ? cover.GetString() : null;
                if (!string.Equals(coverType, "poster", StringComparison.OrdinalIgnoreCase)) continue;

                var url = image.TryGetProperty("remoteUrl", out var remoteUrl) ? remoteUrl.GetString() : null;
                if (!string.IsNullOrWhiteSpace(url)) return url;
            }
        }

        return null;
    }

    /// <summary>Jikan/MAL shape: main_picture.large ?? main_picture.medium.</summary>
    private static string? ExtractMalPoster(JsonElement root)
    {
        if (!root.TryGetProperty("main_picture", out var picture) || picture.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var size in new[] { "large", "medium" })
        {
            if (picture.TryGetProperty(size, out var element) &&
                element.ValueKind == JsonValueKind.String &&
                element.GetString() is { Length: > 0 } url)
            {
                return url;
            }
        }

        return null;
    }

    /// <summary>Card thumbnails do not need a 1000px TMDB original — ask for the 300px variant.</summary>
    private static string? ShrinkPoster(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        return url.Replace("/t/p/original/", "/t/p/w300/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Deep links shown on each tile — always show-specific. A missing slug/ratingKey falls back
    /// to that provider's *search* route, never to its bare root page.
    /// </summary>
    private static IReadOnlyList<ActivityProviderLinkDto> BuildProviderLinks(
        ActivityLog row,
        ProviderBases bases,
        LinkIndex index,
        int? resolvedMalId = null)
    {
        var links = new List<ActivityProviderLinkDto>(3);

        // MyAnimeList on every card: rows that are not MAL-originated (Sonarr imports, Plex
        // scrobbles) still deserve a way to reach the entry.
        var malId = (row.MalId is > 0 ? row.MalId : resolvedMalId) is { } id
            ? id.ToString()
            : index.MalIdByTitle.GetValueOrDefault(NormaliseTitle(row.Title));

        var malUrl = !string.IsNullOrWhiteSpace(malId)
            ? $"https://myanimelist.net/anime/{malId}"
            : $"https://myanimelist.net/anime.php?q={Uri.EscapeDataString(row.Title)}";

        links.Add(new ActivityProviderLinkDto("mal", "MyAnimeList", malUrl));

        if (bases.Sonarr is not null)
        {
            string? slug = null;

            if (row.TvdbId is > 0 && index.SlugByTvdbId.TryGetValue(row.TvdbId.Value.ToString(), out var byTvdb))
                slug = byTvdb;
            else if (row.SonarrSeriesId is not null &&
                     index.SlugBySeriesId.TryGetValue(row.SonarrSeriesId.Value.ToString(), out var byId))
                slug = byId;
            else if (index.SlugByTitle.TryGetValue(NormaliseTitle(row.Title), out var byTitle))
                // Plex scrobbles and MAL adds carry no Sonarr ids: match the cached series by
                // title so the button still lands on the show page instead of Sonarr's search.
                slug = byTitle;

            var url = !string.IsNullOrWhiteSpace(slug)
                ? $"{bases.Sonarr}/series/{slug}"
                : $"{bases.Sonarr}/add/search?term={Uri.EscapeDataString(row.Title)}";

            links.Add(new ActivityProviderLinkDto("sonarr", "Sonarr", url));
        }

        // Every media row offers all three providers; the buttons reflect the library, not the
        // event type (a Sonarr import links to the show on Sonarr, Plex and MAL alike).
        if (bases.Plex is not null)
        {
            var key = index.PlexKeyByTitle.GetValueOrDefault(NormaliseTitle(row.Title));

            // Always go through app.plex.tv, like the anime cards do: the configured host is a
            // LAN address (http://192.168.x.x:32400) that only works on this network and in this
            // browser profile. The web app resolves the server by machine id from anywhere.
            var url = !string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(index.PlexMachineId)
                ? $"https://app.plex.tv/desktop/#!/server/{index.PlexMachineId}/details?key=%2Flibrary%2Fmetadata%2F{key}"
                : $"https://app.plex.tv/desktop/#!/search?query={Uri.EscapeDataString(row.Title)}";

            links.Add(new ActivityProviderLinkDto("plex", "Plex", url));
        }

        return links;
    }

    /// <summary>
    /// Parses the Sonarr/Plex caches once per feed request so links can point at the real show
    /// page. Unparseable rows are simply absent and fall back to a provider search route.
    /// </summary>
    private async Task<LinkIndex> LoadLinkIndexAsync(CancellationToken cancellationToken)
    {
        var byTvdb = new Dictionary<string, string>();
        var bySeries = new Dictionary<string, string>();
        var seriesIdByTvdb = new Dictionary<string, string>();
        var posterByTvdb = new Dictionary<string, string>();
        var posterBySeries = new Dictionary<string, string>();
        var posterByTitle = new Dictionary<string, string>();

        var byTitle = new Dictionary<string, string>();
        try
        {
            foreach (var entry in await cacheEntryRepository.GetAsync(AnimeSource.Sonarr))
            {
                // One malformed cache row must not empty the whole index (that made every Sonarr
                // button fall back to the /add/search page).
                try
                {
                    using var doc = JsonDocument.Parse(entry.RawData);
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) continue;

                    var slug = root.TryGetProperty("titleSlug", out var slugElement) ? slugElement.GetString() : null;
                    if (string.IsNullOrWhiteSpace(slug)) slug = entry.SourceId;

                    var poster = ShrinkPoster(ExtractSonarrPoster(root));

                    int? tvdbId = root.TryGetProperty("tvdbId", out var tvdb) && tvdb.TryGetInt32(out var parsedTvdb)
                        ? parsedTvdb
                        : null;
                    if (tvdbId is not null)
                    {
                        byTvdb[tvdbId.Value.ToString()] = slug!;
                        if (poster is not null) posterByTvdb[tvdbId.Value.ToString()] = poster;
                    }
                    if (root.TryGetProperty("id", out var id) && id.TryGetInt32(out var seriesId))
                    {
                        bySeries[seriesId.ToString()] = slug!;
                        if (poster is not null) posterBySeries[seriesId.ToString()] = poster;
                        if (tvdbId is not null) seriesIdByTvdb[tvdbId.Value.ToString()] = seriesId.ToString();
                    }
                    if (root.TryGetProperty("title", out var titleElement) &&
                        titleElement.GetString() is { Length: > 0 } title)
                    {
                        var key = NormaliseTitle(title);
                        byTitle[key] = slug!;
                        if (poster is not null) posterByTitle[key] = poster;
                    }
                }
                catch (JsonException) { /* unreadable row → enrichment job rebuilds it */ }
            }
        }
        catch { /* cache unavailable → search fallback */ }

        var malBySeries = new Dictionary<string, string>(StringComparer.Ordinal);
        var malAnimeIds = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var sonarrAnimeIds = new Dictionary<string, Guid>(StringComparer.Ordinal);
        try
        {
            var mappings = await animeMappingRepository.GetAllAsync(includeAnime: true);
            foreach (var mapping in mappings)
            {
                if (mapping.Anime is null || string.IsNullOrWhiteSpace(mapping.Anime.SourceId)) continue;
                var target = mapping.Anime.AnimeSource switch
                {
                    AnimeSource.MyAnimeList => malAnimeIds,
                    AnimeSource.Sonarr => sonarrAnimeIds,
                    _ => null,
                };
                target?[mapping.Anime.SourceId] = mapping.Anime.Id;
            }

            foreach (var group in mappings.GroupBy(m => m.AniDbId))
            {
                var malId = group.FirstOrDefault(m => m.Anime?.AnimeSource == AnimeSource.MyAnimeList)?.Anime?.SourceId;
                if (string.IsNullOrWhiteSpace(malId)) continue;

                foreach (var sonarr in group.Where(m => m.Anime?.AnimeSource == AnimeSource.Sonarr))
                {
                    if (!string.IsNullOrWhiteSpace(sonarr.Anime?.SourceId))
                        malBySeries[sonarr.Anime!.SourceId] = malId!;
                }
            }
        }
        catch { /* mapping table unavailable → title fallback */ }

        var malByTitle = new Dictionary<string, string>(StringComparer.Ordinal);
        var malPosterByTitle = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            foreach (var entry in await cacheEntryRepository.GetAsync(AnimeSource.MyAnimeList))
            {
                try
                {
                    using var doc = JsonDocument.Parse(entry.RawData);
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) continue;

                    var poster = ShrinkPoster(ExtractMalPoster(root));

                    // Jikan keeps the romaji title, the English one and user synonyms. Plex and
                    // Sonarr report whatever the file is named, so index every alias.
                    var titles = new List<string>();
                    foreach (var prop in new[] { "title", "title_english", "alternativeTitle" })
                    {
                        if (root.TryGetProperty(prop, out var titleElement) &&
                            titleElement.GetString() is { Length: > 0 } title)
                        {
                            titles.Add(title);
                        }
                    }

                    if (root.TryGetProperty("alternative_titles", out var aliases) && aliases.ValueKind == JsonValueKind.Object)
                    {
                        if (aliases.TryGetProperty("en", out var en) && en.GetString() is { Length: > 0 } enTitle)
                            titles.Add(enTitle);
                        if (aliases.TryGetProperty("synonyms", out var synonyms) && synonyms.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var synonym in synonyms.EnumerateArray())
                            {
                                if (synonym.GetString() is { Length: > 0 } synonymTitle)
                                    titles.Add(synonymTitle);
                            }
                        }
                    }

                    foreach (var title in titles)
                    {
                        var key = NormaliseTitle(title);
                        malByTitle[key] = entry.SourceId;
                        if (poster is not null) malPosterByTitle[key] = poster;
                    }
                }
                catch (JsonException) { /* unreadable row → MAL search fallback */ }
            }
        }
        catch { /* cache unavailable → MAL search fallback */ }

        var byPlexTitle = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            foreach (var entry in await cacheEntryRepository.GetAsync(AnimeSource.Plex))
            {
                try
                {
                    using var doc = JsonDocument.Parse(entry.RawData);
                    var root = doc.RootElement;
                    if (!root.TryGetProperty("ratingKey", out var ratingKey)) continue;

                    foreach (var prop in new[] { "grandparentTitle", "title" })
                    {
                        if (root.TryGetProperty(prop, out var titleElement) &&
                            titleElement.GetString() is { Length: > 0 } title)
                        {
                            byPlexTitle[NormaliseTitle(title)] = ratingKey.ToString();
                        }
                    }
                }
                catch (JsonException) { /* unreadable row → search fallback */ }
            }
        }
        catch { /* cache unavailable → search fallback */ }

        string? machineId = null;
        try
        {
            machineId = (await propertyBag.GetByKeyAsync(SettingsKeys.PlexMachineIdentifier))?.Value;
        }
        catch { /* not configured */ }

        return new LinkIndex(
            byTvdb, bySeries, byTitle, malByTitle, malBySeries, seriesIdByTvdb,
            malAnimeIds, sonarrAnimeIds, byPlexTitle, machineId,
            posterByTvdb, posterBySeries, posterByTitle, malPosterByTitle);
    }
}
