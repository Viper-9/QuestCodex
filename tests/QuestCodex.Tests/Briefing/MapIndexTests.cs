using QuestCodex.Briefing;

namespace QuestCodex.Tests.Briefing;

public class MapIndexTests
{
    private static MapIndex Real() => MapIndex.Parse(File.ReadAllText(TestPaths.MapIndexJson));

    [Fact]
    public void FolderFor_matches_internal_names_ignoring_case()
    {
        var index = Real();
        Assert.Equal("sandbox", index.FolderFor("sandbox"));
        Assert.Equal("sandbox", index.FolderFor("sandbox_high"));
        Assert.Equal("factory4_day", index.FolderFor("factory4_day"));
        Assert.Equal("rezervbase", index.FolderFor("rezervbase"));
        Assert.Null(index.FolderFor("modmap"));
    }

    [Fact]
    public void WithVariants_swaps_folder_of_active_variant_only()
    {
        var index = Real();
        Assert.Equal("interchange-manimal", index.WithVariants(new Dictionary<string, string> { ["interchange"] = "manimal" }).FolderFor("interchange"));
        Assert.Equal("interchange", index.WithVariants(new Dictionary<string, string> { ["interchange"] = "unknown" }).FolderFor("interchange"));
        Assert.Same(index, index.WithVariants(new Dictionary<string, string>()));
    }

    [Fact]
    public void Empty_index_has_no_folders() => Assert.Null(MapIndex.Empty.FolderFor("bigmap"));
}
