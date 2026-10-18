using MusicHub.Api.Identification;
using MusicHub.Api.Ingestion;
using MusicHub.Api.Storage;
using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MusicHub.Api.Data;
using MusicHub.Api.Metadata;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddFileStorage(builder.Configuration);
builder.Services.Configure<FileIngestionOptions>(
    builder.Configuration.GetSection(FileIngestionOptions.SectionName));
builder.Services.AddScoped<FileIngestionService>();

// The connection string is built per context, from the Database__* configuration. Nothing connects at startup.
builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection(DatabaseOptions.SectionName));
builder.Services.AddDbContext<MusicHubDbContext>((sp, options) =>
    options.UseNpgsql(sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<DatabaseOptions>>().Value.BuildConnectionString()));

builder.Services.AddAudioMetadata();
builder.Services.AddIdentification(builder.Configuration);

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

var app = builder.Build();

// Migration step: `--migrate` applies pending migrations and exits. Normal startup never migrates.
if (args.Length > 0 && args[0] == "--migrate")
{
    try
    {
        using var scope = app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<MusicHubDbContext>().Database.MigrateAsync();
        Console.WriteLine("Migrations applied.");
        return 0;
    }
    catch (Exception ex)
    {
        // The exception text can hold connection details, so only its type is reported.
        Console.Error.WriteLine($"Migration failed ({ex.GetType().Name}). Check the Database__* settings and that the database is reachable.");
        return 1;
    }
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Serve the built UI (copied into wwwroot by the Dockerfile).
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthorization();

// Health: /health/live runs no checks (liveness and restart), /health/ready runs the database check, /health runs all.
var healthStatusCodes = new Dictionary<HealthStatus, int>
{
    [HealthStatus.Healthy] = StatusCodes.Status200OK,
    [HealthStatus.Degraded] = StatusCodes.Status200OK,
    [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
};

static Task WriteHealth(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json";
    return context.Response.WriteAsync(JsonSerializer.Serialize(new
    {
        status = report.Status.ToString(),
        checks = report.Entries.Select(e => new { name = e.Key, status = e.Value.Status.ToString() }),
    }, JsonSerializerOptions.Web));
}

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResultStatusCodes = healthStatusCodes,
    ResponseWriter = WriteHealth,
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResultStatusCodes = healthStatusCodes,
    ResponseWriter = WriteHealth,
});
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResultStatusCodes = healthStatusCodes,
    ResponseWriter = WriteHealth,
});

app.MapControllers();

// Unknown /api routes return 404; everything else goes to the SPA so client-side routing works.
app.MapFallback("/api/{**path}", () => Results.NotFound());
app.MapFallbackToFile("index.html");

app.Run();
return 0;
