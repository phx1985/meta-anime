using MetaAnime.Application.Contracts;

namespace MetaAnime.Application.Interfaces;

/// <summary>
/// Export/import of the portable configuration backup (all provider
/// credentials and preferences; admin/JWT secrets excluded by design).
/// </summary>
public interface IAppConfigBackupService
{
    /// <summary>Builds the backup payload with sensitive values decrypted.</summary>
    Task<AppConfigBackupDto> ExportAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts every non-null field (sensitive values re-encrypted). Returns the
    /// number of values written.
    /// </summary>
    Task<int> ImportAsync(AppConfigBackupDto backup, CancellationToken cancellationToken = default);
}
