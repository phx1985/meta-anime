namespace MetaAnime.Core.Sync;

public enum SyncStepState { Running, Done, Error }

public record SyncProgressUpdate(
    string StepKey,
    SyncStepState State,
    int? ProgressPercent,
    long ElapsedMs,
    string? ErrorMessage = null);
