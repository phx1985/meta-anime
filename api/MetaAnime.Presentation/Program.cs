using Hangfire;
using Hangfire.PostgreSql;
using MetaAnime.Application.BackgroundJobs;
using MetaAnime.Core.Settings;
using MetaAnime.Hubs;
using MetaAnime.Utilities.Extensions;
using MetaAnime.Utilities.Hangfire;

var builder = WebApplication.CreateBuilder(args);

// Read container-generated secrets (written by the s6 init script on first boot).
// This file is optional so the app still starts in development without it.
builder.Configuration.AddJsonFile("/config/appsettings.json", optional: true, reloadOnChange: false);

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

// Add API versioning
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new Asp.Versioning.ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
}).AddApiExplorer(options =>
{
    options.GroupNameFormat = "'v'VVV";
    options.SubstituteApiVersionInUrl = true;
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

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGenWithApiKey();
builder.Services.AddCustomServices(builder.Configuration);
builder.Services.SetupDatabase(builder.Configuration);
builder.Services.SetupAuthentication();
var app = builder.Build();
await app.Services.MigrateDatabaseAsync();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapDefaultEndpoints();

// Schedule background jobs
var recurringJobManager = app.Services.GetRequiredService<IRecurringJobManager>();
await AnimeSyncJob.ScheduleAsync(recurringJobManager, app.Services);
MalTokenRefreshJob.Schedule(recurringJobManager);

app.UseHttpsRedirection();
app.UseCors();
app.UseResponseCaching();
app.UseAuthentication();
app.UseAuthorization();

// Serve Angular static files from wwwroot (populated during Docker build)
app.UseDefaultFiles();
app.UseStaticFiles();

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
