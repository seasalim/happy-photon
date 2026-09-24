using System.Text.Json;
using HappyPhoton.Models;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ValueArrayTests
{
    [Fact]
    public void CopiesInputAndExposesOnlyReadOnlyAccess()
    {
        var input = new[] { 1, 2, 3 };
        ValueArray<int> values = input;
        input[0] = 9;
        var exported = values.ToArray(); exported[1] = 9;
        IReadOnlyList<int> readOnly = values;
        Assert.Equal(3, readOnly.Count);
        Assert.Equal(1, readOnly[0]);
        Assert.Equal(new[] { 1, 2, 3 }, readOnly);
        Assert.False(typeof(IList<int>).IsAssignableFrom(values.GetType()));
        Assert.False(values.GetType().GetProperty("Item")!.CanWrite);
    }

    [Fact]
    public void EqualityAndHashUseOrderedValues()
    {
        ValueArray<int> first = [1, 2, 3], equal = [1, 2, 3], reordered = [3, 2, 1];
        Assert.True(first.Equals(equal));
        Assert.True(first.Equals((object)equal));
        Assert.Equal(first.GetHashCode(), equal.GetHashCode());
        Assert.False(first.Equals(reordered));
        Assert.False(first.Equals((ValueArray<int>?)null));
        Assert.False(first.Equals((object)new[] { 1, 2, 3 }));
        Assert.True(((ValueArray<int>)[]).Equals([]));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[1,2,3]")]
    public void JsonIsAPlainArray(string json)
    {
        var values = JsonSerializer.Deserialize<ValueArray<int>>(json)!;
        Assert.Equal(json, JsonSerializer.Serialize(values));
        Assert.Equal(JsonSerializer.Deserialize<int[]>(json)!, values);
        Assert.Null(JsonSerializer.Deserialize<ValueArray<int>>("null"));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ValueArray<int>>("{}"));
    }
}
