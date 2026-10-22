using System.Net;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Reflection;

namespace ShkafMFSB;

public class WebServer
{
    private readonly HttpListener _listener = new();
    private readonly Database _db;
    private readonly Hardware _hw;
    private readonly string _port = "5000";

    public WebServer(Database db, Hardware hw)
    {
        _db = db;
        _hw = hw;
    }

    public void Start()
    {
        try
        {
            _listener.Prefixes.Add($"http://localhost:{_port}/");
            _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
            _listener.Prefixes.Add($"http://*:{_port}/"); 
            
            _listener.Start();
            Logger.Log($"Веб-сервер успешно запущен на порту {_port}");
            Logger.Log($"Путь к EXE: {Environment.ProcessPath}");
        }
        catch (Exception ex)
        {
            Logger.LogError("WebServer.Start", ex);
            MessageBox.Show($"Не удалось запустить веб-сервер на порту {_port}.\n\n" +
                            $"Убедитесь, что программа запущена от имени Администратора.\n" +
                            $"Текст ошибки: {ex.Message}", 
                            "Ошибка веб-сервера", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        
        Task.Run(async () =>
        {
            while (_listener.IsListening)
            {
                try
                {
                    var ctx = await _listener.GetContextAsync();
                    _ = Task.Run(() => HandleRequest(ctx));
                }
                catch (HttpListenerException) { break; }
                catch { /* Ignore */ }
            }
        });
    }

    private async Task HandleRequest(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var res = ctx.Response;
        res.Headers.Add("Access-Control-Allow-Origin", "*");

        try
        {
            string reqPath = req.Url?.AbsolutePath ?? "/";
            Logger.Log($"HTTP-Запрос: {req.HttpMethod} {reqPath}");

            if (reqPath.StartsWith("/api/"))
            {
                res.ContentType = "application/json; charset=utf-8";
                string json = "{}";

                switch (reqPath)
                {
                    case "/api/data":
                        json = JsonSerializer.Serialize(new
                        {
                            employees = _db.GetEmployees(),
                            lockers = _db.GetLockers(),
                            tags = _db.GetTags(),
                            logs = _db.GetLogs()
                        });
                        break;

                    case "/api/system-log":
                        string logPath = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory, "app.log");
                        string logText = File.Exists(logPath) ? File.ReadAllText(logPath) : "Лог пуст.";
                        json = JsonSerializer.Serialize(new { log = logText });
                        break;

                    case "/api/employee":
                        if (req.HttpMethod == "POST")
                        {
                            using var reader = new StreamReader(req.InputStream);
                            var body = JsonSerializer.Deserialize<JsonElement>(await reader.ReadToEndAsync());
                            _db.AddEmployee(body.GetProperty("name").GetString()!);
                        }
                        break;

                    case "/api/assign":
                        if (req.HttpMethod == "POST")
                        {
                            using var reader = new StreamReader(req.InputStream);
                            var body = JsonSerializer.Deserialize<JsonElement>(await reader.ReadToEndAsync());
                            int lockerId = body.GetProperty("lockerId").GetInt32();
                            int? empId = body.GetProperty("employeeId").ValueKind == JsonValueKind.Null ? null : body.GetProperty("employeeId").GetInt32();
                            _db.AssignLocker(lockerId, empId);
                        }
                        break;

                    case "/api/hardware":
                        if (req.HttpMethod == "POST")
                        {
                            using var reader = new StreamReader(req.InputStream);
                            var items = JsonSerializer.Deserialize<List<Locker>>(await reader.ReadToEndAsync());
                            foreach (var item in items!) _db.UpdateHardware(item.Id, item.ModuleId, item.ChannelId);
                        }
                        break;

                    case "/api/open":
                        if (req.HttpMethod == "POST")
                        {
                            using var reader = new StreamReader(req.InputStream);
                            var body = JsonSerializer.Deserialize<JsonElement>(await reader.ReadToEndAsync());
                            int lockerId = body.GetProperty("lockerId").GetInt32();
                            var locker = _db.GetLockers().FirstOrDefault(l => l.Id == lockerId);
                            if (locker != null) await _hw.PulseRelayAsync(locker.ModuleId, locker.ChannelId);
                        }
                        break;

                    case "/api/restore-kiosk":
                        if (req.HttpMethod == "POST")
                        {
                            App.MainKiosk?.Dispatcher.Invoke(() =>
                            {
                                App.MainKiosk.WindowState = WindowState.Maximized;
                                if (!App.Hw.Settings.DemoMode) 
                                {
                                    App.MainKiosk.Topmost = true;
                                    App.MainKiosk.Cursor = System.Windows.Input.Cursors.None;
                                }
                            });
                        }
                        break;
                }

                var bytes = Encoding.UTF8.GetBytes(json);
                res.ContentLength64 = bytes.Length;
                await res.OutputStream.WriteAsync(bytes);
            }
            else if (reqPath == "/favicon.ico")
            {
                res.StatusCode = 404;
            }
            else
            {
                string htmlContent = "";

                // 1. Ищем файл рядом с реальным EXE
                string? exePath = Environment.ProcessPath;
                string? exeDir = exePath != null ? Path.GetDirectoryName(exePath) : AppContext.BaseDirectory;
                string webDir = Path.Combine(exeDir!, "web");
                string filePath = Path.Combine(webDir, reqPath.TrimStart('/'));
                if (reqPath == "/" || reqPath == "/index.html") 
                    filePath = Path.Combine(webDir, "index.html");

                if (File.Exists(filePath))
                {
                    htmlContent = await File.ReadAllTextAsync(filePath);
                    Logger.Log($"HTML прочитан с диска: {filePath} (размер: {htmlContent.Length} байт)");
                }
                else
                {
                    // 2. Читаем из встроенных ресурсов
                    var assembly = Assembly.GetExecutingAssembly();
                    var resourceName = "ShkafMFSB.web.index.html"; 
                    
                    using Stream? stream = assembly.GetManifestResourceStream(resourceName);
                    if (stream != null)
                    {
                        using StreamReader reader = new StreamReader(stream);
                        htmlContent = await reader.ReadToEndAsync();
                        Logger.Log($"HTML прочитан из ресурсов EXE (размер: {htmlContent.Length} байт)");
                    }
                    else
                    {
                        Logger.Log("КРИТИЧЕСКАЯ ОШИБКА: HTML не найден ни на диске, ни в ресурсах!");
                        res.StatusCode = 404;
                        var errBytes = Encoding.UTF8.GetBytes("Файл index.html не найден.");
                        await res.OutputStream.WriteAsync(errBytes);
                        return;
                    }
                }

                var bytes = Encoding.UTF8.GetBytes(htmlContent);
                res.ContentType = "text/html; charset=utf-8";
                res.ContentLength64 = bytes.Length;
                await res.OutputStream.WriteAsync(bytes);
                Logger.Log($"Отправлено {bytes.Length} байт клиенту.");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError("HandleRequest", ex);
            res.StatusCode = 500;
            var bytes = Encoding.UTF8.GetBytes(ex.Message);
            await res.OutputStream.WriteAsync(bytes);
        }
        finally
        {
            res.Close();
        }
    }
}
