namespace ShkafMFSB;

/// <summary>Демонстрационная реализация IHardware — без реального оборудования.</summary>
public class DemoHardware : IHardware
{
    private TaskCompletionSource<string>? _scanTcs;
    private readonly Random _rng = new();
    private readonly CancellationTokenSource _cts = new();

    public Config Settings { get; }
    public event Action<string>? TagScanned;

    public bool IsModbusConnected => true;
    public bool IsRfidAvailable => true;

    public DemoHardware(Config settings) { Settings = settings; }

    public bool ConnectModbus() => true;

    public Task<bool> PulseRelayAsync(int moduleId, int channel, int pulseMs = 5000)
    {
        // Имитация: короткая задержка, всегда успех
        return Task.Delay(200).ContinueWith(_ =>
        {
            Logger.Log($"DEMO: реле module={moduleId} channel={channel}");
            return true;
        });
    }

    public Task<string> WaitForNextTagAsync(int timeoutMs = 15000)
    {
        var tcs = new TaskCompletionSource<string>();
        _scanTcs = tcs;

        // Страховка: если метку никто не прислал, через timeoutMs вернём случайный UID
        var cts = new CancellationTokenSource();
        Task.Delay(timeoutMs, cts.Token).ContinueWith(_ =>
        {
            if (_scanTcs == tcs)
            {
                _scanTcs = null;
                tcs.TrySetResult("DEMO" + _rng.Next(1000, 9999));
            }
        });
        tcs.Task.ContinueWith(_ => cts.Cancel(), TaskContinuationOptions.ExecuteSynchronously);
        return tcs.Task;
    }

    public void StartRfid()
    {
        // Эмуляция: через 3с, затем раз в 10с шлём метку A1B2C3D4.
        // _cts позволяет остановить фоновый цикл при Dispose (раньше был while(true) без отмены).
        var token = _cts.Token;
        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(3000, token);
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(10000, token);
                    const string uid = "A1B2C3D4";
                    var tcs = _scanTcs;
                    if (tcs != null && tcs.TrySetResult(uid))
                        _scanTcs = null;
                    else
                        TagScanned?.Invoke(uid);
                }
            }
            catch (OperationCanceledException) { /* норма при Dispose */ }
            catch (Exception ex) { Logger.LogError("DemoHardware.StartRfid", ex); }
        });
    }

    public void Dispose()
    {
        _cts.Cancel();
        _scanTcs?.TrySetCanceled();
        _scanTcs = null;
        GC.SuppressFinalize(this);
    }
}
