using System.Text.Json;

namespace TempooAnalytics.Api.Data;

/// <summary>Stores a list of strings as a JSON array (jsonb columns).</summary>
public static class StringListJson
{
    public static readonly Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<List<string>, string> Converter = new(
        v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
        v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>());

    public static readonly Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<string>> Comparer = new(
        (a, b) => (a == null && b == null) || (a != null && b != null && a.SequenceEqual(b)),
        v => v.Aggregate(0, (hash, s) => HashCode.Combine(hash, s.GetHashCode())),
        v => v.ToList());
}
