using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AdaptAula.Infrastructure.Persistence;

/// <summary>SQLite has no native array/JSON column type usable through simple EF mapping, so
/// every List/Dictionary property on an entity is stored as a JSON string column. Kept in one
/// place so entity configs stay short.</summary>
internal static class JsonValueConverters
{
    private static readonly JsonSerializerOptions Options = new();

    public static ValueConverter<List<string>, string> ForStringList() => new(
        v => JsonSerializer.Serialize(v, Options),
        v => JsonSerializer.Deserialize<List<string>>(v, Options) ?? new List<string>());

    public static ValueComparer<List<string>> StringListComparer() => new(
        (a, b) => (a ?? new()).SequenceEqual(b ?? new()),
        v => v.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode())),
        v => v.ToList());

    public static ValueConverter<T, string> ForJson<T>() where T : class, new() => new(
        v => JsonSerializer.Serialize(v, Options),
        v => JsonSerializer.Deserialize<T>(v, Options) ?? new T());

    public static ValueComparer<T> JsonComparer<T>() where T : class => new(
        (a, b) => JsonSerializer.Serialize(a, Options) == JsonSerializer.Serialize(b, Options),
        v => JsonSerializer.Serialize(v, Options).GetHashCode(),
        v => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v, Options), Options)!);
}
