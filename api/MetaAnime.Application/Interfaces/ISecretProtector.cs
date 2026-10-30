namespace MetaAnime.Application.Interfaces;

/// <summary>
/// Reversible value protection for sensitive strings that travel through the
/// application layer (Bot-kun API keys at rest, signed search cursors).
/// The implementation uses ASP.NET Core DataProtection (registered in
/// MetaAnime.Presentation); values produced by <see cref="Protect"/> are opaque
/// and carry a recognizable prefix so legacy plaintext can still be read.
/// </summary>
public interface ISecretProtector
{
    /// <summary>Returns true when <paramref name="value"/> looks produced by <see cref="Protect"/>.</summary>
    bool IsProtected(string value);

    /// <summary>Protects a value; output is opaque and prefix-tagged.</summary>
    string Protect(string plaintext);

    /// <summary>
    /// Unprotects a value. Legacy plaintext (no protection prefix) is returned
    /// unchanged so existing rows keep working without migration.
    /// Returns false on tampered/corrupted/foreign values.
    /// </summary>
    bool TryUnprotect(string protectedValue, out string plaintext);
}
