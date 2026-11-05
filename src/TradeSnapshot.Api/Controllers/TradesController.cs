using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TradeSnapshot.Api.Contracts;
using TradeSnapshot.Api.Ingestion;
using TradeSnapshot.Domain.Ingestion;
using TradeSnapshot.Infrastructure.Persistence;

namespace TradeSnapshot.Api.Controllers;

[ApiController]
[Route("api/v1/trades")]
[Produces("application/json")]
public sealed class TradesController(TradeIngestionService ingestion, TradeSnapshotDbContext db) : ControllerBase
{
    /// <summary>Submit a trade event. Idempotent on (external_ref, as_of): safe for upstream to retry.</summary>
    /// <response code="201">New trade created (version 1).</response>
    /// <response code="200">Correction applied, or the message was a duplicate/stale re-send and nothing changed (see <c>outcome</c>).</response>
    /// <response code="400">Malformed request.</response>
    /// <response code="409">Message contradicts what is already held and needs manual review (see <c>reason</c>).</response>
    /// <response code="422">Unknown account/instrument, or a correction tried to change an immutable field.</response>
    [HttpPost]
    [ProducesResponseType<TradeIngestionResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<TradeIngestionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Submit([FromBody] TradeSubmissionRequest request, CancellationToken ct)
    {
        var submission = new TradeSubmission(
            ExternalRef: request.ExternalRef!.Trim(),
            AccountCode: request.AccountId!.Trim().ToUpperInvariant(),
            Isin: NullIfBlank(request.Instrument!.Isin)?.ToUpperInvariant(),
            Symbol: NullIfBlank(request.Instrument.Symbol)?.ToUpperInvariant(),
            Side: request.Side!.Value,
            Quantity: request.Quantity!.Value,
            Price: request.Price!.Value,
            TradeDate: request.TradeDate!.Value,
            AsOfUtc: request.AsOf!.Value.UtcDateTime,
            CorrelationId: HttpContext.TraceIdentifier);

        var result = await ingestion.IngestAsync(submission, ct);
        var body = new TradeIngestionResponse(result.ExternalRef, result.Outcome, result.Reason, result.TradeId, result.Version,
            result.CurrentVersion, result.AsOfUtc, result.Mode, result.ShadowOutcome);

        return result.Outcome switch
        {
            IngestionOutcome.Created => CreatedAtAction(nameof(Get), new { externalRef = result.ExternalRef }, body),
            IngestionOutcome.Conflict => NotApplied(StatusCodes.Status409Conflict, "Trade message conflicts with recorded state", body),
            IngestionOutcome.Rejected => NotApplied(StatusCodes.Status422UnprocessableEntity, "Trade message rejected", body),
            _ => Ok(body),
        };
    }

    /// <summary>Current state of a trade plus its full version history and every message received for it.</summary>
    [HttpGet("{externalRef}")]
    [ProducesResponseType<TradeDetailsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string externalRef, CancellationToken ct)
    {
        var trade = await db.Trades.AsNoTracking()
            .Where(t => t.ExternalRef == externalRef)
            .Select(t => new
            {
                t.Id, t.ExternalRef, t.CurrentVersion,
                AccountCode = t.Account.AccountCode, t.Instrument.Isin, t.Instrument.Symbol,
                Versions = t.Versions.OrderByDescending(v => v.Version).ToList(),
            })
            .SingleOrDefaultAsync(ct);

        if (trade is null)
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Trade not found", detail: $"No trade with external_ref '{externalRef}'.");

        var events = await db.TradeIngestionLog.AsNoTracking()
            .Where(e => e.ExternalRef == externalRef)
            .OrderByDescending(e => e.ReceivedAtUtc).ThenByDescending(e => e.Id)
            .Take(100)
            .Select(e => new IngestionEventResponse(e.ExternalRef, e.AsOfUtc, e.ReceivedAtUtc, e.Outcome, e.Reason, e.Mode, e.ShadowOutcome, e.Version, e.CorrelationId))
            .ToListAsync(ct);

        return Ok(new TradeDetailsResponse(
            trade.Id, trade.ExternalRef, trade.AccountCode, trade.Isin, trade.Symbol, trade.CurrentVersion,
            trade.Versions.Select(v => new TradeVersionResponse(v.Version, v.Side, v.Quantity, v.Price, v.TradeDate, v.AsOfUtc, v.ReceivedAtUtc, v.Version == trade.CurrentVersion)).ToList(),
            events));
    }

    private ObjectResult NotApplied(int statusCode, string title, TradeIngestionResponse body)
    {
        var problem = ProblemDetailsFactory.CreateProblemDetails(HttpContext, statusCode, title,
            detail: $"external_ref '{body.ExternalRef}' was not applied: {body.Reason}.");
        problem.Extensions["outcome"] = body.Outcome;
        problem.Extensions["reason"] = body.Reason;
        problem.Extensions["current_version"] = body.CurrentVersion;
        problem.Extensions["mode"] = body.Mode;
        return new ObjectResult(problem) { StatusCode = statusCode, ContentTypes = { "application/problem+json" } };
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
