using System.Text.Json;
using MetaAnime.Core;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Models;

namespace MetaAnime.Application.Services;

public sealed class CrossLinkService(IPropertyBagRepository propertyBagRepository) : ICrossLinkService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public async Task<List<CrossLinkEntry>> GetAllAsync()
    {
        var entry = await propertyBagRepository.GetByKeyAsync(SettingsKeys.CrossLinks);
        if (entry is null || string.IsNullOrWhiteSpace(entry.Value))
            return [];

        return JsonSerializer.Deserialize<List<CrossLinkEntry>>(entry.Value, JsonOptions) ?? [];
    }

    public async Task AddAsync(string sourceAniDbId, string malId)
    {
        var entries = await GetAllAsync();

        if (entries.Any(e => e.SourceAniDbId == sourceAniDbId && e.MalId == malId))
            return;

        entries.Add(new CrossLinkEntry(sourceAniDbId, malId));
        await propertyBagRepository.UpsertAsync(SettingsKeys.CrossLinks, JsonSerializer.Serialize(entries, JsonOptions));
    }

    public async Task RemoveAsync(string sourceAniDbId, string malId)
    {
        var entries = await GetAllAsync();
        var removed = entries.RemoveAll(e => e.SourceAniDbId == sourceAniDbId && e.MalId == malId);

        if (removed > 0)
            await propertyBagRepository.UpsertAsync(SettingsKeys.CrossLinks, JsonSerializer.Serialize(entries, JsonOptions));
    }
}
