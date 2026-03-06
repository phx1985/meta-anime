using Hangfire;
using Hangfire.PostgreSql;
using MetaAnime.Application.BackgroundJobs;
using MetaAnime.Core.Settings;
using MetaAnime.Hubs;
using MetaAnime.Utilities.Extensions;
using MetaAnime.Utilities.Hangfire;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = "/app/wwwroot"
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
builder.Services.AddControllers();
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
var app = builder.Build();
await app.Services.MigrateDatabaseAsync();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    // Native .NET 10 OpenAPI endpoint + Scalar UI (replaces Swashbuckle)
    app.MapOpenApi();
    app.MapScalarApiReference(opts => opts
        .WithTitle("MetaAnime API")
        .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient));
}

app.MapDefaultEndpoints();

// Serve Angular static files from wwwroot (populated during Docker build)
app.UseDefaultFiles();
app.UseStaticFiles();

// Schedule background jobs
var recurringJobManager = app.Services.GetRequiredService<IRecurringJobManager>();
await AnimeSyncJob.ScheduleAsync(recurringJobManager, app.Services);
MalTokenRefreshJob.Schedule(recurringJobManager);

app.UseCors();
app.UseResponseCaching();
app.UseAuthentication();
app.UseAuthorization();

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
