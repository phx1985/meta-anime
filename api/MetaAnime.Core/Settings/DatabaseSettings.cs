namespace MetaAnime.Core.Settings;

public record DatabaseSettings
{
    public const string SectionName = "Postgres";
    public string Host { get; init; } = string.Empty;
    public string User { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string Db { get; init; } = string.Empty;

    public string ConnectionString => $"Host={Host};Database={Db};Username={User};Password={Password}";
}
