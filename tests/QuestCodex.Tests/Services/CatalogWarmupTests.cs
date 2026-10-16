using QuestCodex.Services;
using static QuestCodex.Tests.Fixtures;

namespace QuestCodex.Tests.Services;

/// <summary>서버 시작 후 첫 업데이트에서 기록된 언어를 미리 만든다(16 catalog-warmup 스펙 §3).</summary>
public class CatalogWarmupTests
{
    private sealed class FakeSource : ICatalogWarmSource
    {
        public IReadOnlyList<string> RecentLangs { get; init; } = [];
        public IReadOnlySet<string> SupportedLangs { get; init; } = new HashSet<string> { "en", "kr" };
        public HashSet<string> Failing { get; init; } = [];
        public List<string> Built { get; } = [];

        public List<ThreadPriority> Priorities { get; } = [];

        public void Prebuild(string lang)
        {
            Priorities.Add(Thread.CurrentThread.Priority);
            if (Failing.Contains(lang)) throw new InvalidOperationException("boom");
            Built.Add(lang);
        }
    }

    [Fact]
    public async Task Builds_on_a_below_normal_background_thread()
    {
        var source = new FakeSource { RecentLangs = ["kr"] };
        await Tick(new CatalogWarmup(source, new RecordingLogger<CatalogWarmup>()));
        Assert.Equal([ThreadPriority.BelowNormal], source.Priorities);
    }

    private static async Task<bool> Tick(CatalogWarmup warmup)
    {
        var result = await warmup.OnUpdateAsync(0, CancellationToken.None);
        if (warmup.Warming is { } t) await t;
        return result;
    }

    [Fact]
    public async Task Builds_recorded_langs_in_order_only_on_the_first_update()
    {
        var source = new FakeSource { RecentLangs = ["kr", "en"] };
        var log = new RecordingLogger<CatalogWarmup>();
        var warmup = new CatalogWarmup(source, log);

        Assert.True(await Tick(warmup));
        Assert.True(await Tick(warmup));

        Assert.Equal(["kr", "en"], source.Built);
        Assert.Collection(log.Entries,
            e => Assert.Equal(("Info", "[QuestCodex] catalog warm-up started (kr, en)"), e),
            e => Assert.StartsWith("[QuestCodex] catalog warm-up finished in ", e.Message));
    }

    [Fact]
    public async Task No_record_does_nothing_and_logs_nothing()
    {
        var source = new FakeSource();
        var log = new RecordingLogger<CatalogWarmup>();
        Assert.True(await Tick(new CatalogWarmup(source, log)));
        Assert.Empty(source.Built);
        Assert.Empty(log.Entries);
    }

    [Fact]
    public async Task Unsupported_langs_are_skipped()
    {
        var source = new FakeSource { RecentLangs = ["xx", "kr"] };
        await Tick(new CatalogWarmup(source, new RecordingLogger<CatalogWarmup>()));
        Assert.Equal(["kr"], source.Built);
    }

    [Fact]
    public async Task A_failing_lang_is_logged_and_the_rest_still_build()
    {
        var source = new FakeSource { RecentLangs = ["kr", "en"], Failing = ["kr"] };
        var log = new RecordingLogger<CatalogWarmup>();
        Assert.True(await Tick(new CatalogWarmup(source, log)));
        Assert.Equal(["en"], source.Built);
        Assert.Contains(log.Entries, e => e.Level == "Warning" && e.Message.Contains("'kr'"));
    }
}
