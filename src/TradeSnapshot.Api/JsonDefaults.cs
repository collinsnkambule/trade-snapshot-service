using System.Text.Json;
using System.Text.Json.Serialization;

namespace TradeSnapshot.Api;

/// <summary>Wire format: snake_case properties and UPPER_SNAKE enums (matches the upstream payload examples).</summary>
public static class JsonDefaults
{
    public static void Apply(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
        options.DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower;
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper, allowIntegerValues: false));
        options.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
    }

    public static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Apply(options);
        return options;
    }
}
