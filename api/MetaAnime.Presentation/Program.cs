using MetaAnime.Utilities.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.


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

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
