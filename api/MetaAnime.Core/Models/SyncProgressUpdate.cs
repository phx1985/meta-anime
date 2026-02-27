namespace MetaAnime.Core.Sync;

public enum SyncStepState { Running, Done, Error }

/// <summary>Step key constants used in <see cref="SyncProgressUpdate"/> to identify each sync phase.</summary>
public static class SyncStepKeys
{
    public const string Kometa = "kometa";
    public const string Plex   = "plex";
    public const string Mal    = "mal";
    public const string Sonarr = "sonarr";
    public const string Match  = "matching";
}

public record SyncProgressUpdate(
    string StepKey,
    SyncStepState State,
    int? ProgressPercent,
    long ElapsedMs,
    string? ErrorMessage = null);
