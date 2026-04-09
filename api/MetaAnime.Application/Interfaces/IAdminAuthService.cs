namespace MetaAnime.Application.Interfaces;

public interface IAdminAuthService
{
    /// <summary>Returns the BCrypt hash of the given password.</summary>
    string HashPassword(string password);

    /// <summary>Verifies a plain-text password against a stored BCrypt hash.</summary>
    bool VerifyPassword(string password, string hash);

    /// <summary>
    /// Generates a signed JWT for the admin user.
    /// </summary>
    /// <param name="secret">The HMAC-SHA256 signing secret.</param>
    /// <param name="lifetime">Token validity duration.</param>
    string GenerateJwt(string secret, TimeSpan lifetime);

    /// <summary>Validates a JWT and returns true if it is valid and not expired.</summary>
    bool ValidateJwt(string token, string secret);
}
