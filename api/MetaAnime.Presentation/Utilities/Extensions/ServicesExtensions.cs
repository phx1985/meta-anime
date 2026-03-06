using MetaAnime.Application;
using MetaAnime.Application.BackgroundJobs;
using MetaAnime.Application.Interfaces;
using MetaAnime.Application.Services;
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
        services.AddScoped<IAnimeMatchingService, AnimeMatchingService>();
        services.AddScoped<IAnimeMediatorService, AnimeMediatorService>();

        services.AddScoped<IKometaAnimeMappingRepository, KometaAnimeMappingRepository>();
        services.AddScoped<ICacheEntryRepository, CacheEntryRepository>();
        services.AddScoped<IAnimeRepository, AnimeRepository>();
        services.AddScoped<IAnimeMappingRepository, AnimeMappingRepository>();
        services.AddScoped<IPropertyBagRepository, PropertyBagRepository>();

        services.AddScoped<ISyncProgressNotifier, SyncProgressNotifier>();
        services.AddScoped<AnimeSyncJob>();
        services.AddHostedService<SettingsSeedService>();
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

        services.AddDbContext<MetaAnimeDbContext>(options => options.UseNpgsql(connectionString));
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
