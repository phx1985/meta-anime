using MetaAnime.Application;
using MetaAnime.Application.BackgroundJobs;
using MetaAnime.Application.Interfaces;
using MetaAnime.Application.Services;
using MetaAnime.Application.Services.BotKun;
using MetaAnime.Application.Services.AniList;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Infrastructure;
using MetaAnime.Infrastructure.Kometa.Models;
using MetaAnime.Infrastructure.Kometa.Services;
using MetaAnime.Infrastructure.MyAnimeList.Models;
using MetaAnime.Infrastructure.MyAnimeList.Services;
using MetaAnime.Infrastructure.Plex.Models;
using MetaAnime.Infrastructure.Plex.Services;
using MetaAnime.Infrastructure.Repositories;
using MetaAnime.Infrastructure.Sonarr.Services;
using MetaAnime.Infrastructure.SonarrAPIs.Sonarr.Models;
using MetaAnime.Presentation.Hubs;
using MetaAnime.Utilities.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using MetaAnime.Core.Settings;

namespace MetaAnime.Utilities.Extensions;

public static class ServicesExtensions
{
    /// <summary>
    /// Add custom services to the service collection
    /// </summary>
    public static void AddCustomServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddApplicationMediator();

        services.AddOptions<PlexSettings>()
            .Bind(configuration.GetSection(PlexSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<SonarrSettings>()
            .Bind(configuration.GetSection(SonarrSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<KometaSettings>()
            .Bind(configuration.GetSection(KometaSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<DatabaseSettings>()
            .Bind(configuration.GetSection(DatabaseSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<AuthenticationSettings>()
            .Bind(configuration.GetSection(AuthenticationSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<MalSettings>()
            .Bind(configuration.GetSection(MalSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient("Kometa");
        services.AddScoped<IKometaAnimeJsonService<KometaRaw>, KometaAnimeJsonService>();
        services.AddScoped<IKometaAnimeMappingService, KometaAnimeMappingService>();
        services.AddScoped<ISonarrAnimeSourceService<SonarrSeries>, SonarrAnimeSourceService>();
        services.AddScoped<IPlexAnimeSourceService<PlexMedia>, PlexAnimeSourceService>();
        services.AddScoped<IPlexAuthService, PlexAuthService>();
        services.AddScoped<IMalAnimeSourceService<MalAnime>, MalAnimeSourceService>();
        services.AddScoped<IMalTokenService, MalTokenService>();
        services.AddScoped<IMalOAuthService, MalOAuthService>();

        // Named HttpClient for calls to plex.tv (used by PlexAuthService)
        services.AddHttpClient("PlexTV", client =>
        {
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.DefaultRequestHeaders.Add("X-Plex-Product", "MetaAnime");
            client.DefaultRequestHeaders.Add("X-Plex-Client-Identifier", MetaAnime.Infrastructure.Plex.Constants.PlexClientIdentifier);
        });
        services.AddScoped<ICacheEntryService, CacheEntryService>();
        services.AddScoped<IAnimeService, AnimeService>();
        services.AddScoped<IAnimeMappingService, AnimeMappingService>();
        services.AddScoped<ICrossLinkService, CrossLinkService>();
        services.AddScoped<IAnimeMatchingService, AnimeMatchingService>();
        services.AddScoped<IAnimeMediatorService, AnimeMediatorService>();
        services.AddScoped<ILibraryAnalysisService, LibraryAnalysisService>();
        // External AI providers (Groq, OpenAI, LM Studio, local runners) can take 30-120 seconds
        // to respond — strip the global standard resilience handler (which imposes a hard 10s
        // AttemptTimeout) and use an infinite HttpClient timeout instead, relying solely on the
        // per-request CancellationToken for cancellation control.
#pragma warning disable EXTEXP0001 // RemoveAllResilienceHandlers is experimental but stable for our use
        services.AddHttpClient<IBotKunChatService, BotKunChatService>()
            .RemoveAllResilienceHandlers()
            // AI providers can take up to 3 minutes for long-form streaming responses.
            // We rely on the per-request CancellationToken for user-initiated cancellation.
            .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromMinutes(3));
#pragma warning restore EXTEXP0001

        // Bot-kun: taste-profile cache + at-rest secret protection (DataProtection-backed
        // — keys persist to the configured key ring, see AddDataProtection in Program.cs).
        services.AddMemoryCache();
        services.AddSingleton<MetaAnime.Application.Interfaces.ISecretProtector, MetaAnime.Utilities.Security.DataProtectionSecretProtector>();
        services.AddScoped<IBotKunTasteProfileService, BotKunTasteProfileService>();
        services.AddScoped<MetaAnime.Application.Interfaces.IAppConfigBackupService, MetaAnime.Application.Services.AppConfigBackupService>();

        services.AddScoped<IKometaAnimeMappingRepository, KometaAnimeMappingRepository>();
        services.AddScoped<ICacheEntryRepository, CacheEntryRepository>();
        services.AddScoped<IAnimeRepository, AnimeRepository>();
        services.AddScoped<IAnimeMappingRepository, AnimeMappingRepository>();
        services.AddScoped<IPropertyBagRepository, PropertyBagRepository>();
        services.AddScoped<IAnimeCastRepository, AnimeCastRepository>();
        services.AddScoped<IVoiceActorRepository, VoiceActorRepository>();
        // AniList GraphQL client: singleton pacing gate (400ms spacing, 55 req/60s sliding window,
        // queue with 15s drop budget — safe under AniList's ~90 req/min unauthenticated limit) and
        // two-state negative cache shared across requests. The pipeline POSTs { query, variables }
        // with the required headers, a hard 10s per-attempt timeout and the 3-attempt retry policy
        // on a fresh request message (and content) per attempt. Endpoint is config-overridable
        // (AniList:ApiUrl / env AniList__ApiUrl) for self-hosted proxies.
        services.AddOptions<AniListSettings>()
            .Bind(configuration.GetSection(AniListSettings.SectionName));
        services.AddSingleton<AniListPacingGate>();
        services.AddSingleton<AniListCastNegativeCache>();
        services.AddSingleton<IAniListClient, AniListHttpPipeline>();
        services.AddScoped<CastIdSpaceMigrator>();
        services.AddScoped<AniListPersonService>();
        services.AddScoped<AniListCastService>();
        services.AddHttpClient(AniListHttpPipeline.HttpClientName, (sp, client) =>
        {
            var apiUrl = sp.GetRequiredService<IOptions<AniListSettings>>().Value.ApiUrl;
            client.BaseAddress = new Uri(string.IsNullOrWhiteSpace(apiUrl) ? AniListSettings.DefaultApiUrl : apiUrl);
            // Timeout is enforced per attempt by the pipeline (hard 10s linked CTS).
            client.Timeout = Timeout.InfiniteTimeSpan;
        });

        services.AddScoped<ISyncProgressNotifier, SyncProgressNotifier>();
        services.AddSingleton<ISyncStateTracker, InMemorySyncStateTracker>();
        services.AddSingleton<IProviderHealthCache, InMemoryProviderHealthCache>();
        services.AddScoped<AnimeSyncJob>();
        services.AddHostedService<SettingsSeedService>();
        services.AddHostedService<ProviderHealthMonitor>();
    }

    /// <summary>
    /// Setup database
    /// </summary>
    public static void SetupDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        // Support both Aspire-injected connection string and manual configuration
        var connectionString = configuration.GetConnectionString("metaanime")
            ?? configuration.GetSection(DatabaseSettings.SectionName).Get<DatabaseSettings>()?.ConnectionString
            ?? string.Empty;

        // Augment the connection string with explicit pool limits so EF Core inherits them.
        // NpgsqlDbContextOptionsBuilder does not expose MaxPoolSize/MinPoolSize in v10+;
        // they are Npgsql connection-string-level settings.
        var pooledConnectionString = new Npgsql.NpgsqlConnectionStringBuilder(connectionString)
        {
            MaxPoolSize             = 50,
            MinPoolSize             = 5,
            ConnectionIdleLifetime  = (int)TimeSpan.FromMinutes(5).TotalSeconds,
        }.ConnectionString;

        services.AddDbContext<MetaAnimeDbContext>(options =>
            options.UseNpgsql(pooledConnectionString));
    }


    /// <summary>
    /// Migrate database
    /// </summary>
    /// <param name="serviceProvider"></param>
    public static async Task MigrateDatabaseAsync(this IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MetaAnimeDbContext>();

        var retries = 10;
        var delay = TimeSpan.FromSeconds(5);
        Exception? lastException = null;

        for (var i = 0; i < retries; i++)
        {
            if (i > 0)
                await Task.Delay(delay);

            try
            {
                // Pre-create the migrations history table so EF Core can query it
                // without logging "Failed executing DbCommand" on a fresh database.
                await dbContext.Database.ExecuteSqlRawAsync("""
                    CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
                        "MigrationId" character varying(150) NOT NULL,
                        "ProductVersion" character varying(32) NOT NULL,
                        CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
                    )
                    """);

                await dbContext.Database.MigrateAsync();
                return;
            }
            catch (Exception ex)
            {
                lastException = ex;
            }
        }

        throw lastException!;
    }

    /// <summary>
    /// Setup authentication
    /// </summary>
    /// <param name="services"></param>
    public static void SetupAuthentication(this IServiceCollection services)
    {
        services.AddSingleton<IAdminAuthService, AdminAuthService>();

        services.AddAuthentication("MetaAnime")
            .AddScheme<AuthenticationSchemeOptions, FlexAuthHandler>("MetaAnime", _ => { });

        services.AddAuthorization();
    }

    /// <summary>
    /// Registers the native .NET 10 OpenAPI document (Microsoft.AspNetCore.OpenApi) with
    /// an API-key security scheme. Replaces Swashbuckle to avoid the
    /// <c>IsConvertibleType / IsEnhancedModelMetadataSupported</c> crash on .NET 10 when
    /// Asp.Versioning.Mvc.ApiExplorer is also registered.
    /// </summary>
    public static IServiceCollection AddSwaggerGenWithApiKey(this IServiceCollection services)
    {
        services.AddOpenApi("v1", options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                // Security scheme
                document.Components ??= new();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes["ApiKey"] = new OpenApiSecurityScheme
                {
                    Type        = SecuritySchemeType.ApiKey,
                    Name        = "X-Api-Key",
                    In          = ParameterLocation.Header,
                    Description = "API Key Authentication",
                    Scheme      = "ApiKey",
                };

                (document.Security ??= []).Add(new OpenApiSecurityRequirement
                {
                    { new OpenApiSecuritySchemeReference("ApiKey", document), [] },
                });

                // Asp.Versioning.Mvc.ApiExplorer is not registered (.NET 10 incompatible),
                // so route templates still contain {version:apiVersion}. Replace with "v1"
                // so the OpenAPI paths are clean and usable.
                if (document.Paths is { Count: > 0 })
                {
                    var rekeyed = document.Paths
                        .ToDictionary(
                            kvp => kvp.Key.Replace("{version}", "v1", StringComparison.OrdinalIgnoreCase),
                            kvp => kvp.Value);
                    document.Paths.Clear();
                    foreach (var (k, v) in rekeyed)
                        document.Paths.Add(k, v);
                }

                return Task.CompletedTask;
            });
        });

        // Wrap the real IApiDescriptionGroupCollectionProvider so that any
        // ApiParameterDescription without enhanced model metadata (ModelMetadata=null,
        // typically {version:apiVersion} route params) is removed before
        // Microsoft.AspNetCore.OpenApi reads IsConvertibleType and throws.
        var existing = services.FirstOrDefault(
            d => d.ServiceType == typeof(IApiDescriptionGroupCollectionProvider));

        if (existing is not null)
        {
            services.Remove(existing);
            services.AddSingleton<IApiDescriptionGroupCollectionProvider>(sp =>
            {
                var inner = existing.ImplementationType is not null
                    ? (IApiDescriptionGroupCollectionProvider)ActivatorUtilities.CreateInstance(sp, existing.ImplementationType)
                    : existing.ImplementationFactory is not null
                        ? (IApiDescriptionGroupCollectionProvider)existing.ImplementationFactory(sp)
                        : (IApiDescriptionGroupCollectionProvider)existing.ImplementationInstance!;

                return new SafeApiDescriptionGroupCollectionProvider(inner);
            });
        }

        return services;
    }


}
