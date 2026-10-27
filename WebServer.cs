using System.Net;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Reflection;
using System.Diagnostics;

namespace ShkafMFSB;

public class WebServer
{
    private readonly HttpListener _listener = new();
    private readonly Database _db;
    private readonly Hardware _hw;
    
    public string Port { get; private set; } = "50000"; 

    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public WebServer(Database db, Hardware hw)
    {
        _db = db;
        _hw = hw;
    }

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
            }
            catch { }
        }

        if (!started) return;
        
        Task.Run(async () =>
        {
            while (_listener.IsListening)
            {
                try
                {
                    var ctx = await _listener.GetContextAsync();
                    _ = Task.Run(() => HandleRequest(ctx));
                }
                catch { }
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
                        }, _jsonOptions);
                        break;

                    case "/api/employee/add":
                        if (req.HttpMethod == "POST")
                        {
                            using var reader = new StreamReader(req.InputStream);
                            var body = JsonSerializer.Deserialize<JsonElement>(await reader.ReadToEndAsync());
                            _db.AddEmployee(body.GetProperty("name").GetString()!);
                        }
                        break;

                    case "/api/employee/update":
                        if (req.HttpMethod == "POST")
                        {
                            using var reader = new StreamReader(req.InputStream);
                            var body = JsonSerializer.Deserialize<JsonElement>(await reader.ReadToEndAsync());
                            _db.UpdateEmployee(body.GetProperty("id").GetInt32(), body.GetProperty("name").GetString()!);
                        }
                        break;

                    case "/api/assign":
                        if (req.HttpMethod == "POST")
                        {
                            using var reader = new StreamReader(req.InputStream);
                            var body = JsonSerializer.Deserialize<JsonElement>(await reader.ReadToEndAsync());
                            _db.AssignLocker(body.GetProperty("lockerId").GetInt32(), 
                                body.GetProperty("employeeId").ValueKind == JsonValueKind.Null ? null : body.GetProperty("employeeId").GetInt32());
                        }
                        break;

                    case "/api/tag/add":
                        if (req.HttpMethod == "POST")
                        {
                            using var reader = new StreamReader(req.InputStream);
                            var body = JsonSerializer.Deserialize<JsonElement>(await reader.ReadToEndAsync());
                            _db.AddTag(body.GetProperty("uid").GetString()!, body.GetProperty("employeeId").GetInt32());
                        }
                        break;

                    case "/api/tag/delete":
                        if (req.HttpMethod == "POST")
                        {
                            using var reader = new StreamReader(req.InputStream);
                            var body = JsonSerializer.Deserialize<JsonElement>(await reader.ReadToEndAsync());
                            _db.DeleteTag(body.GetProperty("uid").GetString()!);
                        }
                        break;

                    case "/api/tag/scan":
                        try
                        {
                            var uid = await _hw.WaitForNextTagAsync(15000);
                            json = JsonSerializer.Serialize(new { uid }, _jsonOptions);
                        }
                        catch (TaskCanceledException)
                        {
                            json = JsonSerializer.Serialize(new { error = "Timeout" }, _jsonOptions);
                        }
                        break;

                    case "/api/hardware":
                        if (req.HttpMethod == "POST")
                        {
                            using var reader = new StreamReader(req.InputStream);
                            var items = JsonSerializer.Deserialize<List<Locker>>(await reader.ReadToEndAsync(), _jsonOptions);
                            foreach (var item in items!) _db.UpdateHardware(item.Id, item.ModuleId, item.ChannelId);
                        }
                        break;

                    case "/api/locker/faulty":
                        if (req.HttpMethod == "POST")
                        {
                            using var reader = new StreamReader(req.InputStream);
                            var body = JsonSerializer.Deserialize<JsonElement>(await reader.ReadToEndAsync());
                            _db.SetFaulty(body.GetProperty("id").GetInt32(), body.GetProperty("faulty").GetBoolean());
                        }
                        break;

                    case "/api/locker/service":
                        if (req.HttpMethod == "POST")
                        {
                            using var reader = new StreamReader(req.InputStream);
                            var body = JsonSerializer.Deserialize<JsonElement>(await reader.ReadToEndAsync());
                            _db.SetServiceLockers(body.GetProperty("id1").GetInt32(), body.GetProperty("id2").GetInt32());
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
