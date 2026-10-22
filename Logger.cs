using System.IO;

namespace ShkafMFSB;

public static class Logger
{
    private static readonly string LogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.log");
    private static readonly object LockObj = new();

    public static void Log(string message)
    {
        try
        {
            lock (LockObj)
            {
                // Заменяем все переносы строк на пробелы, чтобы запись была в одну строку
                string cleanMessage = message.Replace("\r\n", " ").Replace("\n", " ");
                File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {cleanMessage}{Environment.NewLine}");
            }
        }
        catch { /* Игнорируем ошибки записи лога */ }
    }

    public static void LogError(string context, Exception ex)
    {
        Log($"!!! ОШИБКА В {context}: {ex.Message} | {ex.StackTrace}");
    }
}
