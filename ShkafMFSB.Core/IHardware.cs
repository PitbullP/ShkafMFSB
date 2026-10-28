namespace ShkafMFSB;

/// <summary>Абстракция аппаратной части шкафа — реальной или демонстрационной.</summary>
public interface IHardware : IDisposable
{
    Config Settings { get; }
    event Action<string>? TagScanned;

    /// <summary>Есть ли активное подключение к Modbus RTU.</summary>
    bool IsModbusConnected { get; }

    /// <summary>Доступен ли RFID-считыватель (PC/SC).</summary>
    bool IsRfidAvailable { get; }

    bool ConnectModbus();
    void StartRfid();
    Task<bool> PulseRelayAsync(int moduleId, int channel, int pulseMs = 5000);
    Task<string> WaitForNextTagAsync(int timeoutMs = 15000);
}
