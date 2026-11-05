using System.ComponentModel.DataAnnotations;

namespace TradeSnapshot.Api.Contracts;

/// <summary>
/// Decimal range check that is independent of the server's culture.
/// <c>[Range(typeof(decimal), "0.000001", ...)]</c> parses its limits with the current culture by default,
/// so on a host whose decimal separator is a comma (e.g. en-ZA, de-DE) every request fails with
/// "0.000001 is not a valid value for Decimal". Limits and values are parsed in the invariant culture here.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class DecimalRangeAttribute : RangeAttribute
{
    public DecimalRangeAttribute(string minimum, string maximum) : base(typeof(decimal), minimum, maximum)
    {
        ParseLimitsInInvariantCulture = true;
        ConvertValueInInvariantCulture = true;
    }
}
