using System.Text.Json;
using QuestCodex.Briefing;
using QuestCodex.Briefing.Models;
using QuestCodex.Catalog.Models;
using QuestCodex.Web;
using static QuestCodex.Tests.Briefing.B;

namespace QuestCodex.Tests.Briefing;

public class BriefingBuilderTests
{
    private const string IndexJson = """
        { "maps": [
          { "key": "factory4_day", "internalNames": ["factory4_day", "factory4_night"] },
          { "key": "woods", "internalNames": ["Woods"] },
          { "key": "interchange", "internalNames": ["Interchange"], "variants": { "manimal": "interchange-manimal" } }
        ] }
        """;

    private static readonly MapPoint P0 = new(1, 2, 3);

    // 공장: 퀘스트 a(주간·야간 같은 점 + 카운터), 퀘스트 c(좌표 없음). 삼림: 설치·장비·특수. 맵 없음: 퀘스트 any.
    private static BriefingResponse Build(string lang = "kr", SortedDictionary<string, string>? variants = null)
    {
        var a = Quest("a", [
            Obj("a1", "VisitPlace", Prep(mapKeys: ["factory4_day"]), text: "Visit A",
                locations: [new ObjectiveLocation("factory4_day", [P0]), new ObjectiveLocation("factory4_night", [P0]), new ObjectiveLocation("woods", [new MapPoint(9, 9, 9)])]),
            Obj("a2", "CounterCreator", Prep(mapKeys: ["factory4_night"]), 5, text: "Kill"),
        ], traderId: "prapor", name: "Alpha");
        var c = Quest("c", [Obj("c1", "CounterCreator", Prep(mapKeys: ["factory4_day"]), 3, text: "Kill C")], name: "Charlie");
        var w = Quest("w", [
            Obj("w1", "PlaceBeacon", Prep(Item("plant", count: 2), weapons: [new("svd", "SVD")], oneRaid: true)),
            Obj("w2", "PlaceBeacon", Prep(Item("plant"), oneRaid: true)),
        ], locationKey: "woods", name: "Whiskey");
        var any = Quest("any", [Obj("x1", "CounterCreator", Prep(calibers: ["Caliber9x19PARA"], exitStatuses: ["Survived"]), 10, text: "Kill anywhere")], name: "Xray");
        var hand = Quest("hand", [Obj("f", "FindItem", text: "Find on Woods"), Obj("h", "HandoverItem", Prep(Item(items: [Jammer], count: 2)))], name: "Hotel");

        var exits = new SortedDictionary<string, List<MapExit>>(StringComparer.Ordinal)
        {
            ["factory4_day"] = [new MapExit("Gate 3", "Gate 3", "pmc", P0, null, null, null, null)],
            ["factory4_night"] = [new MapExit("Gate 3", "Gate 3", "pmc", P0, null, null, null, null), new MapExit("Med Tent", "Med Tent", "pmc", new(5, 0, 5), null, null, null, null)],
            ["icebreaker"] = [],
        };
        var doors = new SortedDictionary<string, List<LockedDoor>>(StringComparer.Ordinal)
        {
            ["factory4_day"] = [new LockedDoor("key1", "Key 1", "door", P0)],
            ["factory4_night"] = [new LockedDoor("key1", "Key 1", "door", P0)],
        };
        var traders = new SortedDictionary<string, CatalogTrader>(StringComparer.Ordinal) { ["prapor"] = new("prapor", "프라퍼", null, true) };
        var cat = B.Catalog([a, c, w, any, hand], lang, exits, doors, variants, traders);
        var prog = B.Progress(new()
        {
            ["a"] = Qp("Started", new() { ["a1"] = Op(0, null), ["a2"] = Op(2, 5) }),
            ["c"] = Qp("Started", new() { ["c1"] = Op(1, 3) }),
            ["w"] = Qp("Started", new() { ["w1"] = Op(0, 2), ["w2"] = Op(0, 1) }),
            ["any"] = Qp("Started", new() { ["x1"] = Op(4, 10) }),
            ["hand"] = Qp("Started", new() { ["h"] = Op(0, 2) }),
        }, Inv(("ms2000", 1, 0)));
        return BriefingBuilder.Build(cat, prog, MapIndex.Parse(IndexJson), "9.9.9", DateTimeOffset.UnixEpoch);
    }

