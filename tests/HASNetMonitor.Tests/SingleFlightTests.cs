using HASNetMonitor.Core;
using Xunit;

namespace HASNetMonitor.Tests;

/// <summary>
/// The sample timer fires on a pool thread every interval whether or not the previous
/// callback finished. A slow sample - a GPU PDH sweep, a disk read on a spun-down drive -
/// would otherwise pile callbacks up on the pool until the UI thread starves and the whole
/// widget freezes. <see cref="SingleFlight"/> is the guard that keeps one sample in flight.
/// </summary>
public class SingleFlightTests
{
    [Fact]
    public void RunsTheOperationWhenIdle()
    {
        var flight = new SingleFlight();
        int runs = 0;

        Assert.True(flight.TryRun(() => runs++));
        Assert.Equal(1, runs);
    }

    [Fact]
    public void SkipsATickThatArrivesWhileOneIsRunning()
    {
        var flight = new SingleFlight();
        int runs = 0;
        bool? second = null;

        flight.TryRun(() =>
        {
            runs++;
            // A tick lands while the first sample is still running.
            second = flight.TryRun(() => runs++);
        });

        Assert.Equal(1, runs);
        Assert.False(second);
    }

    [Fact]
    public void RunsAgainOnceTheOperationFinished()
    {
        var flight = new SingleFlight();
        int runs = 0;

        Assert.True(flight.TryRun(() => runs++));
        Assert.True(flight.TryRun(() => runs++));
        Assert.Equal(2, runs);
    }

    [Fact]
    public void ClearsTheFlagWhenTheOperationThrows()
    {
        var flight = new SingleFlight();

        Assert.Throws<InvalidOperationException>(() =>
            flight.TryRun(() => throw new InvalidOperationException("boom")));

        // A failed sample must not wedge every later tick.
        Assert.False(flight.IsRunning);
        Assert.True(flight.TryRun(() => { }));
    }

    [Fact]
    public void NeverRunsTwoOperationsAtOnce()
    {
        var flight = new SingleFlight();
        int concurrent = 0;
        int maxConcurrent = 0;
        int started = 0;
        var gate = new object();

        // A slow operation with many ticks arriving: the guard must keep concurrency at 1.
        var threads = new List<Thread>();
        for (int i = 0; i < 8; i++)
        {
            var t = new Thread(() =>
            {
                for (int tick = 0; tick < 50; tick++)
                {
                    flight.TryRun(() =>
                    {
                        lock (gate)
                        {
                            concurrent++;
                            started++;
                            if (concurrent > maxConcurrent) maxConcurrent = concurrent;
                        }

                        Thread.Sleep(1);

                        lock (gate) { concurrent--; }
                    });
                }
            });
            threads.Add(t);
            t.Start();
        }

        foreach (var t in threads) t.Join();

        Assert.Equal(1, maxConcurrent);
        Assert.True(started > 0);
    }
}
