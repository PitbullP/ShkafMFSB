using NModbus;
using NModbus.Serial;
using PCSC;
using System.IO;
using System.IO.Ports;
using System.Text.Json;

namespace ShkafMFSB;

public class Config
{
    public bool DemoMode { get; set; } = true;
    public ModbusConfig Modbus { get; set; } = new();
    public RfidConfig Rfid { get; set; } = new();
    public WebConfig Web { get; set; } = new();
}

public class ModbusConfig { public string Port { get; set; } = "COM3"; public int BaudRate { get; set; } = 9600; }
public class RfidConfig { public string ReaderName { get; set; } = ""; }
public class WebConfig { public int Port { get; set; } = 5000; }

public class Hardware : IDisposable
{
    private IModbusMaster? _master;
    private SerialPort? _port;
    public Config Settings { get; private set; } = new();

    private TaskCompletionSource<string>? _scanTcs;

    public Hardware()
    {
        if (File.Exists("appsettings.json"))
        {
            Settings = JsonSerializer.Deserialize<Config>(File.ReadAllText("appsettings.json")) ?? new Config();
        }
    }

    public bool ConnectModbus()
    {
        if (Settings.DemoMode) return true;
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

    public async Task<bool> PulseRelayAsync(int module, int channel, int pulseMs = 5000)
    {
        if (Settings.DemoMode) { await Task.Delay(200); return true; }
        if (_master == null)
        {
            Logger.LogError("Hardware.PulseRelay (Modbus master не подключён)",
                new InvalidOperationException($"module={module}, channel={channel}"));
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
                    _master.WriteSingleCoil((byte)module, (ushort)channel, true);
                    await Task.Delay(pulseMs);
                    _master.WriteSingleCoil((byte)module, (ushort)channel, false);
                });
                if (attempt > 1) Logger.Log($"Hardware.PulseRelay OK module={module} channel={channel} (попытка {attempt})");
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Hardware.PulseRelay module={module} channel={channel} (попытка {attempt}/{maxAttempts})", ex);
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
        if (Settings.DemoMode)
        {
            // В демо-режиме просто ждем 2 секунды и возвращаем случайный UID
            var tcs = new TaskCompletionSource<string>();
            Task.Delay(2000).ContinueWith(_ => tcs.SetResult("DEMO" + new Random().Next(1000, 9999)));
            return tcs.Task;
        }

        _scanTcs = new TaskCompletionSource<string>();
        var cts = new CancellationTokenSource(timeoutMs);
        cts.Token.Register(() => _scanTcs.TrySetCanceled());
        return _scanTcs.Task;
    }
    
    public void StartRfid()
    {
        if (Settings.DemoMode)
        {
            // Демо-режим: раз в 10 секунд шлем метку
            Task.Run(async () => {
                await Task.Delay(3000);
                while (true)
                {
                    await Task.Delay(10000);
                    string uid = "A1B2C3D4";
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
            });
            return;
        }
        
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
