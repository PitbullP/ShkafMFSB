using ShkafMFSB;
using System;
using System.Threading.Tasks;
using Xunit;

namespace ShkafMFSB.Tests;

public class DemoHardwareTests
{
    private static DemoHardware New(out Config cfg)
    {
        cfg = new Config();
        return new DemoHardware(cfg);
    }

    [Fact]
    public void Settings_ReturnsProvidedConfig()
    {
        using var hw = New(out var cfg);
        Assert.Same(cfg, hw.Settings);
    }

    [Fact]
    public void ConnectModbus_ReturnsTrue()
    {
        using var hw = New(out _);
        Assert.True(hw.ConnectModbus());
    }

    [Fact]
    public async Task PulseRelayAsync_ReturnsTrue()
    {
        using var hw = New(out _);
        Assert.True(await hw.PulseRelayAsync(moduleId: 1, channel: 0));
    }

    [Fact]
    public async Task WaitForNextTagAsync_Timeout_ReturnsDemoUid()
    {
        using var hw = New(out _);
        var uid = await hw.WaitForNextTagAsync(timeoutMs: 200);
        Assert.StartsWith("DEMO", uid);
        Assert.True(uid.Length >= 5);
        Assert.True(int.TryParse(uid.Substring(4), out var num));
        Assert.InRange(num, 1000, 9998);
    }

    [Fact]
    public async Task WaitForNextTagAsync_PendingTask_ThrowsAfterDispose()
    {
        var hw = New(out _);
        var task = hw.WaitForNextTagAsync(timeoutMs: 30000); // долгий таймаут, ждём отмены
        hw.Dispose();
        await Assert.ThrowsAsync<TaskCanceledException>(() => task);
    }

    [Fact]
    public async Task StartRfid_DoesNotFireImmediately()
    {
        using var hw = New(out _);
        int fires = 0;
        hw.TagScanned += _ => Interlocked.Increment(ref fires);
        hw.StartRfid();
        await Task.Delay(150);
        Assert.Equal(0, fires);
    }

    [Fact(Skip = "Slow: первый тик через ~13с, гонять вручную при необходимости")]
    public async Task StartRfid_EmitsTagScannedEvent()
    {
        using var hw = New(out _);
        var tcs = new TaskCompletionSource<string>();
        hw.TagScanned += uid => tcs.TrySetResult(uid);
        hw.StartRfid();

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(20000));
        Assert.Same(tcs.Task, completed);
        Assert.Equal("A1B2C3D4", await tcs.Task);
    }
}
