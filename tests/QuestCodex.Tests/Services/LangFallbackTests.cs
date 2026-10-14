using QuestCodex.Services;
using static QuestCodex.Tests.Fixtures;

namespace QuestCodex.Tests.Services;

public class LangFallbackTests
{
    private static readonly IReadOnlySet<string> Supported = new HashSet<string> { "en", "kr", "es", "es-mx", "ru" };

    [Theory]
    [InlineData("kr", "kr")]
    [InlineData("KR", "kr")]
    [InlineData(" kr ", "kr")]
    [InlineData("es-mx", "es-mx")]
    [InlineData("ES-MX", "es-mx")]
    [InlineData("kr-en", "kr")]
    [InlineData("es-xx", "es")]
    [InlineData("ru-xx", "ru")]
    [InlineData("xx-yy", "en")]
    [InlineData("xx", "en")]
    [InlineData("-kr", "en")]
    [InlineData("", "en")]
    [InlineData("   ", "en")]
    [InlineData(null, "en")]
    public void Picks_the_closest_supported_lang(string? requested, string expected)
        => Assert.Equal(expected, LangFallback.Pick(requested, Supported));

    [Fact]
    public void Without_en_an_unknown_lang_has_no_fallback()
    {
        var noEn = new HashSet<string> { "kr" };
        Assert.Null(LangFallback.Pick("xx", noEn));
        Assert.Equal("kr", LangFallback.Pick("kr-en", noEn));
    }

    [Fact]
    public void Logs_a_fallback_once_per_pair()
    {
        // 로그 기록은 프로세스 전체에서 공유되므로 다른 테스트와 겹치지 않는 코드를 쓴다.
        var log = new RecordingLogger<LangFallbackTests>();
        Assert.Equal("kr", LangFallback.Resolve("kr-zz", Supported, log));
        Assert.Equal("kr", LangFallback.Resolve("KR-ZZ", Supported, log));
        Assert.Equal("en", LangFallback.Resolve("zz-log", Supported, log));

        Assert.Equal(2, log.Entries.Count);
        Assert.Contains("'kr-zz'", log.Entries[0].Message);
        Assert.Contains("'kr'", log.Entries[0].Message);
        Assert.Contains("'en'", log.Entries[1].Message);
    }

    [Fact]
    public void Does_not_log_exact_case_only_or_empty_requests()
    {
        var log = new RecordingLogger<LangFallbackTests>();
        LangFallback.Resolve("kr", Supported, log);
        LangFallback.Resolve("KR", Supported, log);
        LangFallback.Resolve(null, Supported, log);
        LangFallback.Resolve(" ", Supported, log);
        Assert.Empty(log.Entries);
    }
}
