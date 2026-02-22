namespace MetaAnime.Application.Contracts;

public record SettingDefinition(
    string Key,
    string Category,
    bool IsSensitive,
    bool IsRequired,
    string? Value,
    string FieldType = "text"
);
