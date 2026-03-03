namespace MetaAnime.Core.Settings;

public record AuthenticationSettings
{
    public const string SectionName = "Authentication";
    public required string ApiKey { get; init; }

    /// <summary>
    /// When true, the AdminPasswordHash is wiped from the PropertyBag on startup,
    /// restoring public access. Set via env var Authentication__ResetAdminPassword=true.
    /// </summary>
    public bool ResetAdminPassword { get; init; }
}
