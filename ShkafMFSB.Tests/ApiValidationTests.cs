using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ShkafMFSB;
using Xunit;

namespace ShkafMFSB.Tests;

public class ApiValidationTests
{
    private static JsonElement Json(string json) => JsonSerializer.Deserialize<JsonElement>(json);

    // ---------- V.Str ----------
    [Fact]
    public void Str_ReturnsTrimmedValue()
    {
        var b = Json("{\"name\":\"  hello  \"}");
        Assert.Equal("hello", V.Str(b, "name"));
    }

    [Fact]
    public void Str_Missing_Throws()
        => Assert.Throws<ValidationException>(() => V.Str(Json("{}"), "name"));

    [Fact]
    public void Str_Empty_Throws()
        => Assert.Throws<ValidationException>(() => V.Str(Json("{\"name\":\"   \"}"), "name"));

    [Fact]
    public void Str_TooShort_Throws()
        => Assert.Throws<ValidationException>(() => V.Str(Json("{\"pin\":\"12\"}"), "pin", 4, 128));

    [Fact]
    public void Str_TooLong_Throws()
    {
        var longName = new string('a', 201);
        Assert.Throws<ValidationException>(() => V.Str(Json("{\"name\":\"" + longName + "\"}"), "name", 1, 200));
    }

    [Fact]
    public void Str_WrongType_Throws()
        => Assert.Throws<ValidationException>(() => V.Str(Json("{\"name\":123}"), "name"));

    // ---------- V.Int ----------
    [Fact]
    public void Int_ReturnsValue()
        => Assert.Equal(42, V.Number(Json("{\"id\":42}"), "id", 1));

    [Fact]
    public void Int_BelowMin_Throws()
        => Assert.Throws<ValidationException>(() => V.Number(Json("{\"id\":0}"), "id", 1));

    [Fact]
    public void Int_WrongType_Throws()
        => Assert.Throws<ValidationException>(() => V.Number(Json("{\"id\":\"42\"}"), "id"));

    [Fact]
    public void Int_Missing_Throws()
        => Assert.Throws<ValidationException>(() => V.Number(Json("{}"), "id"));

    // ---------- V.NullableInt ----------
    [Fact]
    public void NullableInt_Null_ReturnsNull()
        => Assert.Null(V.NullableNumber(Json("{\"id\":null}"), "id"));

    [Fact]
    public void NullableInt_Missing_ReturnsNull()
        => Assert.Null(V.NullableNumber(Json("{}"), "id"));

    [Fact]
    public void NullableInt_Value_ReturnsValue()
        => Assert.Equal(5, V.NullableNumber(Json("{\"id\":5}"), "id", 1));

    // ---------- V.Bool ----------
    [Fact]
    public void Bool_True_ReturnsTrue()
        => Assert.True(V.Bool(Json("{\"f\":true}"), "f"));

    [Fact]
    public void Bool_False_ReturnsFalse()
        => Assert.False(V.Bool(Json("{\"f\":false}"), "f"));

    [Fact]
    public void Bool_WrongType_Throws()
        => Assert.Throws<ValidationException>(() => V.Bool(Json("{\"f\":\"yes\"}"), "f"));

    // ---------- JsonBodyReader ----------
    [Fact]
    public async Task ReadObject_Valid_ReturnsElement()
    {
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes("{\"a\":1}"));
        var el = await JsonBodyReader.ReadObjectAsync(ms, Encoding.UTF8);
        Assert.Equal(JsonValueKind.Object, el.ValueKind);
    }

    [Fact]
    public async Task ReadObject_Empty_Throws()
    {
        using var ms = new MemoryStream(Array.Empty<byte>());
        await Assert.ThrowsAsync<ValidationException>(() => JsonBodyReader.ReadObjectAsync(ms, Encoding.UTF8));
    }

    [Fact]
    public async Task ReadObject_InvalidJson_Throws()
    {
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes("{not json"));
        await Assert.ThrowsAsync<ValidationException>(() => JsonBodyReader.ReadObjectAsync(ms, Encoding.UTF8));
    }

    [Fact]
    public async Task ReadObject_Array_Throws()
    {
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes("[1,2,3]"));
        await Assert.ThrowsAsync<ValidationException>(() => JsonBodyReader.ReadObjectAsync(ms, Encoding.UTF8));
    }
}
