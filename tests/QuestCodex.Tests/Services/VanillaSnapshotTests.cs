using QuestCodex.Services;

namespace QuestCodex.Tests.Services;

public class VanillaSnapshotTests
{
    [Fact]
    public void Parse_reads_version_and_ids()
    {
        const string json = """{ "sptVersion": "4.1.5", "questIds": ["b", "a"] }""";

        var (version, ids) = VanillaSnapshot.Parse(json);

        Assert.Equal("4.1.5", version);
        Assert.Equal(2, ids.Count);
        Assert.Contains("a", ids);
    }

    [Fact]
    public void Parse_rejects_missing_fields()
    {
        Assert.ThrowsAny<Exception>(() => VanillaSnapshot.Parse("""{ "questIds": [] }"""));
        Assert.ThrowsAny<Exception>(() => VanillaSnapshot.Parse("""{ "sptVersion": "x" }"""));
    }

    [Fact]
    public void Bundled_snapshot_file_exists_next_to_assembly_and_parses()
    {
        // csproj 가 Data\** 를 출력에 복사하므로 테스트 실행 폴더에도 존재해야 한다.
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "vanilla-quest-ids.json");
        Assert.True(File.Exists(path), $"missing {path}");

        var (version, ids) = VanillaSnapshot.Parse(File.ReadAllText(path));

        Assert.Matches(@"^\d+\.\d+\.\d+$", version);
        Assert.True(ids.Count > 300, $"only {ids.Count} ids");
        Assert.All(ids, id => Assert.Matches("^[0-9a-f]{24}$", id));
    }
}
