using System.Collections.Frozen;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using MetaAnime.Application.Contracts;
using MetaAnime.Application.Interfaces;
using MetaAnime.Application.Queries;
using MetaAnime.Core;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Models.BotKun;
using MetaAnime.Infrastructure.MyAnimeList.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MetaAnime.Application.Services.BotKun;

public sealed class BotKunChatService(
    HttpClient httpClient,
    IPropertyBagRepository propertyBagRepository,
    IMalAnimeSourceService<MalAnime> malAnimeSourceService,
    IAnimeMediatorService animeMediatorService,
    ILogger<BotKunChatService> logger,
    IConfiguration configuration) : IBotKunChatService
{
    private static readonly JsonSerializerOptions SerializeOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // ── OpenAI-compatible DTOs ─────────────────────────────────────────────────

    private sealed record OpenAiMessage(string Role, string Content);

    private sealed record OpenAiChatRequest(
        string Model,
        IList<OpenAiMessage> Messages,
        bool Stream,
        [property: JsonPropertyName("max_tokens")] int? MaxTokens = null);

    // Non-streaming response: choices[0].message.content
    private sealed record OpenAiNonStreamChoice(OpenAiMessage? Message);
    private sealed record OpenAiNonStreamResponse(OpenAiNonStreamChoice[]? Choices);

    // Streaming chunk: choices[0].delta.content
    private sealed record OpenAiDelta([property: JsonPropertyName("content")] string? Content);
    private sealed record OpenAiStreamChoice(OpenAiDelta? Delta);
    private sealed record OpenAiStreamChunk(OpenAiStreamChoice[]? Choices);

    // OpenAI /models response shape
    private sealed record ModelsResponseEntry([property: JsonPropertyName("id")] string? Id);
    private sealed record ModelsResponse([property: JsonPropertyName("data")] ModelsResponseEntry[]? Data);

    // ── Public streaming entry points ──────────────────────────────────────────

    public async IAsyncEnumerable<string> StreamChatAsync(
        BotKunChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var isNsfw  = await IsNsfwAllowedAsync();
        var baseUrl = await ResolveEndpointAsync();
        var model   = await GetSettingAsync(SettingsKeys.BotKunModel) ?? string.Empty;
        var apiKey  = await GetSettingAsync(SettingsKeys.BotKunApiKey) ?? string.Empty;

        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(model))
        {
            yield return "Ara ara~ 🎌 Bot-kun has no AI provider configured! Head to **Settings → Bot-kun AI** and enter your API Base URL and Model name first.";
            yield break;
        }

        // ── Pass 1: Intent extraction (non-streaming) ──────────────────────
        BotKunSearchIntent? intent = null;
        try
        {
            intent = await ExtractSearchIntentAsync(request.UserMessage, request.History, baseUrl, model, apiKey, cancellationToken);
            logger.LogInformation(
                "Bot-kun intent: type={Type} genres=[{Genres}] airingStatus={AiringStatus} watchStatus={WatchStatus} page={Page}",
                intent?.Type,
                string.Join(",", intent?.Genres ?? []),
                intent?.AiringStatus,
                intent?.WatchStatus,
                intent?.Page);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Bot-kun intent extraction failed ({Error}); falling back to RAG.", ex.Message);
        }

        // ── Pass 2: Query database or fall back to MAL RAG ────────────────
        var isDbQuery = intent?.Type is "search" or "recommendation";
        string animeContext;
        bool hasNextPage = false;

        if (isDbQuery)
        {
            var filters = BuildFiltersFromIntent(intent!, isNsfw);
            var (results, nextPage) = await QueryAnimeAsync(filters, intent!.Page, cancellationToken);
            hasNextPage = nextPage;

            var filteredResults = ApplyYearFilter(results, intent!.MinYear, intent!.MaxYear);

            logger.LogInformation("Bot-kun DB query returned {Count} result(s) (after year filter).", filteredResults.Count);

            if (filteredResults.Count == 0)
            {
                // Stream an AI-generated "nothing found" response instead of a hardcoded string
                // so the Bot-kun persona stays consistent and natural.
                var zeroResultsPayload = new OpenAiChatRequest(
                    model,
                    [new("system", BotKunPromptBuilder.BuildZeroResults()), new("user", request.UserMessage)],
                    Stream: true,
                    MaxTokens: 100);

                await foreach (var token in ExecuteStreamAsync(baseUrl, apiKey, zeroResultsPayload, cancellationToken))
                    yield return token;
                yield break;
            }

            animeContext = BuildDbAnimeContext(filteredResults, isNsfw);
        }
        else
        {
            animeContext = await BuildRagContextAsync(request.UserMessage);
        }

        // ── Pass 3: Stream final response ─────────────────────────────────
        var systemPrompt = BotKunPromptBuilder.Build(isNsfw, animeContext, isDbQuery, intent?.Page ?? 1, hasNextPage);
        List<OpenAiMessage> messages = [new("system", systemPrompt)];
        foreach (var h in request.History)
            messages.Add(new(h.Role, h.Content));
        messages.Add(new("user", request.UserMessage));

        var payload = new OpenAiChatRequest(model, messages, Stream: true);

        await foreach (var token in ExecuteStreamAsync(baseUrl, apiKey, payload, cancellationToken))
            yield return token;
    }

    public async IAsyncEnumerable<string> StreamGreetingAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var baseUrl = await ResolveEndpointAsync();
        var model   = await GetSettingAsync(SettingsKeys.BotKunModel) ?? string.Empty;
        var apiKey  = await GetSettingAsync(SettingsKeys.BotKunApiKey) ?? string.Empty;

        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(model))
        {
            yield return "Yosh! Bot-kun is here! 🎌 Ask me anything about anime, manga, or your watch list. " +
                "I'm like a Pokédex, but for otakus — Dattebayo! (Configure your AI provider in Settings to enable AI responses.)";
            yield break;
        }

        List<OpenAiMessage> messages =
        [
            new("system", BotKunPromptBuilder.GreetingSystemPrompt),
            new("user", "greet"),
        ];

        var payload = new OpenAiChatRequest(model, messages, Stream: true, MaxTokens: 80);

        await foreach (var token in ExecuteStreamAsync(baseUrl, apiKey, payload, cancellationToken))
            yield return token;
    }

    // ── Model listing ──────────────────────────────────────────────────────────

    public async Task<string[]> GetAvailableModelsAsync(
        string baseUrl, string apiKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) return [];

        var url = baseUrl.TrimEnd('/') + "/models";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(apiKey))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        try
        {
            using var response = await httpClient.SendAsync(req, cancellationToken);
            if (!response.IsSuccessStatusCode) return [];

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var modelsResponse = JsonSerializer.Deserialize<ModelsResponse>(json, DeserializeOptions);
            return modelsResponse?.Data?
                .Select(m => m.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!)
                .Order()
                .ToArray() ?? [];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Failed to fetch models from {Url}: {Error}", url, ex.Message);
            return [];
        }
    }

    // ── Core streaming executor ────────────────────────────────────────────────

    private async IAsyncEnumerable<string> ExecuteStreamAsync(
        string endpoint,
        string apiKey,
        OpenAiChatRequest payload,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string? errorMessage = null;
        HttpResponseMessage? response = null;

        try
        {
            using var req = BuildRequest(HttpMethod.Post, endpoint, apiKey, payload);
            response = await httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning("AI provider unreachable at {Endpoint}: {Error}", endpoint, ex.Message);
            errorMessage = "Gomen nasai~ 🙏 I can't reach the AI provider right now. " +
                "Please check your **Bot-kun AI** settings and make sure the provider is running!";
        }
        catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            yield break;
        }

        if (errorMessage is not null) { yield return errorMessage; yield break; }

        if (!response!.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogError("AI provider returned HTTP {StatusCode}: {Body}", response.StatusCode, body);
            yield return $"Something went haywire — the AI provider returned HTTP {(int)response.StatusCode}. " +
                "Check your model name and API key in **Settings → Bot-kun AI**.";
            yield break;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new System.IO.StreamReader(stream);

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null) break;
            if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;

            var json = line["data: ".Length..].Trim();
            if (json is "[DONE]") yield break;
            if (string.IsNullOrEmpty(json)) continue;

            OpenAiStreamChunk? chunk;
            try { chunk = JsonSerializer.Deserialize<OpenAiStreamChunk>(json, DeserializeOptions); }
            catch (JsonException ex) { logger.LogWarning(ex, "Failed to parse stream chunk: {Json}", json); continue; }

            var content = chunk?.Choices?[0]?.Delta?.Content;
            if (!string.IsNullOrEmpty(content))
                yield return content;
        }
    }

    // ── Pass 1: Intent extraction (non-streaming) ──────────────────────────────

    private async Task<BotKunSearchIntent?> ExtractSearchIntentAsync(
        string userMessage, IList<ChatMessage> history, string endpoint, string model, string apiKey, CancellationToken ct)
    {
        List<OpenAiMessage> messages = [new("system", BotKunPromptBuilder.IntentExtractionSystemPrompt)];

        // Include recent history so AI can detect "more/next" pagination intent
        foreach (var h in history.TakeLast(4))
            messages.Add(new(h.Role, h.Content));

        messages.Add(new("user", userMessage));

        var payload = new OpenAiChatRequest(model, messages, Stream: false, MaxTokens: 300);

        using var req = BuildRequest(HttpMethod.Post, endpoint, apiKey, payload);
        using var httpResponse = await httpClient.SendAsync(req, ct);
        if (!httpResponse.IsSuccessStatusCode) return null;

        var responseJson = await httpResponse.Content.ReadAsStringAsync(ct);
        var envelope = JsonSerializer.Deserialize<OpenAiNonStreamResponse>(responseJson, DeserializeOptions);
        var content = envelope?.Choices?[0]?.Message?.Content;
        if (string.IsNullOrWhiteSpace(content)) return null;

        // The model may wrap JSON in prose — extract the first {...} object
        var start = content.IndexOf('{');
        var end   = content.LastIndexOf('}');
        if (start < 0 || end <= start) return null;

        return JsonSerializer.Deserialize<BotKunSearchIntent>(content[start..(end + 1)], DeserializeOptions);
    }

    // ── HTTP request builder ───────────────────────────────────────────────────

    private HttpRequestMessage BuildRequest<T>(HttpMethod method, string url, string apiKey, T body)
    {
        var req = new HttpRequestMessage(method, url)
        {
            Content = JsonContent.Create(body, options: SerializeOptions),
        };

        if (!string.IsNullOrWhiteSpace(apiKey))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        return req;
    }

    // ── Database filters from intent ───────────────────────────────────────────

    private static readonly FrozenSet<string> NsfwGenres = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "Ecchi", "Hentai", "Erotica");

    private static readonly FrozenSet<string> NsfwRatings = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "r+", "rx");

    private static readonly FrozenSet<string> ValidAiringStatuses = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "finished_airing", "currently_airing", "not_yet_aired");

    private static AnimeFilters BuildFiltersFromIntent(BotKunSearchIntent intent, bool isNsfw)
    {
        // SFW hard-filter: strip NSFW genres and ratings from intent before querying DB.
        // AnimeMediatorService also enforces this at DB level — this is defence-in-depth
        // so the AI context never even requests NSFW content when the setting is off.
        string[]? genres;
        string[]? ageRating;

        if (isNsfw)
        {
            genres    = intent.Genres;
            ageRating = intent.AgeRating;
        }
        else
        {
            var safeGenres = intent.Genres?.Where(g => !NsfwGenres.Contains(g)).ToArray();
            genres = safeGenres is { Length: > 0 } ? safeGenres : null;

            var safeRatings = intent.AgeRating?.Where(r => !NsfwRatings.Contains(r)).ToArray();
            ageRating = safeRatings is { Length: > 0 } ? safeRatings : null;
        }

        return new(
            Title:        intent.Title,
            Genre:        genres,
            AiringStatus: NormalizeAiringStatus(intent.AiringStatus) is string ais ? [ais] : null,
            Status:       NormalizeWatchStatus(intent.WatchStatus) is string ws ? [ws] : null,
            MediaType:    intent.MediaType   is not null ? [intent.MediaType]   : null,
            Demographic:  intent.Demographic is not null ? [intent.Demographic] : null,
            Theme:        intent.Themes,
            AgeRating:    ageRating);
    }

    private static string? NormalizeAiringStatus(string? raw) =>
        raw is not null && ValidAiringStatuses.Contains(raw) ? raw : null;

    private static string? NormalizeWatchStatus(string? raw) => raw?.ToLowerInvariant() switch
    {
        "plan_to_watch" or "plan to watch" or "unwatched" or "not_watched" or "not watched" => "plan_to_watch",
        "watching" or "in_progress" or "in progress"                                         => "watching",
        "completed" or "finished"                                                             => "completed",
        "dropped"                                                                             => "dropped",
        "on_hold" or "on hold" or "paused"                                                   => "on_hold",
        null or "" => null,
        _ => raw,
    };

    private async Task<(IReadOnlyList<AnimeDetailsResponse> Results, bool HasNextPage)> QueryAnimeAsync(
        AnimeFilters filters, int page, CancellationToken ct)
    {
        const int PageSize = 4;
        var result = await animeMediatorService.GetAllAsync(
            page: page, pageSize: PageSize, filters: filters, enriched: false);

        if (result.IsFailure) return ([], false);
        return (result.Value.Data?.ToList() ?? [], result.Value.HasNextPage);
    }

    private static IReadOnlyList<AnimeDetailsResponse> ApplyYearFilter(
        IReadOnlyList<AnimeDetailsResponse> results, int? minYear, int? maxYear)
    {
        if (minYear is null && maxYear is null) return results;

        return results.Where(anime =>
        {
            var startDate = anime.MalAnimes?.FirstOrDefault()?.StartDate;
            if (string.IsNullOrWhiteSpace(startDate)) return true;
            if (!int.TryParse(startDate.AsSpan(0, Math.Min(4, startDate.Length)), out var year)) return true;
            if (minYear.HasValue && year < minYear.Value) return false;
            if (maxYear.HasValue && year > maxYear.Value) return false;
            return true;
        }).ToList();
    }

    // ── Token-minimized context builder ───────────────────────────────────────
    // Only Id, Title, Type, Score are included — all heavy fields (Synopsis, ImageUrls,
    // Background, Characters, Genres, StartDate, Status) are explicitly excluded to keep
    // the context window tight and prevent rate-limiting on free-tier providers (e.g. Groq).

    private static string BuildDbAnimeContext(IReadOnlyList<AnimeDetailsResponse> results, bool isNsfw)
    {
        if (results.Count == 0)
            return "No anime found in the database matching those criteria. Tell the user honestly — no hallucinations.";

        var sb = new System.Text.StringBuilder();

        if (!isNsfw)
            sb.AppendLine("SFW ONLY: All results below are pre-verified safe-for-work content.");

        sb.AppendLine($"Found {results.Count} anime in the database:");
        sb.AppendLine();

        // Explicit permitted-ID list so the model can cross-reference easily
        var permittedIds = results
            .Select(a => a.MalAnimes?.FirstOrDefault()?.Id)
            .Where(id => id != null)
            .ToList();
        sb.AppendLine($"PERMITTED IDs: {string.Join(", ", permittedIds)}");
        sb.AppendLine();

        // Minimal 4-field lines to keep token usage low and prevent rate-limiting
        foreach (var anime in results)
        {
            var mal   = anime.MalAnimes?.FirstOrDefault();
            var malId = mal?.Id ?? "unknown";
            var type  = mal?.MediaType ?? "N/A";
            var score = mal?.Rating?.MalMean is double d ? $"{d:F1}" : "N/A";

            sb.AppendLine($"- MAL_ID={malId} | Title=\"{anime.Title}\" | Type={type} | Score={score}");
        }

        return sb.ToString();
    }

    // ── Shared helpers ─────────────────────────────────────────────────────────

    private async Task<bool> IsNsfwAllowedAsync()
    {
        var entry = await propertyBagRepository.GetByKeyAsync(SettingsKeys.AppShowNsfw);
        return entry?.Value?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
    }

    private async Task<string?> GetSettingAsync(string key)
    {
        var configValue = configuration[key];
        if (!string.IsNullOrWhiteSpace(configValue)) return configValue;

        var entry = await propertyBagRepository.GetByKeyAsync(key);
        return entry?.Value;
    }

    /// <summary>
    /// Resolves the full chat completions endpoint URL from the configured base URL.
    /// Appends /v1/chat/completions unless the URL already ends with it.
    /// </summary>
    private async Task<string> ResolveEndpointAsync()
    {
        var raw = await GetSettingAsync(SettingsKeys.BotKunBaseUrl) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        var trimmed = raw.TrimEnd('/');
        const string suffix = "/chat/completions";
        return trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : trimmed + suffix;
    }

    private async Task<string> BuildRagContextAsync(string userMessage)
    {
        if (string.IsNullOrWhiteSpace(userMessage)) return string.Empty;

        var result = await malAnimeSourceService.SearchByTitleAsync(userMessage, limit: 3);
        if (result.IsFailure || result.Value.Count == 0) return string.Empty;

        var lines = result.Value.Select(item =>
            $"- MAL_ID={item.Id} | \"{item.Title}\"{(item.Year.HasValue ? $" ({item.Year})" : string.Empty)}{(!string.IsNullOrWhiteSpace(item.MediaType) ? $" [{item.MediaType}]" : string.Empty)}");

        return string.Join("\n", lines);
    }
}
