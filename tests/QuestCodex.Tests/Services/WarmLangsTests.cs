using QuestCodex.Services;

namespace QuestCodex.Tests.Services;

/// <summary>미리 만들 언어 기록(16 catalog-warmup 스펙 §4): 갱신 규칙, 파일 왕복, 망가진 파일은 기록 없음.</summary>
public sealed class WarmLangsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "qc-warmlangs-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "cache", "warm-langs.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void New_lang_goes_first()
    {
        Assert.Equal(["kr"], WarmLangs.Add([], "kr"));
        Assert.Equal(["en", "kr"], WarmLangs.Add(["kr"], "en"));
    }

    [Fact]
    public void Keeps_at_most_two_dropping_the_oldest()
    {
        Assert.Equal(["jp", "en"], WarmLangs.Add(["en", "kr"], "jp"));
    }

    [Fact]
    public void Known_lang_is_no_change_so_alternating_requests_do_not_rewrite_the_file()
    {
        Assert.Null(WarmLangs.Add(["en", "kr"], "kr"));
        Assert.Null(WarmLangs.Add(["en", "kr"], "en"));
    }

    [Fact]
    public void Write_then_read_round_trips()
    {
        WarmLangs.Write(FilePath, ["kr", "en"]);
        Assert.Equal(["kr", "en"], WarmLangs.Read(FilePath));
    }

    [Fact]
    public void Missing_or_broken_file_is_no_record()
    {
        Assert.Empty(WarmLangs.Read(FilePath));
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, "{ not json");
        Assert.Empty(WarmLangs.Read(FilePath));
        File.WriteAllText(FilePath, """{ "langs": null }""");
        Assert.Empty(WarmLangs.Read(FilePath));
    }
}
