namespace ShkafMFSB;

/// <summary>Демонстрационная реализация IHardware — без реального оборудования.</summary>
public class DemoHardware : IHardware
{
    private TaskCompletionSource<string>? _scanTcs;
    private readonly Random _rng = new();

    public Config Settings { get; }
    public event Action<string>? TagScanned;

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
        // Раз в 10 секунд эмулируем сканирование метки
        Task.Run(async () =>
        {
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
    }

    public void Dispose()
    {
        _scanTcs?.TrySetCanceled();
        _scanTcs = null;
        GC.SuppressFinalize(this);
    }
}
