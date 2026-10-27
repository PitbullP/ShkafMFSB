using NModbus;
using NModbus.Serial;
using PCSC;
using System.IO;
using System.IO.Ports;
using System.Text.Json;

namespace ShkafMFSB;

public class Config
{
    public bool DemoMode { get; set; }
    public ModbusConfig Modbus { get; set; } = new();
    public RfidConfig Rfid { get; set; } = new();
    public WebConfig Web { get; set; } = new();
    public LockerConfig LockerConfig { get; set; } = new();
}

public class ModbusConfig { public string Port { get; set; } = "COM3"; public int BaudRate { get; set; } = 9600; }
public class RfidConfig { public string ReaderName { get; set; } = ""; }
public class WebConfig { public int Port { get; set; } = 5000; }

public class LockerConfig
{
    public int TotalCells { get; set; } = 62;
    public int ServiceCellThreshold { get; set; } = 60; // ячейки с Id > threshold — сервисные
    public int Module1MaxCell { get; set; } = 32;        // ячейки 1..N — модуль 1, остальные — модуль 2
}

public static class ConfigExtensions
{
    public static Config Load(string path = "appsettings.json")
    {
        if (!File.Exists(path)) return new Config();
        return JsonSerializer.Deserialize<Config>(File.ReadAllText(path)) ?? new Config();
    }
}

public class RealHardware : IHardware
{
    private IModbusMaster? _master;
    private SerialPort? _port;
    public Config Settings { get; }

    private TaskCompletionSource<string>? _scanTcs;

    public RealHardware(Config settings)
    {
        Settings = settings;
    }

    public bool ConnectModbus()
    {
        try
        {
            _port = new SerialPort(Settings.Modbus.Port, Settings.Modbus.BaudRate, Parity.None, 8, StopBits.One);
            _port.Open();
            var factory = new ModbusFactory();
            _master = factory.CreateRtuMaster(new SerialPortAdapter(_port));
            _master.Transport.ReadTimeout = 500;
            return true;
        }
        catch (Exception ex) { Logger.LogError("Hardware.Connect (Modbus)", ex); return false; }
    }

    public async Task<bool> PulseRelayAsync(int moduleId, int channel, int pulseMs = 5000)
    {
        if (_master == null)
        {
            Logger.LogError("Hardware.PulseRelay (Modbus master не подключён)",
                new InvalidOperationException($"module={moduleId}, channel={channel}"));
            return false;
        }

        const int maxAttempts = 3;
        int[] backoffMs = { 100, 300, 900 };

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await Task.Run(async () =>
                {
                    _master.WriteSingleCoil((byte)moduleId, (ushort)channel, true);
                    await Task.Delay(pulseMs);
                    _master.WriteSingleCoil((byte)moduleId, (ushort)channel, false);
                });
                if (attempt > 1) Logger.Log($"Hardware.PulseRelay OK module={moduleId} channel={channel} (попытка {attempt})");
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Hardware.PulseRelay module={moduleId} channel={channel} (попытка {attempt}/{maxAttempts})", ex);
                if (attempt < maxAttempts)
                    await Task.Delay(backoffMs[attempt - 1]);
            }
        }
        return false;
    }

    public event Action<string>? TagScanned;

    // Метод для перехвата метки в админке
    public Task<string> WaitForNextTagAsync(int timeoutMs = 15000)
    {

        _scanTcs = new TaskCompletionSource<string>();
        var cts = new CancellationTokenSource(timeoutMs);
        cts.Token.Register(() => _scanTcs.TrySetCanceled());
        return _scanTcs.Task;
    }
    
    public void StartRfid()
    {
        Task.Run(() =>
        {
            try
            {
                using var ctx = ContextFactory.Instance.Establish(SCardScope.System);
                var readers = ctx.GetReaders();
                if (readers.Length == 0) return;
                string readerName = string.IsNullOrEmpty(Settings.Rfid.ReaderName) ? readers[0] : Settings.Rfid.ReaderName;

                using var reader = new SCardReader(ctx);
                if (reader.Connect(readerName, SCardShareMode.Shared, SCardProtocol.Any) != SCardError.Success) return;

                while (true)
                {
                    var apdu = new byte[] { 0xFF, 0xCA, 0x00, 0x00, 0x00 };
                    var recv = new byte[256];
                    var rc = reader.Transmit(apdu, ref recv);
                    if (rc == SCardError.Success && recv.Length >= 2)
                    {
                        int len = recv.Length - 2;
                        if (len > 0 && recv[len] == 0x90)
                        {
                            var uid = Convert.ToHexString(recv[..len]);
                            
                            if (_scanTcs != null)
                            {
                                _scanTcs.TrySetResult(uid);
                                _scanTcs = null;
                            }
                            else
                            {
                                TagScanned?.Invoke(uid);
                            }
                        }
                    }
                    Thread.Sleep(300);
                }
            }
            catch (Exception ex) { Logger.LogError("Hardware.RfidScan", ex); }
        });
    }

    public void Dispose()
    {
        try { (_master as IDisposable)?.Dispose(); }
        catch (Exception ex) { Logger.LogError("Hardware.Dispose (Modbus)", ex); }
        try { _port?.Close(); _port?.Dispose(); }
        catch (Exception ex) { Logger.LogError("Hardware.Dispose (SerialPort)", ex); }
        try { _scanTcs?.TrySetCanceled(); }
        catch (Exception ex) { Logger.LogError("Hardware.Dispose (ScanTcs)", ex); }
        _master = null;
        _port = null;
        _scanTcs = null;
        GC.SuppressFinalize(this);
    }
}
