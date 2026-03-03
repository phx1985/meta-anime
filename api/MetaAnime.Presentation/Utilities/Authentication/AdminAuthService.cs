using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using MetaAnime.Application.Interfaces;
using Microsoft.IdentityModel.Tokens;

namespace MetaAnime.Utilities.Authentication;

public sealed class AdminAuthService : IAdminAuthService
{
    private const string Issuer = "MetaAnime";
    private const string Audience = "MetaAnime";
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromDays(30);

    public string HashPassword(string password) =>
        BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);

    public bool VerifyPassword(string password, string hash) =>
        BCrypt.Net.BCrypt.Verify(password, hash);

    public string GenerateJwt(string secret)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: [new Claim(ClaimTypes.Name, "admin")],
            expires: DateTime.UtcNow.Add(TokenLifetime),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public bool ValidateJwt(string token, string secret)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Split('.').Length != 3)
            return false;

        try
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
            var handler = new JwtSecurityTokenHandler();
            handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ValidateIssuer = true,
                ValidIssuer = Issuer,
                ValidateAudience = true,
                ValidAudience = Audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero,
            }, out _);

            return true;
        }
        catch
        {
            return false;
        }
    }
}
