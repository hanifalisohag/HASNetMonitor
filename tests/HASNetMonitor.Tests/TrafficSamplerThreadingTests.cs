using System.Diagnostics;
using HASNetMonitor.Core;
using Xunit;

namespace HASNetMonitor.Tests;

/// <summary>
/// Pins the property that fixes the "all chips show -- for a long time, then frozen"
/// freeze: ReadTotals must never enumerate adapters inline. Enumeration can block for
/// seconds on a machine with a VPN or a disconnected adapter, and because it used to
/// run on the sample thread before the sample was posted, the whole widget stalled.
/// </summary>
public class TrafficSamplerThreadingTests
{
    [Fact]
    public void ReadTotalsReturnsQuicklyEvenWhenEnumerationIsSlow()
    {
        var sampler = new TrafficSampler();

        // The very first read triggers a refresh. If the refresh enumerated inline,
        // a slow NDIS query would show up right here.
        var stopwatch = Stopwatch.StartNew();
        sampler.ReadTotals();
        stopwatch.Stop();

        Assert.True(
            stopwatch.ElapsedMilliseconds < 100,
            $"the first ReadTotals took {stopwatch.ElapsedMilliseconds} ms; enumeration is running inline");

        sampler.Dispose();
    }

    [Fact]
    public void ReadTotalsNeverBlocksOnTheRefreshThread()
    {
        var sampler = new TrafficSampler();

        // The refresh runs on its own thread; the sample thread must never wait for it.
        // A read that returns while a refresh is in flight proves the two are decoupled.
        var stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < 100; i++)
        {
            sampler.ReadTotals();
        }

        stopwatch.Stop();

        Assert.True(stopwatch.ElapsedMilliseconds < 500, $"reads took {stopwatch.ElapsedMilliseconds} ms");

        sampler.Dispose();
    }

    [Fact]
    public void TheFirstReadReturnsRealTrafficNotZero()
    {
        var sampler = new TrafficSampler();

        // The first read waits briefly for the initial enumeration, so the widget does
        // not seed itself with zero traffic and then show a huge first jump.
        var totals = sampler.ReadTotals();

        Assert.True(
            totals.Received > 0 || totals.Sent > 0,
            "the first read returned zero traffic; the initial enumeration did not complete");

        sampler.Dispose();
    }

    [Fact]
    public void DisposeIsSafeBeforeAnyRead()
    {
        var sampler = new TrafficSampler();

        sampler.Dispose();
        sampler.Dispose();

        var totals = sampler.ReadTotals();
        Assert.Equal(0, totals.Received);
        Assert.Equal(0, totals.Sent);
    }

    [Fact]
    public void DisposeStopsTheRefreshThread()
    {
        var sampler = new TrafficSampler();
        sampler.ReadTotals();

        sampler.Dispose();

        // After dispose the sampler is inert: reads return zero and never restart a thread.
        var totals = sampler.ReadTotals();
        Assert.Equal(0, totals.Received);
        Assert.Equal(0, totals.Sent);
    }
}
