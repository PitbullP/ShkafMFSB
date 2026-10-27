using System.IO;
using System.Text.RegularExpressions;

namespace ShkafMFSB;

public static partial class Logger
{
    private static readonly string LogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.log");
    private static readonly object LockObj = new();

    [GeneratedRegex(@"""pin""\s*:\s*""[^""]*""", RegexOptions.IgnoreCase)]
    private static partial Regex JsonPinRegex();

    [GeneratedRegex(@"""password""\s*:\s*""[^""]*""", RegexOptions.IgnoreCase)]
    private static partial Regex JsonPasswordRegex();

    [GeneratedRegex(@"""token""\s*:\s*""[^""]*""", RegexOptions.IgnoreCase)]
    private static partial Regex JsonTokenRegex();

    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9_\-\.]+", RegexOptions.IgnoreCase)]
    private static partial Regex BearerRegex();

    public static void Log(string message)
    {
        try
        {
            lock (LockObj)
            {
                string cleanMessage = Redact(message).Replace("\r\n", " ").Replace("\n", " ");
                File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {cleanMessage}{Environment.NewLine}");
            }
        }
        catch { /* Игнорируем ошибки записи лога */ }
    }

    public static void LogError(string context, Exception ex)
    {
        Log($"!!! ОШИБКА В {context}: {ex.Message} | {ex.StackTrace}");
    }

    /// <summary>UID в маскированном виде (первые 4 символа + ****) для использования в логах.</summary>
    public static string MaskUid(string? uid)
    {
        if (string.IsNullOrEmpty(uid) || uid.Length <= 4) return "****";
        return string.Concat(uid.AsSpan(0, 4), "****");
    }

    private static string Redact(string? message)
    {
        if (string.IsNullOrEmpty(message)) return message ?? string.Empty;
        var r = JsonPinRegex().Replace(message, "\"pin\":\"***\"");
        r = JsonPasswordRegex().Replace(r, "\"password\":\"***\"");
        r = JsonTokenRegex().Replace(r, "\"token\":\"***\"");
        r = BearerRegex().Replace(r, "Bearer ***");
        return r;
    }
}
