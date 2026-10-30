using MetaAnime.Application.Interfaces;
using Microsoft.AspNetCore.DataProtection;

namespace MetaAnime.Utilities.Security;

/// <summary>
/// DataProtection-backed <see cref="ISecretProtector"/>.
/// Keys persist to the configured key ring (METAANIME_KEYS_DIR / PersistKeysToFileSystem),
/// so protected values survive container restarts and cannot be decrypted elsewhere.
/// </summary>
public sealed class DataProtectionSecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    // Bump the segment if the payload format ever changes (invalidates old values).
    private const string Purpose = "MetaAnime.SecretProtector.v1";
    private const string Prefix = "dp1:";

    private readonly IDataProtector _protector = provider.CreateProtector(Purpose);

    public bool IsProtected(string value) =>
        !string.IsNullOrEmpty(value) && value.StartsWith(Prefix, StringComparison.Ordinal);

    public string Protect(string plaintext) =>
        Prefix + _protector.Protect(plaintext);

    public bool TryUnprotect(string protectedValue, out string plaintext)
    {
        plaintext = string.Empty;
        if (string.IsNullOrEmpty(protectedValue)) return true;

        // Legacy plaintext rows (written before encryption at rest existed).
        if (!IsProtected(protectedValue))
        {
            plaintext = protectedValue;
            return true;
        }

        try
        {
            plaintext = _protector.Unprotect(protectedValue[Prefix.Length..]);
            return true;
        }
        catch (Exception)
        {
            // Tampered, corrupted, or encrypted under a lost key ring.
            return false;
        }
    }
}
