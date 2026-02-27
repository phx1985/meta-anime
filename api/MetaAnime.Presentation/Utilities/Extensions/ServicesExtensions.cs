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
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Options;
using MetaAnime.Core.Settings;

namespace MetaAnime.Utilities.Extensions;

public static class ServicesExtensions
{
    /// <summary>
    /// Add custom services to the service collection
    /// </summary>
    public static void AddCustomServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMediator();

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
        services.AddScoped<IMalAnimeSourceService<MalAnime>, MalAnimeSourceService>();
        services.AddScoped<IMalTokenService, MalTokenService>();
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
        services.AddAuthentication("ApiKey").AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>("ApiKey", options => { });
        services.AddAuthorization();
    }

    /// <summary>
    /// Add swagger with API key
    /// </summary>
    /// <param name="services"></param>
    /// <returns></returns>
    public static IServiceCollection AddSwaggerGenWithApiKey(this IServiceCollection services)
    {
        services.AddSwaggerGen(c =>
        {
            c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
            {
                Description = "API Key Authentication",
                Name = "X-Api-Key",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.ApiKey,
                Scheme = "ApiKey"
            });


            c.AddSecurityRequirement(doc => new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecuritySchemeReference("ApiKey", doc),
                    new List<string>()
                }
            });
        });

        return services;
    }


}
