using System.Diagnostics;
using System.Text.RegularExpressions;

namespace TradeSnapshot.Api.Observability;

/// <summary>
/// Accepts (or creates) an X-Correlation-ID, echoes it on the response, stores it as the request's
/// TraceIdentifier (so ProblemDetails and persisted audit rows carry it) and opens a logging scope so every log
/// line for the request, including EF and framework logs, is tagged with it. Also emits one summary line per
/// request with status and latency.
/// </summary>
public sealed partial class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-ID";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[HeaderName].ToString();
        if (!IsSafe(correlationId))
            correlationId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");

        context.TraceIdentifier = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using var scope = logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId });
        var start = Stopwatch.GetTimestamp();
        try
        {
            await next(context);
        }
        finally
        {
            Log.RequestCompleted(logger, context.Request.Method, context.Request.Path, context.Response.StatusCode,
                Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }

    // Caller-supplied values end up in logs and the database: restrict them to prevent log forging / bloat.
    private static bool IsSafe(string value) => !string.IsNullOrEmpty(value) && SafeId().IsMatch(value);

    [GeneratedRegex("^[A-Za-z0-9._:-]{1,64}$")]
    private static partial Regex SafeId();
}
