using System.Diagnostics;
using HASNetMonitor.Core;

namespace HASNetMonitor.Tests;

/// <summary>
/// The GPU monitor's threading contract. The widget's sample timer callback runs on the
/// shared ThreadPool, so a slow GPU collect must never occupy a pool thread or block the
/// caller - that is what froze the whole visualization. These tests pin the contract:
/// ReadPercent returns promptly, the collect runs on a dedicated thread, and Dispose
/// never hangs.
/// </summary>
public class GpuMonitorThreadingTests
{
    /// <summary>
    /// ReadPercent must return promptly on every tick, even while a collect is in
    /// flight. A blocking read here is what stalls the widget's timer callback.
    /// </summary>
    [Fact]
    public void ReadPercentNeverBlocksTheCaller()
    {
        using var monitor = new GpuMonitor();
        var sw = Stopwatch.StartNew();

        for (int i = 0; i < 200; i++)
        {
            long before = sw.ElapsedMilliseconds;
            monitor.ReadPercent();
            long elapsed = sw.ElapsedMilliseconds - before;

            // Generous bound: the call only signals a worker and reads a field. A
            // blocking PDH collect would blow far past this.
            Assert.True(elapsed < 50, $"ReadPercent blocked for {elapsed}ms on tick {i}");
        }
    }

    /// <summary>
    /// The collect must run on a thread this monitor owns, not a shared ThreadPool
    /// thread. Pool threads are what the widget's timer callback also needs.
    /// </summary>
    [Fact]
    public void TheCollectRunsOffTheSharedThreadPool()
    {
        using var monitor = new GpuMonitor();

        // Drive a few reads so the worker starts and a collect runs.
        for (int i = 0; i < 5; i++)
        {
            monitor.ReadPercent();
            Thread.Sleep(20);
        }

        Thread? worker = monitor.WorkerThreadForTests;
        Assert.NotNull(worker);
        Assert.True(worker!.IsAlive);
        Assert.True(worker.IsBackground);
        Assert.False(worker.IsThreadPoolThread, "the GPU collect must not use a ThreadPool thread");
    }

    /// <summary>
    /// Dispose must return promptly and be safe to call twice. The old drain loop could
    /// block the UI thread for two seconds.
    /// </summary>
    [Fact]
    public void DisposeReturnsPromptlyAndIsIdempotent()
    {
        var monitor = new GpuMonitor();
        for (int i = 0; i < 5; i++)
        {
            monitor.ReadPercent();
            Thread.Sleep(20);
        }

        var sw = Stopwatch.StartNew();
        monitor.Dispose();
        monitor.Dispose();
        long elapsed = sw.ElapsedMilliseconds;

        Assert.True(elapsed < 3000, $"Dispose took {elapsed}ms");
    }

    /// <summary>
    /// After Dispose, ReadPercent reports nothing rather than a stale value.
    /// </summary>
    [Fact]
    public void ReadPercentAfterDisposeReturnsNull()
    {
        var monitor = new GpuMonitor();
        monitor.ReadPercent();
        monitor.Dispose();

        Assert.Null(monitor.ReadPercent());
    }

    /// <summary>
    /// A read that never resolves must not leave the monitor permanently stuck: the
    /// caller keeps getting prompt answers and the worker keeps being signalled.
    /// </summary>
    [Fact]
    public void RepeatedReadsKeepReturningPromptly()
    {
        using var monitor = new GpuMonitor();
        var sw = Stopwatch.StartNew();

        for (int i = 0; i < 100; i++)
        {
            long before = sw.ElapsedMilliseconds;
            monitor.ReadPercent();
            Assert.True(sw.ElapsedMilliseconds - before < 50);
            Thread.Sleep(5);
        }
    }

    /// <summary>
    /// Every tick must be able to start a fresh collect once the previous one finished.
    /// If the in-flight flag ever stuck, the chip would freeze on one value forever -
    /// the permanent-freeze symptom. This drives many ticks and requires the completed
    /// collect count to keep climbing.
    /// </summary>
    [Fact]
    public void EachTickCanStartAFreshCollect()
    {
        using var monitor = new GpuMonitor();

        int first = 0;
        for (int i = 0; i < 200 && first == 0; i++)
        {
            monitor.ReadPercent();
            Thread.Sleep(10);
            first = monitor.CompletedCollectsForTests;
        }

        Assert.True(first > 0, "no collect ever completed");

        // Keep ticking; the count must advance past the first collect.
        for (int i = 0; i < 200 && monitor.CompletedCollectsForTests <= first; i++)
        {
            monitor.ReadPercent();
            Thread.Sleep(10);
        }

        Assert.True(
            monitor.CompletedCollectsForTests > first,
            "the in-flight flag stuck: no further collect ran after the first");
    }
}
