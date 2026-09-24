using System.Collections;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HappyPhoton.Models;

// Elements must also be immutable when sharing this collection between model snapshots.
[CollectionBuilder(typeof(ValueArray), nameof(ValueArray.Create))]
[JsonConverter(typeof(ValueArray.ConverterFactory))]
public sealed class ValueArray<T> : IReadOnlyList<T>, IEquatable<ValueArray<T>>
{
    private readonly T[] _items;
    public ValueArray(ReadOnlySpan<T> items) => _items = items.ToArray();
    public int Length => _items.Length;
    public int Count => _items.Length;
    public T this[int index] => _items[index];
    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public bool Equals(ValueArray<T>? other) => other != null && _items.SequenceEqual(other._items);
    public override bool Equals(object? obj) => obj is ValueArray<T> other && Equals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var item in _items) hash.Add(item);
        return hash.ToHashCode();
    }
    public static implicit operator ValueArray<T>(T[] items) => new(items);
}

public static class ValueArray
{
    public static ValueArray<T> Create<T>(ReadOnlySpan<T> items) => new(items);

    public sealed class ConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type type) => type.IsGenericType &&
            type.GetGenericTypeDefinition() == typeof(ValueArray<>);
        public override JsonConverter CreateConverter(Type type, JsonSerializerOptions options) =>
            (JsonConverter)Activator.CreateInstance(typeof(Converter<>).MakeGenericType(type.GetGenericArguments()))!;
    }

    private sealed class Converter<T> : JsonConverter<ValueArray<T>>
    {
        public override ValueArray<T>? Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
            JsonSerializer.Deserialize<T[]>(ref reader, options) is { } items ? new(items) : null;
        public override void Write(Utf8JsonWriter writer, ValueArray<T> value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (var item in value) JsonSerializer.Serialize(writer, item, options);
            writer.WriteEndArray();
        }
    }
}
