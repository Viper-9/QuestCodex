using QuestCodex.Catalog.Locations;
using QuestCodex.Catalog.Models;

namespace QuestCodex.Tests.Catalog;

/// <summary>보스 출현(18 스펙): 서버 표에서 바닐라 이름만 골라 구역별 마커 하나로 묶는다.</summary>
public class BossSpawnBuilderTests
{
    private static BossSpawnRow Row(string name, double chance, string zone, bool conditional = false) => new(name, chance, zone, conditional);

    private static BossZonePoint Pt(string zone, double x, bool bossSpot = true) => new(zone, new MapPoint(x, 0, 0), bossSpot);

    private static MapBossSpawns Build(IReadOnlyList<BossSpawnRow> rows, params BossZonePoint[] points)
        => BossSpawnBuilder.Build(new Dictionary<string, LocationBosses> { ["bigmap"] = new(rows, points) })["bigmap"];

    [Fact]
    public void Only_vanilla_bosses_are_kept()
    {
        var result = Build(
            [Row("bossBully", 39, "A"), Row("pmcUSEC", 50, "A"), Row("pmcBEAR", 50, "A"), Row("bossWedge", 25, "A"), Row("ISBHeavy", 20, "A")],
            Pt("A", 0));

        Assert.Equal(["bossBully"], Assert.Single(result.Zones).Bosses.Select(b => b.Key));
    }

    [Fact]
    public void Chance_is_split_between_the_candidate_zones()
    {
        var result = Build([Row("bossBully", 39, "A,B,C")], Pt("A", 0), Pt("B", 1), Pt("C", 2));

        Assert.Equal(3, result.Zones.Count);
        Assert.All(result.Zones, z =>
        {
            var boss = Assert.Single(z.Bosses);
            Assert.Equal(13, boss.Chance);
            Assert.Equal(39, boss.MapChance);
        });
    }

    [Fact]
    public void A_zone_listed_twice_counts_twice()
    {
        var result = Build([Row("bossSanitar", 60, "A,A,B")], Pt("A", 0), Pt("B", 1));

        Assert.Equal(40, result.Zones.Single(z => z.Zone == "A").Bosses[0].Chance);
        Assert.Equal(20, result.Zones.Single(z => z.Zone == "B").Bosses[0].Chance);
    }

    [Fact]
    public void Rows_of_the_same_boss_combine_as_independent_rolls()
    {
        var boss = Assert.Single(Assert.Single(Build([Row("pmcBot", 50, "A"), Row("pmcBot", 50, "A")], Pt("A", 0)).Zones).Bosses);

        Assert.Equal(75, boss.Chance);
        Assert.Equal(75, boss.MapChance);
    }

    [Fact]
    public void Conditional_rows_stay_apart_from_unconditional_ones()
    {
        var bosses = Assert.Single(Build([Row("pmcBot", 60, "A"), Row("pmcBot", 45, "A", conditional: true)], Pt("A", 0)).Zones).Bosses;

        Assert.Equal([(60, false), (45, true)], bosses.Select(b => (b.Chance, b.Conditional)));
    }

    [Fact]
    public void Spawns_without_a_known_place_go_to_the_unplaced_list()
    {
        var result = Build([Row("bossPartisan", 15, "", conditional: true), Row("bossKnight", 20, "Nowhere")], Pt("A", 0));

        Assert.Empty(result.Zones);
        Assert.Equal([("bossKnight", 20, false), ("bossPartisan", 15, true)], result.Unplaced.Select(b => (b.Key, b.Chance, b.Conditional)));
    }

    [Fact]
    public void Marker_sits_on_the_boss_spawn_point_nearest_the_middle()
    {
        var result = Build([Row("bossBully", 39, "A")], Pt("A", 0), Pt("A", 8), Pt("A", 10), Pt("A", 5, bossSpot: false));

        Assert.Equal(8, Assert.Single(result.Zones).Position.X);
    }

    [Fact]
    public void A_zone_without_boss_spawn_points_uses_its_other_points()
    {
        var result = Build([Row("bossBully", 39, "A")], Pt("A", 3, bossSpot: false));

        Assert.Equal(3, Assert.Single(result.Zones).Position.X);
    }

    [Fact]
    public void Disabled_spawns_and_empty_maps_are_dropped()
    {
        var all = BossSpawnBuilder.Build(new Dictionary<string, LocationBosses>
        {
            ["bigmap"] = new([Row("gifter", 0, "A")], [Pt("A", 0)]),
            ["woods"] = new([Row("bossKojaniy", 30, "A")], [Pt("A", 0)]),
        });

        Assert.Equal(["woods"], all.Keys);
    }

    [Fact]
    public void Real_bosses_list_before_minor_ones_then_by_chance()
    {
        var bosses = Assert.Single(Build(
            [Row("pmcBot", 80, "A"), Row("sectantPriest", 20, "A"), Row("bossKnight", 40, "A")], Pt("A", 0)).Zones).Bosses;

        Assert.Equal(["bossKnight", "sectantPriest", "pmcBot"], bosses.Select(b => b.Key));
        Assert.Equal(["boss", "boss", "minor"], bosses.Select(b => b.Kind));
    }
}
