using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TradeSnapshot.Api;
using TradeSnapshot.Api.Ingestion;
using TradeSnapshot.Api.Observability;
using TradeSnapshot.Infrastructure;
using TradeSnapshot.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// ---- Configuration-bound options ----
builder.Services.AddOptions<CorrectionHandlingOptions>()
    .Bind(builder.Configuration.GetSection(CorrectionHandlingOptions.SectionName))
    .ValidateOnStart();

// ---- Persistence ----
var connectionString = builder.Configuration.GetConnectionString("TradeSnapshot")
    ?? throw new InvalidOperationException("Connection string 'TradeSnapshot' is not configured.");
builder.Services.AddTradeSnapshotPersistence(connectionString);

// ---- Application services ----
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ICorrectionHandlingGate, CorrectionHandlingGate>();
builder.Services.AddSingleton<IngestionMetrics>();
builder.Services.AddScoped<TradeIngestionService>();

// ---- HTTP surface ----
builder.Services
    .AddControllers(options =>
    {
        // Validation error keys use the JSON (snake_case) names the caller actually sent.
        options.ModelMetadataDetailsProviders.Add(new SystemTextJsonValidationMetadataProvider(JsonNamingPolicy.SnakeCaseLower));
    })
    .AddJsonOptions(options => JsonDefaults.Apply(options.JsonSerializerOptions));

builder.Services.AddProblemDetails(options =>
    options.CustomizeProblemDetails = ctx => ctx.ProblemDetails.Extensions["correlation_id"] = ctx.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<TradeSnapshotDbContext>("database", tags: ["ready"]);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    var xml = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
    if (File.Exists(xml)) options.IncludeXmlComments(xml);
});

var app = builder.Build();

// ---- Startup tasks ----
if (app.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
{
    // Convenient for local/dev and tests. In production, apply the idempotent script (sql/schema.sql) or
    // `dotnet ef database update` from the deployment pipeline instead, so schema changes are reviewed and gated.
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<TradeSnapshotDbContext>().Database.MigrateAsync();
}

LogCorrectionHandling(app);
app.Services.GetRequiredService<IOptionsMonitor<CorrectionHandlingOptions>>().OnChange(_ => LogCorrectionHandling(app));

// ---- Pipeline ----
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.MapHealthChecks("/health/live", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new() { Predicate = check => check.Tags.Contains("ready") });

app.Run();

static void LogCorrectionHandling(WebApplication app)
{
    var options = app.Services.GetRequiredService<IOptionsMonitor<CorrectionHandlingOptions>>().CurrentValue;
    Log.CorrectionHandlingConfigured(app.Logger, options.Mode, string.Join(", ", options.PilotAccounts));
}

/// <summary>Exposed for WebApplicationFactory in integration tests.</summary>
public partial class Program;