    private static BriefingMap Map(BriefingResponse r, string key) => r.Maps.Single(m => m.Key == key);

    [Fact]
    public void Header_fields()
    {
        var r = Build();
        Assert.Equal((1, "9.9.9", "kr", DateTimeOffset.UnixEpoch), (r.SchemaVersion, r.QuestCodexVersion, r.Lang, r.GeneratedAt));
        Assert.Equal("프라퍼", r.Traders["prapor"].Name);
    }

    [Fact]
    public void Maps_follow_web_tab_order_with_location_ids_names_and_folders()
    {
        var r = Build();
        Assert.Equal(RaidPlan.MapOrder.Append("icebreaker"), r.Maps.Select(m => m.Key));
        var factory = Map(r, "factory4_day");
        Assert.Equal(["factory4_day", "factory4_night"], factory.LocationIds);
        Assert.Equal(("공장", 3, true, "factory4_day"), (factory.Name, factory.Count, factory.HasMap, factory.MapFolder));
        Assert.Equal((false, (string?)null), (Map(r, "bigmap").HasMap, Map(r, "bigmap").MapFolder));
        Assert.Equal("아이스브레이커", Map(r, "icebreaker").Name);   // 서버에만 있는 모드 맵도 탭이 생긴다
    }

    [Fact]
    public void Variant_changes_name_and_folder()
    {
        var r = Build(variants: new(StringComparer.Ordinal) { ["interchange"] = "manimal" });
        var ic = Map(r, "interchange");
        Assert.Equal(("확장된 인터체인지", "interchange-manimal"), (ic.Name, ic.MapFolder));
    }

    [Fact]
    public void Quests_are_ordered_numbered_and_summarized()
    {
        var factory = Map(Build(), "factory4_day");
        Assert.Equal([("a", 1, "목표 2개", 2.0, 5.0), ("c", (int?)null, "Kill C ×3", 1.0, 3.0)],
            factory.Quests.Select(q => (q.QuestId, q.Marker, q.Summary, q.Current, q.Target)));
        Assert.Equal(("prapor", "Alpha"), (factory.Quests[0].TraderId, factory.Quests[0].Name));
    }

    [Fact]
    public void Objective_locations_keep_only_this_tab_merged_without_duplicate_points()
    {
        var a1 = Map(Build(), "factory4_day").Quests[0].Objectives[0];
        Assert.Equal(("a1", "Visit A", 0.0, (double?)null, false), (a1.ConditionId, a1.Text, a1.Current, a1.Target, a1.InferredMap));
        var loc = Assert.Single(a1.Locations);
        Assert.Equal("factory4_day", loc.Map);
        Assert.Equal([P0], loc.Points);
        Assert.Empty(Map(Build(), "factory4_day").Quests[0].Objectives[1].Locations);
    }

    [Fact]
    public void Bring_find_gear_special_of_a_map()
    {
        var woods = Map(Build(), "woods");
        var bring = Assert.Single(woods.Bring);
        Assert.Equal(("ms2000", 3.0, 1.0, false, 2.0), (bring.Items[0].Tpl, bring.Need, bring.Have, bring.Fir, bring.Missing));
        Assert.Equal([("w", 3.0)], bring.Sources.Select(s => (s.QuestId, s.Count)));

        var find = Assert.Single(woods.Find);
        Assert.Equal(("jam", 2.0, "here"), (find.Items[0].Tpl, find.Missing, find.Place));
        Assert.Equal("elsewhere", Assert.Single(Map(Build(), "bigmap").Find).Place);

        var gear = Assert.Single(woods.Gear);
        Assert.Equal(("w", "weapon"), (gear.QuestId, Assert.Single(gear.Rows).Label));
        var special = Assert.Single(woods.Special);
        Assert.Equal(["oneRaid"], special.Rows.Select(x => x.Kind));      // 두 목표의 같은 줄은 한 번
    }

