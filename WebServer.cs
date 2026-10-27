using System.Globalization;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Net;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Diagnostics;

namespace ShkafMFSB;

public class WebServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private bool _disposed;
    private readonly Database _db;
    private readonly IHardware _hw;
    public string Port { get; private set; } = "50000"; 
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly TokenStore _tokens = new(TimeSpan.FromMinutes(30));

    public WebServer(Database db, IHardware hw) { _db = db; _hw = hw; }

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
                Port = p.ToString(CultureInfo.InvariantCulture);
                started = true;
                Logger.Log($"Веб-сервер запущен на порту {Port}");
                break;
            } catch (Exception ex) { Logger.LogError($"WebServer.Start port={p}", ex); }
        }

        if (!started) return;
        
        Task.Run(async () =>
        {
            while (_listener.IsListening)
            {
                try
                {
                    var ctx = await _listener.GetContextAsync();
                    _ = Task.Run(() => HandleRequest(ctx))
                        .ContinueWith(
                            t => Logger.LogError("WebServer.HandleRequest (unobserved)", t.Exception!),
                            TaskContinuationOptions.OnlyOnFaulted);
                }
                catch (HttpListenerException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex) { Logger.LogError("WebServer.GetContext", ex); }
            }
        });
    }

    private sealed class UnauthorizedException : Exception
    {
        public UnauthorizedException() : base("Не авторизован") { }
    }

    private sealed class TokenStore
    {
        private readonly ConcurrentDictionary<string, DateTime> _tokens = new();
        private readonly TimeSpan _ttl;

        public TokenStore(TimeSpan ttl) { _ttl = ttl; }

        public string Issue()
        {
            Cleanup();
            var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .Replace("+", "-").Replace("/", "_").Replace("=", "");
            _tokens[token] = DateTime.UtcNow.Add(_ttl);
            return token;
        }

        public bool IsValid(string? token)
        {
            if (string.IsNullOrEmpty(token)) return false;
            if (!_tokens.TryGetValue(token, out var expires)) return false;
            if (expires < DateTime.UtcNow) { _tokens.TryRemove(token, out _); return false; }
            return true;
        }

        private void Cleanup()
        {
            var now = DateTime.UtcNow;
            foreach (var kv in _tokens)
                if (kv.Value < now) _tokens.TryRemove(kv.Key, out _);
        }
    }

    private bool IsTokenValid(HttpListenerRequest req)
    {
        var header = req.Headers["Authorization"];
        if (string.IsNullOrEmpty(header)) return false;
        const string prefix = "Bearer ";
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        return _tokens.IsValid(header.Substring(prefix.Length).Trim());
    }

    // Валидация вынесена в Core: ApiValidation.cs (ValidationException, V, JsonBodyReader)
    private static Task<JsonElement> ReadJsonBodyAsync(HttpListenerRequest req)
        => JsonBodyReader.ReadObjectAsync(req.InputStream, req.ContentEncoding);

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

            if (reqPath.StartsWith("/api/", StringComparison.Ordinal))
            {
                res.ContentType = "application/json; charset=utf-8";
                string json = "{}";
                int status = 200;
                try
                {
                if (reqPath != "/api/verify-pin" && !IsTokenValid(req))
                    throw new UnauthorizedException();
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
                        {
                            var body = await ReadJsonBodyAsync(req);
                            bool ok = _db.VerifyPin(V.Str(body, "pin", 0, 128));
                            if (ok)
                            {
                                var token = _tokens.Issue();
                                json = JsonSerializer.Serialize(new { success = ok, token }, _jsonOptions);
                            }
                            else
                            {
                                json = JsonSerializer.Serialize(new { success = ok }, _jsonOptions);
                            }
                        }
                        break;

                    case "/api/settings/pin":
                        {
                            var body = await ReadJsonBodyAsync(req);
                            _db.UpdatePin(V.Str(body, "pin", 4, 128));
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
                    case "/api/locker/faulty":
                    case "/api/locker/service":
                    case "/api/open":
                        {
                            var body = await ReadJsonBodyAsync(req);
                            if (reqPath == "/api/employee/add") _db.AddEmployee(V.Str(body, "name", 1, 200));
                            if (reqPath == "/api/employee/update") _db.UpdateEmployee(V.Number(body, "id", 1), V.Str(body, "name", 1, 200));
                            if (reqPath == "/api/assign") _db.AssignLocker(V.Number(body, "lockerId", 1), V.NullableNumber(body, "employeeId", 1));
                            if (reqPath == "/api/tag/add") _db.AddTag(V.Str(body, "uid", 4, 64), V.Number(body, "employeeId", 1));
                            if (reqPath == "/api/tag/delete") _db.DeleteTag(V.Str(body, "uid", 4, 64));
                            if (reqPath == "/api/locker/faulty") _db.SetFaulty(V.Number(body, "id", 1), V.Bool(body, "faulty"));
                            if (reqPath == "/api/locker/service") _db.SetServiceLockers(V.Number(body, "id1", 1), V.Number(body, "id2", 1));
                            if (reqPath == "/api/open") { int lid = V.Number(body, "lockerId", 1); var l = _db.GetLockers().FirstOrDefault(x => x.Id == lid); if (l != null) { bool ok = await _hw.PulseRelayAsync(l.ModuleId, l.ChannelId); if (!ok) { status = 500; json = JsonSerializer.Serialize(new { error = "Не удалось открыть ячейку" }, _jsonOptions); } } }
                        }
                        break;

                    case "/api/hardware":
                        {
                            using var reader = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8);
                            var rawText = await reader.ReadToEndAsync();
                            if (string.IsNullOrWhiteSpace(rawText))
                                throw new ValidationException("Тело запроса пустое");
                            List<Locker>? items;
                            try { items = JsonSerializer.Deserialize<List<Locker>>(rawText, _jsonOptions); }
                            catch (JsonException jex) { throw new ValidationException("Некорректный JSON: " + jex.Message); }
                            if (items == null) throw new ValidationException("Список не может быть null");
                            foreach (var item in items)
                            {
                                if (item.Id <= 0) throw new ValidationException("Id ячейки должен быть > 0");
                                _db.UpdateHardware(item.Id, item.ModuleId, item.ChannelId);
                            }
                        }
                        break;

                    case "/api/tag/scan":
                        try
                        {
                            var uid = await _hw.WaitForNextTagAsync(15000);
                            json = JsonSerializer.Serialize(new { uid }, _jsonOptions);
                        }
                        catch (Exception ex) { Logger.Log("TagScan timeout: " + ex.Message); json = JsonSerializer.Serialize(new { error = "Timeout" }, _jsonOptions); }
                        break;

                    case "/api/restore-kiosk":
                        App.MainKiosk?.Dispatcher.Invoke(() =>
                        {
                            App.MainKiosk.WindowState = WindowState.Maximized;
                            if (!App.Hw.Settings.DemoMode) { App.MainKiosk.Topmost = true; App.MainKiosk.Cursor = System.Windows.Input.Cursors.None; }
                        });
                        break;
                }
                }
                catch (UnauthorizedException)
                {
                    status = 401;
                    json = JsonSerializer.Serialize(new { error = "Не авторизован" }, _jsonOptions);
                }
                catch (ValidationException vex)
                {
                    status = 400;
                    json = JsonSerializer.Serialize(new { error = vex.Message }, _jsonOptions);
                }
                catch (JsonException jex)
                {
                    status = 400;
                    json = JsonSerializer.Serialize(new { error = "Некорректный JSON: " + jex.Message }, _jsonOptions);
                }

                res.StatusCode = status;
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { if (_listener.IsListening) _listener.Stop(); }
        catch (Exception ex) { Logger.LogError("WebServer.Dispose (Stop)", ex); }
        try { _listener.Close(); }
        catch (Exception ex) { Logger.LogError("WebServer.Dispose (Close)", ex); }
        GC.SuppressFinalize(this);
    }
}
