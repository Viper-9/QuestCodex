using QuestCodex.Catalog.Locations;
using QuestCodex.Catalog.Models;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Utils.Json;
using static QuestCodex.Tests.Fixtures;
using PointTable = System.Collections.Generic.IReadOnlyDictionary<string, System.Collections.Generic.IReadOnlyDictionary<string, System.Collections.Generic.IReadOnlyList<QuestCodex.Catalog.Models.MapPoint>>>;
using AreaTable = System.Collections.Generic.IReadOnlyDictionary<string, System.Collections.Generic.IReadOnlyDictionary<string, System.Collections.Generic.IReadOnlyList<QuestCodex.Catalog.Models.MapArea>>>;

namespace QuestCodex.Tests.Catalog;

public class LocationResolverTests
{
    private const string CustomsId = "56f40101d2720b2a4d8b45d6";
    private static readonly MapPoint Fuel4Customs = new(-334.93, 2.22, -163.46);
    private static readonly MapPoint Fuel4Reserve = new(-334.93, -101.46, -163.46);
    private static readonly MongoId QuestItemTpl = Id(700);
    private static readonly MongoId LootTpl = Id(701);

    /// <summary>(map, id, point) 목록 → map → id → points.</summary>
    private static PointTable Table(params (string Map, string Id, MapPoint Point)[] rows)
        => rows.GroupBy(r => r.Map).ToDictionary(
            g => g.Key,
            g => (IReadOnlyDictionary<string, IReadOnlyList<MapPoint>>)g.GroupBy(r => r.Id)
                .ToDictionary(x => x.Key, x => (IReadOnlyList<MapPoint>)x.Select(r => r.Point).ToList()));

    private static LocationResolver Resolver(PointTable? zones = null, PointTable? spawns = null, AreaTable? areas = null)
        => new(
            zones ?? Table(),
            spawns ?? Table(),
            new Dictionary<string, string> { [CustomsId] = "bigmap" },
            new Dictionary<MongoId, TemplateItem>
            {
                [QuestItemTpl] = new() { Id = QuestItemTpl, Properties = new() { QuestItem = true } },
                [LootTpl] = new() { Id = LootTpl, Properties = new() { QuestItem = false } },
            },
            areas);

    private static AreaTable Areas(params (string Map, string Id, MapArea Area)[] rows)
    {
        var builder = new AreaTableBuilder();
        foreach (var (map, id, area) in rows) builder.Add(map, id, area);
        return builder.Build();
    }

    private static QuestCondition Beacon(string zoneId) => new()
    {
        Id = Id(1), ConditionType = "PlaceBeacon", DynamicLocale = false, ZoneId = zoneId,
    };

    private static QuestCondition Counter(params QuestConditionCounterCondition[] subs) => new()
    {
        Id = Id(2), ConditionType = "CounterCreator", DynamicLocale = false,
        Counter = new QuestConditionCounter { Conditions = [.. subs] },
    };

    private static QuestConditionCounterCondition Sub(string type, string? target = null, List<string>? zoneIds = null) => new()
    {
        ConditionType = type, Target = target is null ? null : new ListOrT<string>(null, target), Zones = zoneIds,
    };

    private static QuestCondition FindItem(MongoId tpl) => new()
    {
        Id = Id(3), ConditionType = "FindItem", DynamicLocale = false, Target = new ListOrT<string>([tpl.ToString()], null),
    };

