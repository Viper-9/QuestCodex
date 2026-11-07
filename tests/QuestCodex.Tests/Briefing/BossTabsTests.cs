using QuestCodex.Briefing;
using QuestCodex.Briefing.Models;
using QuestCodex.Catalog.Models;

namespace QuestCodex.Tests.Briefing;

// 웹 mapProjection.test.ts 의 bossesForTab 사례를 같은 기대값으로 옮기고 몇 가지를 더했다.
public class BossTabsTests
{
    private static MapBossEntry Boss(string key, int chance, bool conditional = false, bool rotating = false)
        => new(key, "boss", chance, chance, conditional, rotating);

    private static MapBossZone Zone(string name, params MapBossEntry[] bosses) => new(name, new MapPoint(1, 0, 2), bosses);

    private static MapBossSpawns Spawns(MapBossZone[] zones, params MapBossEntry[] unplaced) => new(zones, unplaced);

    private static SortedDictionary<string, MapBossSpawns> Catalog(params (string Map, MapBossSpawns Spawns)[] maps)
    {
        var d = new SortedDictionary<string, MapBossSpawns>(StringComparer.Ordinal);
        foreach (var (map, spawns) in maps) d[map] = spawns;
        return d;
    }

    private static (string Key, string? OnlyOn)[] Lines(IEnumerable<BriefingBoss> bosses) => bosses.Select(b => (b.Key, b.OnlyOn)).ToArray();

    [Fact]
    public void Only_maps_of_the_tab_are_collected()
    {
        var result = BossTabs.ForTab(Catalog(
            ("bigmap", Spawns([Zone("ZoneScavBase", Boss("bossBully", 13))], Boss("bossPartisan", 15, conditional: true))),
            ("woods", Spawns([Zone("ZoneRoad", Boss("bossKojaniy", 30))]))), "bigmap", 1);

        Assert.Equal(["ZoneScavBase"], result.Zones.Select(z => z.Zone));
        Assert.Equal(["bossPartisan"], result.Unplaced.Select(b => b.Key));
        Assert.True(result.Unplaced[0].Conditional);
    }

    [Fact]
    public void Same_value_on_both_maps_is_one_line_and_one_sided_boss_names_its_map()
    {
        var result = BossTabs.ForTab(Catalog(
            ("factory4_day", Spawns([Zone("BotZone", Boss("bossTagilla", 30))])),
            ("factory4_night", Spawns([Zone("BotZone", Boss("bossTagilla", 30), Boss("sectantPriest", 20))]))), "factory4_day", 2);

        var zone = Assert.Single(result.Zones);
        Assert.Equal([("bossTagilla", (string?)null), ("sectantPriest", "factory4_night")], Lines(zone.Bosses));
    }

    [Fact]
    public void Different_values_give_one_line_per_map()
    {
        var result = BossTabs.ForTab(Catalog(
            ("factory4_day", Spawns([Zone("BotZone", Boss("bossTagilla", 30))])),
            ("factory4_night", Spawns([Zone("BotZone", Boss("bossTagilla", 50))]))), "factory4_day", 2);

        Assert.Equal([(30, "factory4_day"), (50, "factory4_night")], result.Zones[0].Bosses.Select(b => (b.MapChance, b.OnlyOn)));
    }

    [Fact]
    public void Different_rotating_flag_gives_one_line_per_map()
    {
        var result = BossTabs.ForTab(Catalog(
            ("factory4_day", Spawns([], Boss("bossKnight", 30, rotating: true))),
            ("factory4_night", Spawns([], Boss("bossKnight", 30)))), "factory4_day", 2);

        Assert.Equal([("bossKnight", "factory4_day"), ("bossKnight", (string?)"factory4_night")], Lines(result.Unplaced));
    }

    [Fact]
    public void Missing_pair_map_marks_the_boss_with_its_map()
    {
        // 그라운드 제로 저레벨처럼 보스가 없어 카탈로그에 안 오는 짝 맵이 있어도 "모든 맵" 으로 보지 않는다.
        var result = BossTabs.ForTab(Catalog(
            ("sandbox_high", Spawns([Zone("ZoneSandbox", Boss("sectantPriest", 44))]))), "sandbox", 2);

        Assert.Equal(["sandbox_high"], result.Zones[0].Bosses.Select(b => b.OnlyOn));
    }

    [Fact]
    public void Same_zone_name_is_one_marker_at_the_first_position_in_first_seen_order()
    {
        var result = BossTabs.ForTab(Catalog(
            ("factory4_day", Spawns([Zone("BotZone", Boss("bossTagilla", 30)), Zone("BotZoneGate", Boss("bossTagilla", 30))])),
            ("factory4_night", Spawns([
                new MapBossZone("BotZone", new MapPoint(9, 9, 9), [Boss("bossTagilla", 30)]),
                Zone("BotZoneBasement", Boss("sectantPriest", 20))]))), "factory4_day", 2);

        Assert.Equal(["BotZone", "BotZoneGate", "BotZoneBasement"], result.Zones.Select(z => z.Zone));
        Assert.Equal(new MapPoint(1, 0, 2), result.Zones[0].Position);
        Assert.Equal([("bossTagilla", (string?)null)], Lines(result.Zones[0].Bosses));
        // 주간에만 있는 구역의 보스는 주간 표시
        Assert.Equal([("bossTagilla", (string?)"factory4_day")], Lines(result.Zones[1].Bosses));
    }

    [Fact]
    public void Conditional_and_unconditional_rows_of_one_boss_stay_apart()
    {
        var result = BossTabs.ForTab(Catalog(
            ("bigmap", Spawns([], Boss("bossPartisan", 15), Boss("bossPartisan", 40, conditional: true)))), "bigmap", 1);

        Assert.Equal([(false, 15), (true, 40)], result.Unplaced.Select(b => (b.Conditional, b.MapChance)));
        Assert.All(result.Unplaced, b => Assert.Null(b.OnlyOn));
    }

    [Fact]
    public void Map_count_larger_than_received_maps_marks_every_line()
    {
        var result = BossTabs.ForTab(Catalog(
            ("factory4_day", Spawns([Zone("BotZone", Boss("bossTagilla", 30))], Boss("bossKnight", 10)))), "factory4_day", 2);

        Assert.Equal(["factory4_day"], result.Zones[0].Bosses.Select(b => b.OnlyOn));
        Assert.Equal(["factory4_day"], result.Unplaced.Select(b => b.OnlyOn));
    }

    [Fact]
    public void Missing_catalog_or_tab_gives_empty_result()
    {
        Assert.Empty(BossTabs.ForTab(null, "bigmap", 1).Zones);
        Assert.Empty(BossTabs.ForTab(null, "bigmap", 1).Unplaced);
        var other = BossTabs.ForTab(Catalog(("woods", Spawns([Zone("ZoneRoad", Boss("bossKojaniy", 30))]))), "labyrinth", 1);
        Assert.Empty(other.Zones);
        Assert.Empty(other.Unplaced);
    }

    [Fact]
    public void Kind_and_map_chance_are_carried_over()
    {
        var minor = new MapBossEntry("pmcBot", "minor", 5, 40, false, false);
        var result = BossTabs.ForTab(Catalog(("laboratory", Spawns([Zone("BotZoneFloor1", minor)]))), "laboratory", 1);

        var line = Assert.Single(result.Zones[0].Bosses);
        Assert.Equal(new BriefingBoss("pmcBot", "minor", 40, false, false, null), line);
    }
}
