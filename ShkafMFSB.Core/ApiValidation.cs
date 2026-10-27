using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ShkafMFSB;

/// <summary>Ошибка валидации входных данных API. Ловится в WebServer -> 400 Bad Request.</summary>
public sealed class ValidationException : Exception
{
    public ValidationException(string message) : base(message) { }
}

/// <summary>Хелперы валидации JSON-полей. Бросают ValidationException.</summary>
public static class V
{
    public static string Str(JsonElement body, string name, int min = 1, int max = 200)
    {
        if (!body.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.String)
            throw new ValidationException($"Поле '{name}' обязательно и должно быть строкой");
        var s = el.GetString() ?? "";
        if (s.Trim().Length < min)
            throw new ValidationException($"Поле '{name}' не может быть короче {min} символов");
        if (s.Length > max)
            throw new ValidationException($"Поле '{name}' не может быть длиннее {max} символов");
        return s.Trim();
    }

    public static int Number(JsonElement body, string name, int min = int.MinValue, int max = int.MaxValue)
    {
        if (!body.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.Number)
            throw new ValidationException($"Поле '{name}' обязательно и должно быть числом");
        if (!el.TryGetInt32(out var v))
            throw new ValidationException($"Поле '{name}' должно быть 32-битным целым");
        if (v < min || v > max)
            throw new ValidationException($"Поле '{name}' должно быть в диапазоне [{min}..{max}]");
        return v;
    }

    public static int? NullableNumber(JsonElement body, string name, int min = int.MinValue, int max = int.MaxValue)
    {
        if (!body.TryGetProperty(name, out var el) || el.ValueKind == JsonValueKind.Null)
            return null;
        if (el.ValueKind != JsonValueKind.Number || !el.TryGetInt32(out var v))
            throw new ValidationException($"Поле '{name}' должно быть числом или null");
        if (v < min || v > max)
            throw new ValidationException($"Поле '{name}' должно быть в диапазоне [{min}..{max}]");
        return v;
    }

    public static bool Bool(JsonElement body, string name)
    {
        if (!body.TryGetProperty(name, out var el))
            throw new ValidationException($"Поле '{name}' обязательно");
        if (el.ValueKind == JsonValueKind.True) return true;
        if (el.ValueKind == JsonValueKind.False) return false;
        throw new ValidationException($"Поле '{name}' должно быть bool");
    }
}

/// <summary>Чтение JSON-объекта из потока (не зависит от HttpListener — тестируется напрямую).</summary>
public static class JsonBodyReader
{
    public static async Task<JsonElement> ReadObjectAsync(Stream input, Encoding? encoding)
    {
        using var reader = new StreamReader(input, encoding ?? Encoding.UTF8);
        var text = await reader.ReadToEndAsync();
        if (string.IsNullOrWhiteSpace(text))
            throw new ValidationException("Тело запроса пустое");
        try
        {
            var el = JsonSerializer.Deserialize<JsonElement>(text);
            if (el.ValueKind != JsonValueKind.Object)
                throw new ValidationException("Тело запроса должно быть JSON-объектом");
            return el;
        }
        catch (JsonException jex)
        {
            throw new ValidationException("Некорректный JSON: " + jex.Message);
        }
    }
}
