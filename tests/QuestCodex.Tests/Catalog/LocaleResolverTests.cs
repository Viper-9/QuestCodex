using QuestCodex.Catalog;

namespace QuestCodex.Tests.Catalog;

public class LocaleResolverTests
{
    private static readonly Dictionary<string, string> Kr = new() { ["q1 name"] = "탄창 모으기", ["blank"] = "  " };
    private static readonly Dictionary<string, string> En = new() { ["q1 name"] = "Collect mags", ["q2 name"] = "Only in en", ["blank"] = "en blank" };

    [Fact]
    public void Uses_requested_locale_first()
    {
        var r = new LocaleResolver(Kr, En);
        var text = r.Resolve("q1 name", "q1", out var fellBack);
        Assert.Equal("탄창 모으기", text);
        Assert.False(fellBack);
    }

    [Fact]
    public void Falls_back_to_en_and_reports_it()
    {
        var r = new LocaleResolver(Kr, En);
        var text = r.Resolve("q2 name", "q2", out var fellBack);
        Assert.Equal("Only in en", text);
        Assert.True(fellBack);
    }

    [Fact]
    public void Falls_back_to_raw_text_when_missing_everywhere()
    {
        var r = new LocaleResolver(Kr, En);
        var text = r.Resolve("q3 name", "q3", out var fellBack);
        Assert.Equal("q3", text);
        Assert.True(fellBack);
    }

    [Fact]
    public void Whitespace_value_counts_as_missing()
    {
        var r = new LocaleResolver(Kr, En);
        Assert.Equal("en blank", r.Resolve("blank", "x", out _));
    }

    [Fact]
    public void TryResolve_returns_null_when_missing()
    {
        var r = new LocaleResolver(Kr, En);
        Assert.Null(r.TryResolve("nope"));
        Assert.Equal("Collect mags", new LocaleResolver(new Dictionary<string, string>(), En).TryResolve("q1 name"));
    }
}
