using MetaAnime.Application.Interfaces;
using MetaAnime.Core;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Settings;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace MetaAnime.Utilities.Authentication;

/// <summary>
/// Flexible authentication handler with two operating modes:
/// <list type="bullet">
///   <item>
///     <term>Public mode</term>
///     <description>When <c>AdminPasswordHash</c> is absent from the PropertyBag, every request
///     is automatically authenticated — the app is open without credentials.</description>
///   </item>
///   <item>
///     <term>Protected mode</term>
///     <description>When a hash is present, the request must carry either a valid JWT Bearer token
///     (issued by <c>AuthController.Login</c>) or the static <c>X-Api-Key</c> header (backward
///     compatibility / Swagger).</description>
///   </item>
/// </list>
/// </summary>
public sealed class FlexAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IServiceProvider serviceProvider,
    IOptions<AuthenticationSettings> authSettings,
    IAdminAuthService adminAuthService)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    private readonly string _apiKey = authSettings.Value.ApiKey;

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Resolve the repository inside a scope (handler lifetime is transient per request).
        using var scope = serviceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IPropertyBagRepository>();

        var hashEntry = await repo.GetByKeyAsync(SettingsKeys.AdminPasswordHash);
        var passwordHash = hashEntry?.Value;

        // ── Public mode: no password set ─────────────────────────────────────
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            return AuthenticateResult.Success(BuildTicket("public"));
        }

        // ── Protected mode: validate JWT Bearer token ─────────────────────────
        if (Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            var header = authHeader.FirstOrDefault() ?? string.Empty;
            if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var token = header["Bearer ".Length..].Trim();
                var secretEntry = await repo.GetByKeyAsync(SettingsKeys.JwtSecret);
                if (secretEntry is not null && adminAuthService.ValidateJwt(token, secretEntry.Value))
                {
                    return AuthenticateResult.Success(BuildTicket("jwt"));
                }

                return AuthenticateResult.Fail("Invalid or expired JWT.");
            }
        }

        // ── Protected mode fallback: X-Api-Key header (Swagger / scripts) ─────
        if (Request.Headers.TryGetValue("X-Api-Key", out var apiKeyHeader))
        {
            var provided = apiKeyHeader.FirstOrDefault();
            if (!string.IsNullOrEmpty(provided) && provided == _apiKey)
            {
                return AuthenticateResult.Success(BuildTicket("apikey"));
            }

            return AuthenticateResult.Fail("Invalid API key.");
        }

        return AuthenticateResult.Fail("No credentials provided.");
    }

    private AuthenticationTicket BuildTicket(string authMethod)
    {
        var claims = new[] { new Claim(ClaimTypes.Name, "admin"), new Claim("auth_method", authMethod) };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        return new AuthenticationTicket(principal, Scheme.Name);
    }
}
