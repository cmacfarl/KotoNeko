using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KotoNeko.Data;

/// <summary>Stores a small list of records as one JSON text column.</summary>
internal static class JsonListConversion
{
    private static readonly JsonSerializerOptions Options = new() {
        // Keep Japanese readable in the column rather than \u-escaped.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static PropertyBuilder<List<T>> AsJsonList<T>(this PropertyBuilder<List<T>> property) =>
        property.HasConversion(
            list => JsonSerializer.Serialize(list, Options),
            json => string.IsNullOrEmpty(json) ? new List<T>() : JsonSerializer.Deserialize<List<T>>(json, Options) ?? new List<T>(),
            new ValueComparer<List<T>>(
                (a, b) => JsonSerializer.Serialize(a, Options) == JsonSerializer.Serialize(b, Options),
                list => JsonSerializer.Serialize(list, Options).GetHashCode(),
                list => JsonSerializer.Deserialize<List<T>>(JsonSerializer.Serialize(list, Options), Options)!));
}
