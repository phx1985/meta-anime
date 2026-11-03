using Hangfire;
using Hangfire.PostgreSql;
using MetaAnime.Application.BackgroundJobs;
using MetaAnime.Core;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Settings;
using MetaAnime.Hubs;
using MetaAnime.Utilities.ErrorHandling;
using MetaAnime.Utilities.Extensions;
using MetaAnime.Utilities.Hangfire;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;
using System.Text.Json;
using System.Threading.RateLimiting;

// Writable-path contract for containers with a read-only root filesystem:
// the app directory (/app) is never written to. Deployments point the writable
// locations at mounted volumes/tmpfs via these optional environment variables;
// the container's s6 run script sets them to /config and /tmp.
// NOTE: ASP.NET's HostingEnvironmentExtensions.Initialize() CREATES the web root
// (Directory.CreateDirectory) when it is missing — so a /app/wwwroot default would
// try mkdir("/app") and crash on hosts with an immutable root filesystem (Bazzite).
// Resolution order: METAANIME_WEBROOT -> /app/wwwroot when it exists (container) ->
// a local-safe path next to the binary.
// Writable-path contract for containers with a read-only root filesystem:
// the app directory (/app) is never written to. Deployments point the writable
// locations at mounted volumes/tmpfs via these optional environment variables;
// the container's s6 run script sets them to /config and /tmp.
// NOTE: ASP.NET's HostingEnvironmentExtensions.Initialize() CREATES the web root
// (Directory.CreateDirectory) when it is missing — so a /app/wwwroot default would
// try mkdir("/app") and crash on hosts with an immutable root filesystem (Bazzite).
// Resolution order: METAANIME_WEBROOT -> /app/wwwroot when it exists (container) ->
// a local-safe path next to the binary.
var webRootPath = Environment.GetEnvironmentVariable("METAANIME_WEBROOT");
if (string.IsNullOrEmpty(webRootPath))
{
    webRootPath = Directory.Exists("/app/wwwroot")
        ? "/app/wwwroot"
        : Path.Combine(AppContext.BaseDirectory, "wwwroot");
}
var dataProtectionKeysDir = Environment.GetEnvironmentVariable("METAANIME_KEYS_DIR");
var assetConfigDir = Environment.GetEnvironmentVariable("METAANIME_ASSET_CONFIG_DIR") ?? "/config/wwwroot-assets";

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = webRootPath
});

// Read container-generated secrets (written by the s6 init script on first boot).
// This file is optional so the app still starts in development without it.
builder.Configuration.AddJsonFile("/config/appsettings.json", optional: true, reloadOnChange: false);

// Explicitly load the environment-specific settings file (e.g. appsettings.Development.json).
// AppContext.BaseDirectory is used so the file is resolved from the binary output directory,
// where MSBuild copies it via CopyToOutputDirectory=PreserveNewest. This is necessary when
// running via Aspire, where the content root may not match the directory containing the file.
builder.Configuration.AddJsonFile(
    Path.Combine(AppContext.BaseDirectory, $"appsettings.{builder.Environment.EnvironmentName}.json"),
    optional: true,
    reloadOnChange: true);

builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddResponseCaching();

// Global error handling (RFC 9457): unhandled exceptions are logged server-side
// and answered with the unified ProblemDetails envelope — never raw exception text.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// Bot-kun AI endpoints: per-user rate limit (20 req/min) to protect free-tier
// provider quotas and the server from runaway or looping clients.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy<string>("botkun", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.Identity?.Name
                ?? context.Connection.RemoteIpAddress?.ToString()
                ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
});

builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
        options.PayloadSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter()));

// Configure CORS — allow the Angular dev-server origin in Development
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            var allowedOrigins = builder.Configuration
                .GetSection("AllowedCorsOrigins")
                .Get<string[]>()
                ?? ["http://localhost:4200"];

            policy.WithOrigins(allowedOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        }
    });
});

// Add API versioning — .AddApiExplorer() is intentionally omitted: Asp.Versioning.Mvc.ApiExplorer 8.1.1
// creates ApiParameterDescription objects with ModelMetadata=null on .NET 10, causing
// IsConvertibleType / IsEnhancedModelMetadataSupported crashes at MapControllers() time.
// Version substitution in OpenAPI paths is handled by the document transformer instead.
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new Asp.Versioning.ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
});

// Configure Hangfire
var hangfireConnectionString = builder.Configuration.GetConnectionString("metaanime")
    ?? builder.Configuration.GetSection(DatabaseSettings.SectionName).Get<DatabaseSettings>()?.ConnectionString
    ?? string.Empty;
builder.Services.AddHangfire(configuration => configuration
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(c =>
        c.UseNpgsqlConnection(hangfireConnectionString)));

builder.Services.AddHangfireServer();

// OpenAPI document generation (Microsoft.AspNetCore.OpenApi — no Swashbuckle, avoids
// the IsConvertibleType / IsEnhancedModelMetadataSupported crash on .NET 10).
builder.Services.AddSwaggerGenWithApiKey();
builder.Services.AddCustomServices(builder.Configuration);
builder.Services.SetupDatabase(builder.Configuration);
builder.Services.SetupAuthentication();

// Persist Data Protection keys to the configured writable directory so they
// survive restarts. On failure (e.g. misconfigured read-only path) fall back to
// the ephemeral repository instead of crashing startup.
if (!string.IsNullOrWhiteSpace(dataProtectionKeysDir))
{
    try
    {
        builder.Services.AddDataProtection()
            .PersistKeysToFileSystem(Directory.CreateDirectory(dataProtectionKeysDir));
    }
    catch (IOException ex)
    {
        Console.Error.WriteLine(
            $"Data Protection keys directory '{dataProtectionKeysDir}' is not writable ({ex.Message}); " +
            "using an ephemeral key repository. Keys will not survive restarts.");
    }
}
var app = builder.Build();
await app.Services.MigrateDatabaseAsync();

