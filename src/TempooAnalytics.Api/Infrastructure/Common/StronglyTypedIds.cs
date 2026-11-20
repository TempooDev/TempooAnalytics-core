using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TempooAnalytics.Api.Infrastructure.Common;

[TypeConverter(typeof(StronglyTypedIdConverter<UserId>))]
[JsonConverter(typeof(StronglyTypedIdJsonConverter<UserId>))]
public readonly record struct UserId(Guid Value) 
{
    public static UserId Empty => new(Guid.Empty);
    public static UserId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
    public static bool TryParse(string? s, out UserId result)
    {
        if (Guid.TryParse(s, out var guid)) { result = new UserId(guid); return true; }
        result = Empty; return false;
    }
}

[TypeConverter(typeof(StronglyTypedIdConverter<SiteId>))]
[JsonConverter(typeof(StronglyTypedIdJsonConverter<SiteId>))]
public readonly record struct SiteId(Guid Value)
{
    public static SiteId Empty => new(Guid.Empty);
    public static SiteId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
    public static bool TryParse(string? s, out SiteId result)
    {
        if (Guid.TryParse(s, out var guid)) { result = new SiteId(guid); return true; }
        result = Empty; return false;
    }
}

[TypeConverter(typeof(StronglyTypedIdConverter<EventId>))]
[JsonConverter(typeof(StronglyTypedIdJsonConverter<EventId>))]
public readonly record struct EventId(Guid Value)
{
    public static EventId Empty => new(Guid.Empty);
    public static EventId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
    public static bool TryParse(string? s, out EventId result)
    {
        if (Guid.TryParse(s, out var guid)) { result = new EventId(guid); return true; }
        result = Empty; return false;
    }
}

public class StronglyTypedIdConverter<T> : TypeConverter where T : struct
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);
    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is string s)
        {
            var method = typeof(T).GetMethod("TryParse", [typeof(string), typeof(T).MakeByRefType()]);
            if (method != null)
            {
                var args = new object?[] { s, null };
                if ((bool)method.Invoke(null, args)!) return args[1];
            }
        }
        return base.ConvertFrom(context, culture, value);
    }
}

public class StronglyTypedIdJsonConverter<T> : JsonConverter<T> where T : struct
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString();
        var method = typeof(T).GetMethod("TryParse", [typeof(string), typeof(T).MakeByRefType()]);
        if (method != null)
        {
            var args = new object?[] { s, null };
            if ((bool)method.Invoke(null, args)!) return (T)args[1]!;
        }
        return default;
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
