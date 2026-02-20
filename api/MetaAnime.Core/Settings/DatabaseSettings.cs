namespace MetaAnime.Core.Settings;

public record DatabaseSettings
{
    public const string SectionName = "Postgres";
    public required string Host { get; init; }
    public required string User { get; init; }
    public required string Password { get; init; }
    public required string Db { get; init; }

    public string ConnectionString => $"Host={Host};Database={Db};Username={User};Password={Password}";
}
