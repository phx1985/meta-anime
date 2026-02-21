using Hangfire;
using Hangfire.PostgreSql;
using MetaAnime.Application.BackgroundJobs;
using MetaAnime.Core.Settings;
using MetaAnime.Utilities.Extensions;
using MetaAnime.Utilities.Hangfire;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddResponseCaching();
builder.Services.AddControllers();

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
app.Services.MigrateDatabase();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapDefaultEndpoints();

// Configure Hangfire Dashboard
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new HangfireAuthorizationFilter() }
});

// Schedule background jobs
AnimeSyncJob.Schedule();
MalTokenRefreshJob.Schedule();

app.UseHttpsRedirection();
app.UseResponseCaching();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
