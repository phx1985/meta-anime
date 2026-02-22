namespace MetaAnime.Application.Contracts;

public record SettingDefinition(
    string Key,
    string Category,
    string DisplayName,
    string Description,
    bool IsSensitive,
    bool IsRequired,
    string? Value
);
