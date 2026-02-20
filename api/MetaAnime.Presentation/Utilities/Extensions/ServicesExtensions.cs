using LukeHagar.PlexAPI.SDK;
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
using MetaAnime.Infrastructure.Plex.Utilities;
using MetaAnime.Infrastructure.Repositories;
using MetaAnime.Infrastructure.Sonarr.Services;
using MetaAnime.Infrastructure.SonarrAPIs.Sonarr.Models;
using MetaAnime.Utilities.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Options;
using MetaAnime.Core.Settings;
// using Microsoft.OpenApi.Models;

namespace MetaAnime.Utilities.Extensions;

public static class ServicesExtensions
{
    /// <summary>
    /// Add custom services to the service collection
    /// </summary>
    public static void AddCustomServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Register MediatR
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(IAnimeMediatorService).Assembly));

        // Register Options
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

        // External services
        services.AddSingleton<IPlexAPI, PlexAPI>(provider => 
        {
            var settings = provider.GetRequiredService<IOptions<PlexSettings>>().Value;
            return PlexAPI.Builder()
                .WithToken(settings.Token)
                .WithServerUrl(settings.Host)
                .WithClient(new SpeakeasyHttpClient2())
                .Build();
        });

        // Register custom services
        services.AddScoped<IKometaAnimeJsonService<KometaRaw>, KometaAnimeJsonService>();
        services.AddScoped<IKometaAnimeMappingService, KometaAnimeMappingService>();
        services.AddScoped<ISonarrAnimeSourceService<SonarrSeries>, SonarrAnimeSourceService>();
        services.AddScoped<IPlexAnimeSourceService<PlexMedia>, PlexAnimeSourceService>();
        services.AddScoped<IMalAnimeSourceService<MalAnime>, MalAnimeSourceService>();
        services.AddScoped<ICacheEntryService, CacheEntryService>();
        services.AddScoped<IAnimeService, AnimeService>();
        services.AddScoped<IAnimeMappingService, AnimeMappingService>();
        services.AddScoped<IAnimeMatchingService, AnimeMatchingService>();
        services.AddScoped<IAnimeMediatorService, AnimeMediatorService>();

        // Register repositories
        services.AddScoped<IKometaAnimeMappingRepository, KometaAnimeMappingRepository>();
        services.AddScoped<ICacheEntryRepository, CacheEntryRepository>();
        services.AddScoped<IAnimeRepository, AnimeRepository>();
        services.AddScoped<IAnimeMappingRepository, AnimeMappingRepository>();
    }

    /// <summary>
    /// Setup database
    /// </summary>
    public static void SetupDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        var dbSettings = configuration.GetSection(DatabaseSettings.SectionName).Get<DatabaseSettings>();

        // If configuration is missing, fallback or let it fail? 
        // Previous logic handled EF command skipping. 
        // If dbSettings is null here, CreateDbContext will fail later.
        // For EF Migrations we might need to handle this gracefully if we want to support generating script without env vars.
        // But let's assume valid config or fail for now as Get<T> binds.

        var connectionString = dbSettings?.ConnectionString ?? string.Empty;

        services.AddDbContext<MetaAnimeDbContext>(options => options.UseNpgsql(connectionString));
    }


    /// <summary>
    /// Migrate database
    /// </summary>
    /// <param name="serviceProvider"></param>
    public static void MigrateDatabase(this IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MetaAnimeDbContext>();

        // Wait for the database to be ready
        var retries = 10;
        var delay = TimeSpan.FromSeconds(5);
        for (var i = 0; i < retries; i++)
        {
            try
            {
                if (dbContext.Database.CanConnect())
                {
                    break;
                }
            }
            catch
            {
                // Ignore exceptions and retry
            }
            Thread.Sleep(delay);
        }

        dbContext.Database.Migrate();
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
