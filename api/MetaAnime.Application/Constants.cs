namespace MetaAnime.Application;

public static class Constants
{
    public static class BotKunEndpoints
    {
        public const string Models           = "/models";
        public const string ChatCompletions  = "/chat/completions";
    }

    public static class Errors
    {
        public const string AddAsync = "Error.AddAsync";
        public const string AnyAsync = "Error.AnyAsync";
        public const string BulkDeleteAll = "Error.BulkDeleteAll";
        public const string BulkDeleteAllAsync = "Error.BulkDeleteAllAsync";
        public const string BulkDeleteBySourceAsync = "Error.BulkDeleteBySourceAsync";
        public const string BulkInsertAsync = "Error.BulkInsertAsync";
        public const string CreateAsync = "Error.CreateAsync";
        public const string DeleteAsync = "Error.DeleteAsync";
        public const string DeletePropertyBag = "Error.DeletePropertyBag";
        public const string GetAllAsync = "Error.GetAllAsync";
        public const string GetAllBySourceAsync = "Error.GetAllBySourceAsync";
        public const string GetAllFilteredAniDbIdsAsync = "Error.GetAllFilteredAniDbIdsAsync";
        public const string GetAllPropertyBag = "Error.GetAllPropertyBag";
        public const string GetAnimeAsync = "Error.GetAnimeAsync";
        public const string GetAsync = "Error.GetAsync";
        public const string GetByAniDbIdAsync = "Error.GetByAniDbIdAsync";
        public const string GetByAniDbIdsAsync = "Error.GetByAniDbIdsAsync";
        public const string GetByIdAsync = "Error.GetByIdAsync";
        public const string GetByPlatformIdAsync = "Error.GetByPlatformIdAsync";
        public const string GetBySourceIdsAsync = "Error.GetBySourceIdsAsync";
        public const string GetPagedAniDbIdsAsync = "Error.GetPagedAniDbIdsAsync";
        public const string GetPropertyBagByKey = "Error.GetPropertyBagByKey";
        public const string GetSonarrSeriesByCodecAsync = "Error.GetSonarrSeriesByCodecAsync";
        public const string GetStatisticsAsync = "Error.GetStatisticsAsync";
        public const string GetActivityStatsAsync = "Error.GetActivityStatsAsync";
        public const string GetCachedActivityStatsAsync = "Error.GetCachedActivityStatsAsync";
        public const string CacheDownloadStatsAsync = "Error.CacheDownloadStatsAsync";
        public const string GetSyncStatus = "Error.GetSyncStatus";
        public const string NotFound = "Error.NotFound";
        public const string RefreshData = "Error.RefreshData";
        public const string RemoveAsync = "Error.RemoveAsync";
        public const string SearchMalAnimeAsync = "Error.SearchMalAnimeAsync";
        public const string SearchMalAnimeWithDetailsAsync = "Error.SearchMalAnimeWithDetailsAsync";
        public const string SyncAllAsync = "Error.SyncAllAsync";
        public const string UpdateAsync = "Error.UpdateAsync";
        public const string UpsertPropertyBag = "Error.UpsertPropertyBag";
        public const string GetLibraryAnalysisAsync = "Error.GetLibraryAnalysisAsync";
        public const string EnrichLibrarySeriesAsync = "Error.EnrichLibrarySeriesAsync";
        public const string GetEpisodeFilesAsync = "Error.GetEpisodeFilesAsync";
        public const string GetSeiyuuNexusAsync = "Error.GetSeiyuuNexusAsync";
        public const string ResolveMalCoversAsync = "Error.ResolveMalCoversAsync";
        public const string CastProviderUnavailable = "Error.CastProviderUnavailable";
        public const string GetVoiceActorProfileAsync = "Error.GetVoiceActorProfileAsync";
        public const string SyncAnimeCastAsync = "Error.SyncAnimeCastAsync";
        public const string MigrateCastIdSpace = "Error.MigrateCastIdSpace";