    [Fact]
    public void Anywhere_holds_mapless_objectives_once()
    {
        var r = Build();
        var q = Assert.Single(r.Anywhere.Quests);
        Assert.Equal(("any", (int?)null, "Kill anywhere ×10", 4.0, 10.0), (q.QuestId, q.Marker, q.Summary, q.Current, q.Target));
        Assert.Equal("caliber", Assert.Single(Assert.Single(r.Anywhere.Gear).Rows).Label);
        Assert.Equal("exit", Assert.Single(Assert.Single(r.Anywhere.Special).Rows).Kind);
        Assert.Empty(r.Anywhere.Bring);
        Assert.All(r.Maps, m => Assert.DoesNotContain(m.Quests, x => x.QuestId == "any"));
        Assert.All(r.Maps, m => Assert.DoesNotContain(m.Gear, x => x.QuestId == "any"));
    }

    [Fact]
    public void Exits_and_doors_merge_variant_maps_without_duplicates()
    {
        var factory = Map(Build(), "factory4_day");
        Assert.Equal(["Gate 3", "Med Tent"], factory.Exits.Select(e => e.Key));
        Assert.Single(factory.LockedDoors);
    }

    [Fact]
    public void Json_uses_contract_field_names()
    {
        var json = JsonSerializer.Serialize(Build(), QuestCodexJson.Options);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        var map = root.GetProperty("maps").EnumerateArray().Single(m => m.GetProperty("key").GetString() == "woods");
        foreach (var name in new[] { "locationIds", "name", "count", "hasMap", "mapFolder", "quests", "bring", "find", "gear", "special", "exits", "lockedDoors" })
            Assert.True(map.TryGetProperty(name, out _), name);
        Assert.Equal("here", map.GetProperty("find")[0].GetProperty("place").GetString());
        Assert.Equal(JsonValueKind.Array, map.GetProperty("gear")[0].GetProperty("rows")[0].GetProperty("tpls")[0].ValueKind);
        var obj = root.GetProperty("maps").EnumerateArray().Single(m => m.GetProperty("key").GetString() == "factory4_day")
            .GetProperty("quests")[0].GetProperty("objectives")[0];
        foreach (var name in new[] { "conditionId", "text", "current", "target", "inferredMap", "locations" })
            Assert.True(obj.TryGetProperty(name, out _), name);
        Assert.True(root.GetProperty("anywhere").TryGetProperty("bring", out _));
    }

    [Fact]
    public void Anywhere_quests_are_ordered_by_counter_ratio_then_name()
    {
        // 카탈로그(ID) 순서는 a, b, c 지만 기대 순서는 b(9/10) → c(1/10) → a(카운터 없음)
        var a = Quest("a", [Obj("a1", "CounterCreator", Prep(exitStatuses: ["Survived"]), text: "No counter")], name: "Alpha");
        var b = Quest("b", [Obj("b1", "CounterCreator", Prep(exitStatuses: ["Survived"]), 10, text: "High")], name: "Zulu");
        var c = Quest("c", [Obj("c1", "CounterCreator", Prep(exitStatuses: ["Survived"]), 10, text: "Low")], name: "Bravo");
        var prog = B.Progress(new()
        {
            ["a"] = Qp("Started", new() { ["a1"] = Op(0, null) }),
            ["b"] = Qp("Started", new() { ["b1"] = Op(9, 10) }),
            ["c"] = Qp("Started", new() { ["c1"] = Op(1, 10) }),
        });
        var r = BriefingBuilder.Build(B.Catalog([a, b, c]), prog, MapIndex.Parse(IndexJson), "9.9.9", DateTimeOffset.UnixEpoch);
        Assert.Equal(new[] { "b", "c", "a" }, r.Anywhere.Quests.Select(q => q.QuestId));
    }
}