// Hangfire.PostgreSql probes its version table ("hangfire"."schema") BEFORE creating it,
// so on a fresh database Postgres logs `relation "hangfire.schema" does not exist` once.
// Pre-create the schema and version table with the installer's own idempotent DDL so the
// probe simply reads zero rows and the install proceeds silently. The warm-up connection
// also forces the (idempotent) install to complete single-threaded before the background
// server hands connections to its workers.
await using (var hangfireSetup = new Npgsql.NpgsqlConnection(hangfireConnectionString))
{
    await hangfireSetup.OpenAsync();
    await using var setupCommand = new Npgsql.NpgsqlCommand(
        """
        CREATE SCHEMA IF NOT EXISTS "hangfire";
        CREATE TABLE IF NOT EXISTS "hangfire"."schema" ("version" INT NOT NULL, PRIMARY KEY ("version"));
        """, hangfireSetup);
    await setupCommand.ExecuteNonQueryAsync();
}
using (var hangfireConnection = (app.Services.GetService<JobStorage>() ?? JobStorage.Current).GetConnection())
{
    // Job ids are numeric in Hangfire.PostgreSql (GetJobData parses the id as long) —
    // a nonexistent id just returns null but still forces the schema bootstrap above.
    hangfireConnection.GetJobData("1");
}

// Configure the HTTP request pipeline.
app.UseExceptionHandler(); // unified RFC 9457 error envelope for unhandled exceptions

if (app.Environment.IsDevelopment())
{
    // Native .NET 10 OpenAPI endpoint + Scalar UI (replaces Swashbuckle)
    app.MapOpenApi();
    app.MapScalarApiReference(opts => opts
        .WithTitle("MetaAnime API")
        .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient));
}

app.MapDefaultEndpoints();

// Health check endpoint — available in all environments for Docker HEALTHCHECK probe
app.MapHealthChecks("/api/health");

// Serve the Angular runtime config written by the container init script from the
// writable directory (see METAANIME_ASSET_CONFIG_DIR) as an overlay at /assets.
// A request that the overlay cannot satisfy falls through to the wwwroot static
// files below, so the rest of /assets/* keeps working unchanged.
//
// SECURITY: /assets/config.json is served DYNAMICALLY (below) and never from a
// static file. Once an admin password exists, the runtime API key is returned
// only to authenticated callers (or via GET /api/v1/auth/runtime-key) — an
// anonymous visitor must never receive a credential that grants full API access.
app.Use(async (context, next) =>
{
    if (HttpMethods.IsGet(context.Request.Method) &&
        string.Equals(context.Request.Path, "/assets/config.json", StringComparison.OrdinalIgnoreCase))
    {
        var apiKey = string.Empty;
        var repo = context.RequestServices.GetRequiredService<IPropertyBagRepository>();
        var hashEntry = await repo.GetByKeyAsync(SettingsKeys.AdminPasswordHash);
        var protectedMode = !string.IsNullOrWhiteSpace(hashEntry?.Value);

        // First-run / wizard mode (no password yet): keep the original behaviour and
        // hand the key to the SPA. Protected mode: only authenticated requests get it.
        if (!protectedMode || (await context.AuthenticateAsync()).Succeeded)
        {
            apiKey = context.RequestServices
                .GetRequiredService<IOptions<AuthenticationSettings>>().Value.ApiKey;
        }

        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new { apiKey }));
        return;
    }

    await next();
});

if (Directory.Exists(assetConfigDir))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(assetConfigDir),
        RequestPath = "/assets"
    });
}

// Serve Angular static files from wwwroot (populated during Docker build)
app.UseDefaultFiles();
app.UseStaticFiles();

// Schedule background jobs
var recurringJobManager = app.Services.GetRequiredService<IRecurringJobManager>();
await AnimeSyncJob.ScheduleAsync(recurringJobManager, app.Services);
MalTokenRefreshJob.Schedule(recurringJobManager);
await DownloadStatsCacheJob.ScheduleAsync(recurringJobManager, app.Services);

app.UseCors();
app.UseResponseCaching();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// Reset admin password if the flag is set (recovery mechanism).
var authOptions = app.Services.GetRequiredService<IOptions<MetaAnime.Core.Settings.AuthenticationSettings>>();
if (authOptions.Value.ResetAdminPassword)
{
    using var resetScope = app.Services.CreateScope();
    var propRepo = resetScope.ServiceProvider.GetRequiredService<MetaAnime.Core.Interfaces.Repositories.IPropertyBagRepository>();
    await propRepo.DeleteAsync(MetaAnime.Core.SettingsKeys.AdminPasswordHash);
    app.Logger.LogWarning("AdminPasswordHash has been reset. Remove Authentication:ResetAdminPassword from config to re-enable password protection.");
}

// Hangfire Dashboard — open in Development (browser can't send X-Api-Key),
// protected by HangfireAuthorizationFilter in all other environments.
var dashboardOptions = new DashboardOptions();
if (!app.Environment.IsDevelopment())
{
    dashboardOptions.Authorization = [new HangfireAuthorizationFilter()];
}
app.UseHangfireDashboard("/hangfire", dashboardOptions);

app.MapControllers();
app.MapHub<SyncHub>("/hubs/sync");

// Fallback to Angular index.html for all unmatched routes (client-side routing)
app.MapFallbackToFile("index.html");

app.Run();