        // Admin authentication flows
        public const string GetAuthStatus = "Error.GetAuthStatus";
        public const string Login = "Error.Login";
        public const string ChangePassword = "Error.ChangePassword";
        public const string SetupPassword = "Error.SetupPassword";
        public const string DisableAuth = "Error.DisableAuth";
        public const string AuthNotConfigured = "Error.AuthNotConfigured";
        public const string AuthInvalidPassword = "Error.AuthInvalidPassword";
        public const string AuthPasswordAlreadySet = "Error.AuthPasswordAlreadySet";
        public const string AuthEmptyPassword = "Error.AuthEmptyPassword";
        public const string AuthNotEnabled = "Error.AuthNotEnabled";

        // Cross-link queries/commands
        public const string GetCrossLinks = "Error.GetCrossLinks";
        public const string RemoveCrossLink = "Error.RemoveCrossLink";

        // Settings / property-bag write policy
        public const string GetMalOAuthRedirectUri = "Error.GetMalOAuthRedirectUri";
        public const string UpdateSetting = "Error.UpdateSetting";
        public const string PropertyBagKeyNotAllowed = "Error.PropertyBagKeyNotAllowed";
        public const string InvalidBotKunProviderUrl = "Error.InvalidBotKunProviderUrl";
    }

    public static class JobNames
    {
        public const string AnimeMetadataSync = "anime-metadata-sync";
        public const string MalTokenRefresh = "mal-token-refresh";
        public const string DownloadStatsCache = "download-stats-cache";
    }

    public static class LogMessages
    {
        public static class AnimeSyncJob
        {
            public const string Starting = "Starting anime metadata sync at {Time}";
            public const string Succeeded = "Successfully synced anime metadata at {Time}";
            public const string MatchFailed = "Failed to match anime: {ErrorMessage}";
            public const string Error = "Error during anime sync";
        }

        public static class MalTokenRefreshJob
        {
            public const string Starting = "Starting MAL token refresh at {Time}";
            public const string Skipped = "Skipping MAL token refresh: MyAnimeList ClientId is not configured.";
            public const string Succeeded = "Successfully refreshed MAL token at {Time}";
            public const string Failed = "Failed to refresh MAL token: {ErrorMessage}";
            public const string Error = "Error during MAL token refresh";
        }

        public static class DownloadStatsCacheJob
        {
            public const string Starting = "Starting download stats cache refresh at {Time}";
            public const string Succeeded = "Successfully cached download stats at {Time}";
            public const string Error = "Error during download stats cache refresh";
        }

        public static class CastSyncJob
        {
            public const string Starting  = "Starting cast sync for anime {MalId}";
            public const string Succeeded = "Cast sync complete for anime {MalId}: {Count} entries";
            public const string Skipped   = "Skipping cast sync for anime {MalId}: no cast data available";
            public const string Error     = "Error during cast sync for anime {MalId}";
        }

        public static class AniListPersonService
        {
            public const string Fetching = "Fetching AniList profile for person {PersonId}";
            public const string FetchFailed = "AniList fetch failed for person {PersonId}";
        }

        public static class AnimeMediatorService
        {
            public const string NoAnimeMappings = "AniDbId {AniDbId} has no anime mappings — data may be inconsistent";
            public const string MalDeserializeFailedGenreFilter = "Failed to deserialize MAL anime {MalId} for genre filtering: {Error}";
            public const string MalCacheDeserializeFailedStats = "Failed to deserialize MAL cache entry {SourceId} for statistics";
            public const string PlexCacheDeserializeFailedStats = "Failed to deserialize Plex cache entry {SourceId} for statistics";
            public const string MalAnimeMyListStatus = "MAL anime {SourceId} ({Title}) has MyListStatus: Status={Status}";
            public const string MalCacheDeserializeFailed = "Failed to deserialize MAL anime cache data for SourceId: {SourceId}";
            public const string SonarrAnimeSeasons = "Sonarr anime {SourceId} ({Title}) has {TotalSeasons} seasons";
            public const string SonarrCacheDeserializeFailed = "Failed to deserialize Sonarr anime cache data for SourceId: {SourceId}";
        }
    }
}
