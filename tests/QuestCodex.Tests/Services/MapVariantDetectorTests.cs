using QuestCodex.Services;

namespace QuestCodex.Tests.Services;

public class MapVariantDetectorTests
{
    [Fact]
    public void No_map_mod_means_no_variants()
        => Assert.Empty(MapVariantDetector.Detect(["com.viper.questcodex", "com.manimal.icebreaker", null]));

    [Fact]
    public void Manimal_interchange_turns_on_the_expanded_interchange()
        => Assert.Equal(
            new Dictionary<string, string> { ["interchange"] = "manimal" },
            MapVariantDetector.Detect(["com.viper.questcodex", "Com.Manimal.Interchange"]));
}
