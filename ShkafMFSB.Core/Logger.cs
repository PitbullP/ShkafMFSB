using System.IO;
using System.Text.RegularExpressions;

namespace ShkafMFSB;

public static partial class Logger
{
    private static readonly string LogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.log");
    private static readonly object LockObj = new();

    // Ротация: при превышении 10 МБ app.log -> app.1.log, app.1.log -> app.2.log, ... храним 5 файлов
    private const long MaxLogSizeBytes = 10L * 1024 * 1024;
    private const int MaxLogFiles = 5;

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
                RotateIfNeeded();
                string cleanMessage = Redact(message).Replace("\r\n", " ").Replace("\n", " ");
                File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {cleanMessage}{Environment.NewLine}");
            }
        }
        catch { /* best-effort: ошибка логирования не должна вызывать рекурсию/падение */ }
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

    private static void RotateIfNeeded()
    {
        try
        {
            var info = new FileInfo(LogPath);
            if (!info.Exists || info.Length < MaxLogSizeBytes) return;

            // Сдвигаем app.{N-1}.log -> app.{N}.log, удаляя самый старый
            for (int i = MaxLogFiles - 1; i >= 1; i--)
            {
                var src = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"app.{i}.log");
                var dst = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"app.{i + 1}.log");
                if (File.Exists(src)) File.Move(src, dst, overwrite: true);
            }

            // app.log -> app.1.log
            File.Move(LogPath, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.1.log"), overwrite: true);
        }
        catch
        {
            // best-effort: не роняем логирование из-за проблем с ФС
        }
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
