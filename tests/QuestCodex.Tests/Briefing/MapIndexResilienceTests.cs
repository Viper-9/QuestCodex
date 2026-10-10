using QuestCodex.Briefing;

namespace QuestCodex.Tests.Briefing;

public class MapIndexResilienceTests
{
    [Fact]
    public void Parse_without_maps_is_empty_and_does_not_throw()
    {
        var index = MapIndex.Parse("{}");
        Assert.Empty(index.Maps);
        Assert.Null(index.FolderFor("woods"));
    }

    [Fact]
    public void Load_returns_empty_for_garbage_and_missing_file()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var bad = Path.Combine(dir.FullName, "index.json");
            File.WriteAllText(bad, "{ not json");
            Assert.Empty(MapIndex.Load(bad).Maps);
            Assert.Empty(MapIndex.Load(Path.Combine(dir.FullName, "missing.json")).Maps);
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public void Culture_comparer_falls_back_to_invariant_for_unknown_culture()
        => Assert.Same(RaidNeeds.NameOrder, RaidNeeds.CultureOrDefault("xx-INVALID-zz"));
}
