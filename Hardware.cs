using NModbus;
using PCSC;
using System.IO.Ports;
using System.Text.Json;

namespace ShkafMFSB;

public class Config
{
    public bool DemoMode { get; set; } = true;
    public ModbusConfig Modbus { get; set; } = new();
    public RfidConfig Rfid { get; set; } = new();
}
public class ModbusConfig { public string Port { get; set; } = "COM3"; public int BaudRate { get; set; } = 9600; }
public class RfidConfig { public string ReaderName { get; set; } = ""; }

public class Hardware
{
    private IModbusMaster? _master;
    private SerialPort? _port;
    public Config Settings { get; private set; } = new();

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
            _master = new ModbusFactory().CreateRtuMaster(_port);
            _master.Transport.ReadTimeout = 500;
            return true;
        }
        catch { return false; }
    }

    public async Task PulseRelayAsync(int module, int channel, int pulseMs = 5000)
    {
        if (Settings.DemoMode) { await Task.Delay(200); return; }
        if (_master == null) return;
        
        await Task.Run(async () =>
        {
            try
            {
                _master.WriteSingleCoil((byte)module, (ushort)channel, true);
                await Task.Delay(pulseMs);
                _master.WriteSingleCoil((byte)module, (ushort)channel, false);
            }
            catch { /* Ignore */ }
        });
    }

    public event Action<string>? TagScanned;
    
    public void StartRfid()
    {
        if (Settings.DemoMode) return;
        
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
                            TagScanned?.Invoke(uid);
                        }
                    }
                    Thread.Sleep(300);
                }
            }
            catch { /* Log or ignore */ }
        });
    }
}
