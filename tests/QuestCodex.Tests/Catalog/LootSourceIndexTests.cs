using QuestCodex.Catalog.Loot;

namespace QuestCodex.Tests.Catalog;

/// <summary>카파 제출 아이템 출처(12 kappa-loot-sources 스펙 §2.2): 하나 열었을 때 들어 있을 확률, 봇 묶음.</summary>
public class LootSourceIndexTests
{
    private static StaticLootTable Table(IReadOnlyList<(int, double)> counts, params (string, double)[] items) => new(counts, items);

    private static Dictionary<string, IReadOnlySet<string>> NoBots() => new();

    [Fact]
    public void Chance_is_one_minus_miss_over_count_distribution()
    {
        // p = 0.1, 개수 1개(50%) · 2개(50%) → 0.5·0.1 + 0.5·(1 − 0.9²) = 0.05 + 0.095
        var chance = LootSourceIndex.Chance(0.1, [(1, 1), (2, 1)], 2, 1);
        Assert.Equal(0.145, chance, 6);
    }

    [Fact]
    public void Multiplier_scales_item_count()
    {
        // 개수 1 × 배율 2 → 2번 뽑음
        Assert.Equal(1 - 0.9 * 0.9, LootSourceIndex.Chance(0.1, [(1, 1)], 1, 2), 6);
        Assert.Equal(0, LootSourceIndex.Chance(0.1, [(1, 1)], 1, 0), 6);
    }

    [Fact]
    public void Averages_over_maps_weighted_by_placed_count_and_skips_unplaced_containers()
    {
        var safe = Table([(1, 1)], ("rooster", 1), ("other", 9));          // 10%
        var richSafe = Table([(1, 1)], ("rooster", 3), ("other", 7));      // 30%
        var maps = new[]
        {
            new LootMapInput("a", new Dictionary<string, StaticLootTable> { ["safe"] = safe, ["bag"] = safe }, new Dictionary<string, int> { ["safe"] = 3 }, 1),
            new LootMapInput("b", new Dictionary<string, StaticLootTable> { ["safe"] = richSafe }, new Dictionary<string, int> { ["safe"] = 1 }, 1),
        };

        var result = LootSourceIndex.Build(maps, NoBots());

        var containers = result["rooster"].Containers;
        Assert.Single(containers);                         // bag 은 어느 맵에도 안 놓였다
        Assert.Equal("safe", containers[0].ContainerTpl);
        Assert.Equal((3 * 0.1 + 1 * 0.3) / 4, containers[0].Chance, 6);
    }

    [Fact]
    public void Containers_are_sorted_by_chance()
    {
        var maps = new[]
        {
            new LootMapInput("a", new Dictionary<string, StaticLootTable>
            {
                ["bag"] = Table([(1, 1)], ("x", 1), ("y", 99)),
                ["safe"] = Table([(1, 1)], ("x", 1), ("y", 1)),
            }, new Dictionary<string, int> { ["bag"] = 5, ["safe"] = 1 }, 1),
        };

        var result = LootSourceIndex.Build(maps, NoBots());

        Assert.Equal(["safe", "bag"], result["x"].Containers.Select(c => c.ContainerTpl));
    }

    [Fact]
    public void Bot_groups_are_merged_in_display_order()
    {
        var bots = new Dictionary<string, IReadOnlySet<string>>
        {
            ["pmcUSEC"] = new HashSet<string> { "axe" },
            ["assault"] = new HashSet<string> { "axe" },
            ["followerBully"] = new HashSet<string> { "axe", "wallet" },
            ["bossKilla"] = new HashSet<string> { "wallet" },
        };

        var result = LootSourceIndex.Build([], bots);

        Assert.Equal(["scav", "pmc", "boss"], result["axe"].Bots);
        Assert.Empty(result["axe"].Containers);
        Assert.Equal(["boss"], result["wallet"].Bots);
    }

    [Theory]
    [InlineData("assault", "scav")]
    [InlineData("crazyAssaultEvent", "scav")]
    [InlineData("bear", "pmc")]
    [InlineData("bossTagillaAgro", "boss")]
    [InlineData("tagillaHelperAgro", "boss")]
    [InlineData("pmcBot", "raider")]
    [InlineData("exUsec", "raider")]
    [InlineData("sectantWarrior", "cultist")]
    [InlineData("gifter", "other")]
    public void BotGroup_maps_bot_types(string type, string group)
        => Assert.Equal(group, LootSourceIndex.BotGroup(type));
}
