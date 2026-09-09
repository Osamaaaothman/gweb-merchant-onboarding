using System.Globalization;
using Amazon.DynamoDBv2.Model;
using Gweb.Domain.Applications;

namespace Gweb.Adapters.Persistence;

/// <summary>
/// Shared helpers for mapping optional domain values to/from DynamoDB AttributeValues.
/// DynamoDB items omit the attribute entirely for an unset field rather than storing
/// an explicit NULL -- smaller items, and "TryGetValue" reads naturally as "was this
/// ever set".
/// </summary>
internal static class DynamoDbItemMapping
{
    public static void PutIfNotNull(this Dictionary<string, AttributeValue> item, string key, string? value)
    {
        if (value is not null)
        {
            item[key] = new AttributeValue { S = value };
        }
    }

    public static void PutIfNotNull(this Dictionary<string, AttributeValue> item, string key, decimal? value)
    {
        if (value is not null)
        {
            item[key] = new AttributeValue { N = value.Value.ToString(CultureInfo.InvariantCulture) };
        }
    }

    public static void PutIfNotNull(this Dictionary<string, AttributeValue> item, string key, int? value)
    {
        if (value is not null)
        {
            item[key] = new AttributeValue { N = value.Value.ToString(CultureInfo.InvariantCulture) };
        }
    }

    public static void PutIfNotNull(this Dictionary<string, AttributeValue> item, string key, DateOnly? value)
    {
        if (value is not null)
        {
            item[key] = new AttributeValue { S = value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
        }
    }

    public static void PutIfNotNull(this Dictionary<string, AttributeValue> item, string key, DateTimeOffset? value)
    {
        if (value is not null)
        {
            item[key] = new AttributeValue { S = value.Value.ToString("O", CultureInfo.InvariantCulture) };
        }
    }

    public static void PutIfNotNull<TEnum>(this Dictionary<string, AttributeValue> item, string key, TEnum? value) where TEnum : struct, Enum
    {
        if (value is not null)
        {
            item[key] = new AttributeValue { S = value.Value.ToString() };
        }
    }

    public static string? GetOptionalString(this Dictionary<string, AttributeValue> item, string key) =>
        item.TryGetValue(key, out var value) ? value.S : null;

    public static decimal? GetOptionalDecimal(this Dictionary<string, AttributeValue> item, string key) =>
        item.TryGetValue(key, out var value) ? decimal.Parse(value.N, CultureInfo.InvariantCulture) : null;

    public static int? GetOptionalInt(this Dictionary<string, AttributeValue> item, string key) =>
        item.TryGetValue(key, out var value) ? int.Parse(value.N, CultureInfo.InvariantCulture) : null;

    public static DateOnly? GetOptionalDate(this Dictionary<string, AttributeValue> item, string key) =>
        item.TryGetValue(key, out var value) ? DateOnly.Parse(value.S, CultureInfo.InvariantCulture) : null;

    public static DateTimeOffset? GetOptionalTimestamp(this Dictionary<string, AttributeValue> item, string key) =>
        item.TryGetValue(key, out var value) ? DateTimeOffset.Parse(value.S, CultureInfo.InvariantCulture) : null;

    public static TEnum? GetOptionalEnum<TEnum>(this Dictionary<string, AttributeValue> item, string key) where TEnum : struct, Enum =>
        item.TryGetValue(key, out var value) ? Enum.Parse<TEnum>(value.S) : null;

    public static Address? GetOptionalAddress(this Dictionary<string, AttributeValue> item, string key)
    {
        if (!item.TryGetValue(key, out var value) || value.M is null)
        {
            return null;
        }
        var map = value.M;
        return new Address(
            map["line1"].S,
            map.TryGetValue("line2", out var line2) ? line2.S : null,
            map["city"].S,
            map["state"].S,
            map["postalCode"].S,
            map["country"].S);
    }

    public static void PutIfNotNull(this Dictionary<string, AttributeValue> item, string key, Address? value)
    {
        if (value is null)
        {
            return;
        }
        var map = new Dictionary<string, AttributeValue>
        {
            ["line1"] = new AttributeValue { S = value.Line1 },
            ["city"] = new AttributeValue { S = value.City },
            ["state"] = new AttributeValue { S = value.State },
            ["postalCode"] = new AttributeValue { S = value.PostalCode },
            ["country"] = new AttributeValue { S = value.Country },
        };
        if (value.Line2 is not null)
        {
            map["line2"] = new AttributeValue { S = value.Line2 };
        }
        item[key] = new AttributeValue { M = map };
    }
}
