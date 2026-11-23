using System.Diagnostics;
using HASNetMonitor.Core;
using Xunit;

namespace HASNetMonitor.Tests;

/// <summary>
/// The user's requirement: every metric is sampled independently, so a metric whose read
/// is slow - a GPU PDH sweep, a disk query on a spun-down drive - must never hold up any
/// other metric. These tests pin that: a slow sampler's Latest returns instantly, a fast
/// sampler keeps advancing while a slow one is stuck, and a throwing read cannot kill a
/// sampler or blank the others.
/// </summary>
public class MetricSamplerTests
{
    [Fact]
    public void LatestNeverBlocksEvenWhenTheReadIsSlow()
    {
        using var slow = new MetricSampler<int>(() =>
        {
            System.Threading.Thread.Sleep(5000);
            return 1;
        }, unavailable: -1, intervalMs: 10);

        slow.Start();

        // The read is still sleeping; Latest must return at once with the placeholder.
        var sw = Stopwatch.StartNew();
        int value = slow.Latest;
        sw.Stop();

        Assert.Equal(-1, value);
        Assert.True(sw.ElapsedMilliseconds < 200, $"Latest blocked for {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void AFastSamplerKeepsAdvancingWhileASlowOneIsStuck()
    {
        int fastCount = 0;
        using var fast = new MetricSampler<int>(() => System.Threading.Interlocked.Increment(ref fastCount), unavailable: 0, intervalMs: 20);
        using var slow = new MetricSampler<int>(() =>
        {
            System.Threading.Thread.Sleep(5000);
            return 99;
        }, unavailable: -1, intervalMs: 10);

        fast.Start();
        slow.Start();

        System.Threading.Thread.Sleep(300);

        // The slow sampler is still inside its first read, so it reports its placeholder.
        Assert.Equal(-1, slow.Latest);

        // The fast sampler has advanced many times regardless.
        Assert.True(fast.Latest >= 3, $"the fast sampler only advanced to {fast.Latest}");
    }

    [Fact]
    public void AThrowingReadDoesNotKillTheSampler()
    {
        int calls = 0;
        using var sampler = new MetricSampler<int>(() =>
        {
            int n = System.Threading.Interlocked.Increment(ref calls);
            if (n == 1)
            {
                throw new InvalidOperationException("first read fails");
            }

            return n;
        }, unavailable: -1, intervalMs: 20);

        sampler.Start();

        // Wait for the second read, which succeeds.
        var deadline = Stopwatch.StartNew();
        while (sampler.Latest < 0 && deadline.ElapsedMilliseconds < 2000)
        {
            System.Threading.Thread.Sleep(20);
        }

        Assert.True(sampler.Latest >= 2, $"the sampler stopped after a throwing read (Latest={sampler.Latest})");
    }

    [Fact]
    public void DisposeStopsTheThreadAndIsSafeToRepeat()
    {
        int calls = 0;
        var sampler = new MetricSampler<int>(() => System.Threading.Interlocked.Increment(ref calls), unavailable: 0, intervalMs: 10);
        sampler.Start();
        System.Threading.Thread.Sleep(100);

        sampler.Dispose();
        sampler.Dispose();

        int after = calls;
        System.Threading.Thread.Sleep(150);
        Assert.Equal(after, calls);
    }

    [Fact]
    public void LatestIsThePlaceholderBeforeTheFirstReadCompletes()
    {
        using var sampler = new MetricSampler<string>(() => "value", unavailable: "none", intervalMs: 10);
        Assert.Equal("none", sampler.Latest);
        Assert.False(sampler.HasValue);
    }
}