    [Theory]
    [InlineData(CustomsId, "bigmap")]
    [InlineData("TarkovStreets", "tarkovstreets")]
    [InlineData("any", null)]
    [InlineData("marathon", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Quest_map_comes_from_location_id_or_name(string? location, string? expected)
        => Assert.Equal(expected, Resolver().QuestMap(location));

    [Fact]
    public void Rule1_quest_map_hit_drops_copies_on_other_maps()
    {
        var resolver = Resolver(Table(("bigmap", "fuel4", Fuel4Customs), ("rezervbase", "fuel4", Fuel4Reserve)));
        var warnings = new List<CatalogWarning>();

        var locations = resolver.Resolve(Beacon("fuel4"), "bigmap", warnings, "q");

        var only = Assert.Single(locations);
        Assert.Equal("bigmap", only.Map);
        Assert.Equal([Fuel4Customs], only.Points);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Rule2_no_quest_map_keeps_every_map()
    {
        var p = new MapPoint(1, 2, 3);
        var resolver = Resolver(Table(("woods", "place", p), ("bigmap", "place", p), ("shoreline", "place", p)));

        var locations = resolver.Resolve(Counter(Sub("VisitPlace", "place")), null, [], "q");

        Assert.Equal(["bigmap", "shoreline", "woods"], locations.Select(l => l.Map));
    }

    [Fact]
    public void Top_level_visit_place_resolves_its_target_zone()
    {
        // Icebreaker "Wiring the Vessel": VisitPlace as a top-level condition, not inside a CounterCreator
        var p = new MapPoint(-13.66, 17.18, 6.68);
        var resolver = Resolver(Table(("icebreaker", "fix_element_one", p)));
        var visit = new QuestCondition
        {
            Id = Id(4), ConditionType = "VisitPlace", DynamicLocale = false, Target = new ListOrT<string>(null, "fix_element_one"),
        };
        var warnings = new List<CatalogWarning>();

        var only = Assert.Single(resolver.Resolve(visit, "icebreaker", warnings, "q"));

        Assert.Equal("icebreaker", only.Map);
        Assert.Equal([p], only.Points);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Rule2_quest_map_miss_keeps_every_map()
    {
        var p = new MapPoint(1, 2, 3);
        var resolver = Resolver(Table(("laboratory", "labs_zone", p)));

        var locations = resolver.Resolve(Beacon("labs_zone"), "bigmap", [], "q");

        Assert.Equal("laboratory", Assert.Single(locations).Map);
    }

    [Fact]
    public void Paired_map_counts_as_the_quest_map()
    {
        var night = new MapPoint(1, 0, 1);
        var elsewhere = new MapPoint(9, 9, 9);
        var resolver = Resolver(Table(("factory4_night", "z", night), ("bigmap", "z", elsewhere)));

        var locations = resolver.Resolve(Beacon("z"), "factory4_day", [], "q");

        Assert.Equal("factory4_night", Assert.Single(locations).Map);
    }

    [Fact]
    public void Quest_map_and_its_pair_are_both_kept()
    {
        var p = new MapPoint(1, 0, 1);
        var resolver = Resolver(Table(("sandbox", "z", p), ("sandbox_high", "z", p), ("bigmap", "z", p)));

        var locations = resolver.Resolve(Beacon("z"), "sandbox_high", [], "q");

        Assert.Equal(["sandbox_high", "sandbox"], locations.Select(l => l.Map));
    }

    [Fact]
    public void Zone_ids_are_trimmed_before_lookup()
    {
        var p = new MapPoint(1, 2, 3);
        var resolver = Resolver(Table(("labyrinth", "event_labyrinth_11_lightkeep_place_03", p)));
        var warnings = new List<CatalogWarning>();

        var locations = resolver.Resolve(Beacon("event_labyrinth_11_lightkeep_place_03 "), "labyrinth", warnings, "q");

        Assert.Equal([p], Assert.Single(locations).Points);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Missing_zone_warns_and_yields_no_locations()
    {
        var warnings = new List<CatalogWarning>();

        var locations = Resolver().Resolve(Beacon("bunker2"), "woods", warnings, "q1");

        Assert.Empty(locations);
        var w = Assert.Single(warnings);
        Assert.Equal(WarningCodes.QuestZoneNotFound, w.Code);
        Assert.Equal("q1", w.QuestId);
        Assert.Contains("bunker2", w.Detail);
    }

    [Fact]
    public void Counter_conditions_collect_visit_flare_and_in_zone_ids_in_order()
    {
        var a = new MapPoint(1, 0, 0);
        var b = new MapPoint(2, 0, 0);
        var c = new MapPoint(3, 0, 0);
        var d = new MapPoint(4, 0, 0);
        var resolver = Resolver(Table(("bigmap", "visit", a), ("bigmap", "flare", b), ("bigmap", "in1", c), ("bigmap", "in2", d)));

        var locations = resolver.Resolve(
            Counter(Sub("VisitPlace", "visit"), Sub("Kills"), Sub("LaunchFlare", "flare"), Sub("InZone", zoneIds: ["in1", "in2"])),
            "bigmap", [], "q");

        Assert.Equal([a, b, c, d], Assert.Single(locations).Points);
    }

    [Fact]
    public void Leave_item_at_location_uses_zone_id()
    {
        var p = new MapPoint(1, 2, 3);
        var condition = Beacon("stash") with { ConditionType = "LeaveItemAtLocation" };

        var locations = Resolver(Table(("bigmap", "stash", p))).Resolve(condition, "bigmap", [], "q");

        Assert.Equal([p], Assert.Single(locations).Points);
    }

    [Fact]
    public void Several_points_for_one_zone_are_all_kept_once()
    {
        var p1 = new MapPoint(1, 0, 0);
        var p2 = new MapPoint(2, 0, 0);
        var resolver = Resolver(Table(("bigmap", "z", p1), ("bigmap", "z", p2), ("bigmap", "z", p1)));

        var locations = resolver.Resolve(Beacon("z"), "bigmap", [], "q");

        Assert.Equal([p1, p2], Assert.Single(locations).Points);
    }

    [Fact]
    public void Find_quest_item_uses_forced_loot_spawns()
    {
        var p = new MapPoint(10, 1, 20);
        var resolver = Resolver(spawns: Table(("bigmap", QuestItemTpl.ToString(), p)));

        var locations = resolver.Resolve(FindItem(QuestItemTpl), "bigmap", [], "q");

        Assert.Equal([p], Assert.Single(locations).Points);
    }

    [Fact]
    public void Find_regular_item_has_no_location_even_if_forced()
    {
        var resolver = Resolver(spawns: Table(("bigmap", LootTpl.ToString(), new MapPoint(1, 1, 1))));
        var warnings = new List<CatalogWarning>();

        Assert.Empty(resolver.Resolve(FindItem(LootTpl), "bigmap", warnings, "q"));
        Assert.Empty(warnings);
    }

    [Fact]
    public void Unrelated_conditions_have_no_location_and_no_warning()
    {
        var warnings = new List<CatalogWarning>();
        var handover = new QuestCondition { Id = Id(4), ConditionType = "HandoverItem", DynamicLocale = false };

        Assert.Empty(Resolver().Resolve(handover, "bigmap", warnings, "q"));
        Assert.Empty(Resolver().Resolve(Counter(Sub("Kills"), Sub("ExitName")), "bigmap", warnings, "q"));
        Assert.Empty(warnings);
    }

    // ---- 영역 (08 스펙): InZone·LaunchFlare 만 영역을 싣는다 ----

    private static readonly MapPoint ZoneCenter = new(410, 13.6, -329);
    private static readonly MapArea Forest = new(ZoneCenter, 500, 1600, 0, -36.4, 63.6);

    [Fact]
    public void In_zone_kill_carries_the_zone_area()
    {
        var resolver = Resolver(Table(("woods", "kill_zone", ZoneCenter)), areas: Areas(("woods", "kill_zone", Forest)));

        var location = Assert.Single(resolver.Resolve(Counter(Sub("InZone", zoneIds: ["kill_zone"]), Sub("Kills")), "woods", [], "q"));

        Assert.Equal([ZoneCenter], location.Points);
        Assert.Equal([Forest], location.Areas);
    }

    /// <summary>신호탄 감지 상자는 땅에서 솟은 기둥이라 높이 범위를 바닥 + 1m 한 점으로 줄인다(Huntsman Administrator 가 3층으로 뜬 실측).</summary>
    [Fact]
    public void Flare_area_is_collapsed_to_its_bottom_height()
    {
        var detector = new MapArea(new MapPoint(-64.8, 10.3, 106.7), 46.24, 70.93, 0, -0.28, 20.88);
        var resolver = Resolver(Table(("tarkovstreets", "flare", new MapPoint(-64.8, 10.3, 106.7))), areas: Areas(("tarkovstreets", "flare", detector)));

        var location = Assert.Single(resolver.Resolve(Counter(Sub("LaunchFlare", "flare")), "tarkovstreets", [], "q"));

        var area = Assert.Single(location.Areas);
        Assert.Equal(0.72, area.MinY, 2);
        Assert.Equal(0.72, area.MaxY, 2);
        Assert.Equal(46.24, area.SizeX);
    }

    [Fact]
    public void In_zone_keeps_the_full_height_range()
    {
        var resolver = Resolver(Table(("woods", "k", ZoneCenter)), areas: Areas(("woods", "k", Forest)));

        var area = Assert.Single(Assert.Single(resolver.Resolve(Counter(Sub("InZone", zoneIds: ["k"])), "woods", [], "q")).Areas);

        Assert.Equal(-36.4, area.MinY);
        Assert.Equal(63.6, area.MaxY);
    }

    [Fact]
    public void Visit_and_plant_stay_points_even_when_the_zone_has_an_area()
    {
        var resolver = Resolver(Table(("woods", "z", ZoneCenter)), areas: Areas(("woods", "z", Forest)));

        Assert.Empty(Assert.Single(resolver.Resolve(Counter(Sub("VisitPlace", "z")), "woods", [], "q")).Areas);
        Assert.Empty(Assert.Single(resolver.Resolve(Beacon("z"), "woods", [], "q")).Areas);
    }

    [Fact]
    public void Area_follows_rule1_and_is_absent_without_size_data()
    {
        var reserveCopy = new MapPoint(1, -100, 1);
        var resolver = Resolver(
            Table(("woods", "z", ZoneCenter), ("rezervbase", "z", reserveCopy), ("woods", "bare", new MapPoint(7, 8, 9))),
            areas: Areas(("woods", "z", Forest), ("rezervbase", "z", new MapArea(reserveCopy, 5, 5, 0, -101, -99))));

        var locations = resolver.Resolve(Counter(Sub("InZone", zoneIds: ["z", "bare"])), "woods", [], "q");

        var woods = Assert.Single(locations);
        Assert.Equal([Forest], woods.Areas); // Reserve 복사본은 규칙 1 로 빠지고, 크기 없는 "bare" 는 점만
        Assert.Equal(2, woods.Points.Count);
    }

    [Fact]
    public void No_area_table_means_no_areas()
        => Assert.Empty(Assert.Single(
            Resolver(Table(("woods", "z", ZoneCenter))).Resolve(Counter(Sub("InZone", zoneIds: ["z"])), "woods", [], "q")).Areas);
}
