using QuestCodex.Services;

namespace QuestCodex.Tests.Services;

/// <summary>성공한 값만 기억하는 Lazy(16 catalog-warmup 스펙 §5).</summary>
public class RetryingLazyTests
{
    [Fact]
    public void Success_is_remembered()
    {
        var calls = 0;
        var lazy = new RetryingLazy<string>(() => $"v{++calls}");
        Assert.Equal(("v1", "v1"), (lazy.Value, lazy.Value));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Failure_is_not_remembered_so_the_next_read_retries()
    {
        var calls = 0;
        var lazy = new RetryingLazy<string>(() => ++calls == 1 ? throw new InvalidOperationException("boom") : "ok");
        Assert.Throws<InvalidOperationException>(() => lazy.Value);
        Assert.Equal("ok", lazy.Value);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Concurrent_reads_wait_for_one_run()
    {
        var calls = 0;
        using var gate = new ManualResetEventSlim();
        var lazy = new RetryingLazy<string>(() =>
        {
            Interlocked.Increment(ref calls);
            gate.Wait();
            return "v";
        });

        var readers = Enumerable.Range(0, 8).Select(_ => Task.Run(() => lazy.Value)).ToList();
        await Task.Delay(50);
        gate.Set();
        Assert.All(await Task.WhenAll(readers), v => Assert.Equal("v", v));
        Assert.Equal(1, calls);
    }
}
