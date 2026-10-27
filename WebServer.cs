using System.Net;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Diagnostics;

namespace ShkafMFSB;

public class WebServer
{
    private readonly HttpListener _listener = new();
    private readonly Database _db;
    private readonly Hardware _hw;
    public string Port { get; private set; } = "50000"; 
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public WebServer(Database db, Hardware hw) { _db = db; _hw = hw; }

    public void Start()
    {
        bool started = false;
        int startPort = _hw.Settings.Web.Port;
        if (startPort <= 0) startPort = 50000;
        
        for (int p = startPort; p <= startPort + 20; p++)
        {
            try
            {
                _listener.Prefixes.Clear();
                _listener.Prefixes.Add($"http://127.0.0.1:{p}/");
                _listener.Prefixes.Add($"http://localhost:{p}/");
                _listener.Start();
                Port = p.ToString();
                started = true;
                Logger.Log($"Веб-сервер запущен на порту {Port}");
                break;
            } catch { }
        }

        if (!started) return;
        
        Task.Run(async () =>
        {
            while (_listener.IsListening)
            {
                try { var ctx = await _listener.GetContextAsync(); _ = Task.Run(() => HandleRequest(ctx)); } catch { }
            }
        });
    }

    private void ApplyCors(HttpListenerRequest req, HttpListenerResponse res)
    {
        var origin = req.Headers["Origin"];
        if (!string.IsNullOrEmpty(origin) && IsAllowedOrigin(origin))
        {
            res.Headers.Add("Access-Control-Allow-Origin", origin);
            res.Headers.Add("Vary", "Origin");
        }
        res.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
        res.Headers.Add("Access-Control-Allow-Headers", "Content-Type");
        res.Headers.Add("Access-Control-Max-Age", "600");
    }

    private bool IsAllowedOrigin(string origin)
    {
        return origin == $"http://localhost:{Port}"
            || origin == $"http://127.0.0.1:{Port}";
    }

    private async Task HandleRequest(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var res = ctx.Response;
        ApplyCors(req, res);

        try
        {
            // CORS preflight
            if (string.Equals(req.HttpMethod, "OPTIONS", StringComparison.OrdinalIgnoreCase))
            {
                res.StatusCode = 204;
                return;
            }

            string reqPath = req.Url?.AbsolutePath ?? "/";

            if (reqPath.StartsWith("/api/"))
            {
                res.ContentType = "application/json; charset=utf-8";
                string json = "{}";

                switch (reqPath)
                {
                    case "/api/data":
                        json = JsonSerializer.Serialize(new { employees = _db.GetEmployees(), lockers = _db.GetLockers(), tags = _db.GetTags(), logs = _db.GetLogs() }, _jsonOptions);
                        break;

                    case "/api/system-log":
                        // ИСПРАВЛЕНО: Берем путь из реального расположения EXE
                        string logPath = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory, "app.log");
                        string logText = File.Exists(logPath) ? File.ReadAllText(logPath) : "Файл app.log пуст или не найден.";
                        json = JsonSerializer.Serialize(new { log = logText }, _jsonOptions);
                        break;

                    case "/api/verify-pin":
                        using (var reader = new StreamReader(req.InputStream))
                        {
                            var body = JsonSerializer.Deserialize<JsonElement>(await reader.ReadToEndAsync());
                            bool ok = _db.VerifyPin(body.GetProperty("pin").GetString()!);
                            json = JsonSerializer.Serialize(new { success = ok }, _jsonOptions);
                        }
                        break;

                    case "/api/settings/pin":
                        using (var reader = new StreamReader(req.InputStream))
                        {
                            var body = JsonSerializer.Deserialize<JsonElement>(await reader.ReadToEndAsync());
                            _db.UpdatePin(body.GetProperty("pin").GetString()!);
                        }
                        break;

                    case "/api/backup":
                        string backupFile = _db.BackupDb();
                        json = JsonSerializer.Serialize(new { path = backupFile }, _jsonOptions);
                        break;

                    case "/api/employee/add":
                    case "/api/employee/update":
                    case "/api/assign":
                    case "/api/tag/add":
                    case "/api/tag/delete":
                    case "/api/hardware":
                    case "/api/locker/faulty":
                    case "/api/locker/service":
                    case "/api/open":
                        using (var reader = new StreamReader(req.InputStream))
                        {
                            var body = JsonSerializer.Deserialize<JsonElement>(await reader.ReadToEndAsync());
                            if (reqPath == "/api/employee/add") _db.AddEmployee(body.GetProperty("name").GetString()!);
                            if (reqPath == "/api/employee/update") _db.UpdateEmployee(body.GetProperty("id").GetInt32(), body.GetProperty("name").GetString()!);
                            if (reqPath == "/api/assign") _db.AssignLocker(body.GetProperty("lockerId").GetInt32(), body.GetProperty("employeeId").ValueKind == JsonValueKind.Null ? null : body.GetProperty("employeeId").GetInt32());
                            if (reqPath == "/api/tag/add") _db.AddTag(body.GetProperty("uid").GetString()!, body.GetProperty("employeeId").GetInt32());
                            if (reqPath == "/api/tag/delete") _db.DeleteTag(body.GetProperty("uid").GetString()!);
                            if (reqPath == "/api/locker/faulty") _db.SetFaulty(body.GetProperty("id").GetInt32(), body.GetProperty("faulty").GetBoolean());
                            if (reqPath == "/api/locker/service") _db.SetServiceLockers(body.GetProperty("id1").GetInt32(), body.GetProperty("id2").GetInt32());
                            if (reqPath == "/api/hardware") { var items = JsonSerializer.Deserialize<List<Locker>>(body.GetRawText(), _jsonOptions); foreach (var item in items!) _db.UpdateHardware(item.Id, item.ModuleId, item.ChannelId); }
                            if (reqPath == "/api/open") { int lid = body.GetProperty("lockerId").GetInt32(); var l = _db.GetLockers().FirstOrDefault(x => x.Id == lid); if (l != null) await _hw.PulseRelayAsync(l.ModuleId, l.ChannelId); }
                        }
                        break;

                    case "/api/tag/scan":
                        try
                        {
                            var uid = await _hw.WaitForNextTagAsync(15000);
                            json = JsonSerializer.Serialize(new { uid }, _jsonOptions);
                        }
                        catch { json = JsonSerializer.Serialize(new { error = "Timeout" }, _jsonOptions); }
                        break;

                    case "/api/restore-kiosk":
                        App.MainKiosk?.Dispatcher.Invoke(() =>
                        {
                            App.MainKiosk.WindowState = WindowState.Maximized;
                            if (!App.Hw.Settings.DemoMode) { App.MainKiosk.Topmost = true; App.MainKiosk.Cursor = System.Windows.Input.Cursors.None; }
                        });
                        break;
                }

                var bytes = Encoding.UTF8.GetBytes(json);
                res.ContentLength64 = bytes.Length;
                await res.OutputStream.WriteAsync(bytes);
            }
            else
            {
                string filePath = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!, "web", "index.html");
                if (File.Exists(filePath))
                {
                    string htmlContent = await File.ReadAllTextAsync(filePath);
                    var bytes = Encoding.UTF8.GetBytes(htmlContent);
                    res.ContentType = "text/html; charset=utf-8";
                    res.ContentLength64 = bytes.Length;
                    await res.OutputStream.WriteAsync(bytes);
                }
                else res.StatusCode = 404;
            }
        }
        catch (Exception ex) { Logger.LogError("WebServer", ex); }
        finally { res.Close(); }
    }
}
