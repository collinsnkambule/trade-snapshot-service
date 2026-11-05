using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore.Storage;

namespace TradeSnapshot.Api.Observability;

/// <summary>
/// Last-resort handler: logs once with full context and returns RFC 7807 ProblemDetails without leaking internals.
/// Database exhaustion after EF's retries maps to 503 + Retry-After so upstream senders back off and re-send
/// (safe, because ingestion is idempotent).
/// </summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var request = httpContext.Request;
        ProblemDetails problem;

        if (exception is RetryLimitExceededException)
        {
            Log.DatabaseUnavailable(logger, exception, request.Method, request.Path);
            httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            httpContext.Response.Headers.RetryAfter = "30";
            problem = new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Service temporarily unavailable",
                Detail = "The database could not be reached. The request was not applied and is safe to retry.",
            };
        }
        else
        {
            Log.UnhandledException(logger, exception, request.Method, request.Path);
            httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
            problem = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred",
                Detail = "The error has been logged. Quote the correlation id when reporting it.",
            };
        }

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }
}
