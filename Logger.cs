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
                File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
            }
        }
        catch { /* Если не удалось записать лог, просто игнорируем */ }
    }

    public static void LogError(string context, Exception ex)
    {
        Log($"!!! ОШИБКА В {context}: {ex.Message}\n{ex.StackTrace}");
    }
}
