using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using TradeSnapshot.Domain.Entities;

namespace TradeSnapshot.Domain.Ingestion;

/// <summary>
/// Deterministic hash of a trade's business content. Decimals are normalised (120 == 120.000) so that
/// formatting differences in upstream payloads do not look like changes.
/// </summary>
public static class TradeFingerprint
{
    public static byte[] Compute(int accountId, int instrumentId, TradeSide side, decimal quantity, decimal price, DateOnly tradeDate)
    {
        var canonical = string.Join('|',
            accountId.ToString(CultureInfo.InvariantCulture),
            instrumentId.ToString(CultureInfo.InvariantCulture),
            side.ToString(),
            Normalise(quantity),
            Normalise(price),
            tradeDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        return SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
    }

    private static string Normalise(decimal value) =>
        value.ToString("0.############################", CultureInfo.InvariantCulture);
}
