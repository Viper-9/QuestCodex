using QuestCodex.Services;

namespace QuestCodex.Tests.Services;

public class RequiredKeysSnapshotTests
{
    [Fact]
    public void Parse_reads_key_groups_by_condition_and_drops_empty_ones()
    {
        const string json = """{ "source": "x", "objectives": { "c1": [["k1"], ["k2", "k3"]], "c2": [[]] } }""";

        var keys = RequiredKeysSnapshot.Parse(json);

        Assert.Equal(["c1"], keys.Keys);
        Assert.Equal(["k1"], keys["c1"][0]);
        Assert.Equal(["k2", "k3"], keys["c1"][1]);
    }

    [Fact]
    public void Bundled_snapshot_file_exists_next_to_assembly_and_parses()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "required-keys.json");
        Assert.True(File.Exists(path), $"missing {path}");

        var keys = RequiredKeysSnapshot.Parse(File.ReadAllText(path));

        Assert.True(keys.Count > 50, $"only {keys.Count} objectives");
        Assert.All(keys, kv => Assert.Matches("^[0-9a-f]{24}$", kv.Key));
        Assert.All(keys.Values.SelectMany(g => g).SelectMany(g => g), tpl => Assert.Matches("^[0-9a-f]{24}$", tpl));
    }
}
