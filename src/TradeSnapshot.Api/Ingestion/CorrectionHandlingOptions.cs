using Microsoft.Extensions.Options;
using TradeSnapshot.Domain.Ingestion;

namespace TradeSnapshot.Api.Ingestion;

/// <summary>
/// Config section "TradeIngestion:CorrectionHandling". Read through IOptionsMonitor, so a change in any reloadable
/// configuration source (appsettings on disk, Azure App Configuration with refresh, etc.) takes effect on the next
/// request without a restart or deploy.
/// </summary>
public sealed class CorrectionHandlingOptions
{
    public const string SectionName = "TradeIngestion:CorrectionHandling";

    /// <summary>Global switch. Disabled is the kill switch and always wins.</summary>
    public CorrectionHandlingMode Mode { get; set; } = CorrectionHandlingMode.Disabled;

    /// <summary>
    /// Optional canary list. When non-empty and <see cref="Mode"/> is Enabled, only these accounts get the full
    /// behaviour; every other account runs in Shadow, so its decisions are still evaluated and logged.
    /// </summary>
    public List<string> PilotAccounts { get; set; } = new();
}

public interface ICorrectionHandlingGate
{
    CorrectionHandlingMode ResolveFor(string accountCode);
}

public sealed class CorrectionHandlingGate(IOptionsMonitor<CorrectionHandlingOptions> options) : ICorrectionHandlingGate
{
    public CorrectionHandlingMode ResolveFor(string accountCode)
    {
        var current = options.CurrentValue;
        return current.Mode switch
        {
            CorrectionHandlingMode.Enabled when current.PilotAccounts.Count > 0
                && !current.PilotAccounts.Contains(accountCode, StringComparer.OrdinalIgnoreCase) => CorrectionHandlingMode.Shadow,
            var mode => mode,
        };
    }
}
