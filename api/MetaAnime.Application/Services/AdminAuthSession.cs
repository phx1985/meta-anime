using MetaAnime.Application.Contracts;
using MetaAnime.Application.Interfaces;
using MetaAnime.Core;
using MetaAnime.Core.Interfaces.Repositories;

namespace MetaAnime.Application.Services;

/// <summary>
/// Shared JWT-secret / session-lifetime resolution for the admin auth commands.
/// The JWT secret is generated once on first use and persisted; the session
/// lifetime is read from settings (default 7 days, clamped to 1–365).
/// </summary>
internal static class AdminAuthSession
{
    internal static async Task<string> GetOrCreateJwtSecretAsync(IPropertyBagRepository repo)
    {
        var existing = await repo.GetByKeyAsync(SettingsKeys.JwtSecret);
        if (existing is not null && !string.IsNullOrWhiteSpace(existing.Value))
            return existing.Value;

        // Generate a cryptographically-random 256-bit secret on first use.
        var secret = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        await repo.UpsertAsync(SettingsKeys.JwtSecret, secret);
        return secret;
    }

    internal static async Task<TimeSpan> GetSessionLifetimeAsync(IPropertyBagRepository repo)
    {
        var entry = await repo.GetByKeyAsync(SettingsKeys.SessionLifetimeDays);
        if (int.TryParse(entry?.Value, out var days) && days >= 1 && days <= 365)
            return TimeSpan.FromDays(days);
        return TimeSpan.FromDays(7);
    }

    internal static async Task<LoginResponse> IssueLoginAsync(
        IPropertyBagRepository repo, IAdminAuthService adminAuthService)
    {
        var secret = await GetOrCreateJwtSecretAsync(repo);
        var lifetime = await GetSessionLifetimeAsync(repo);
        return new LoginResponse(adminAuthService.GenerateJwt(secret, lifetime));
    }
}
