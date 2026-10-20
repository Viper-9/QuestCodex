using QuestCodex.Briefing;
using QuestCodex.Catalog.Models;
using QuestCodex.Progress.Models;
using static QuestCodex.Tests.Briefing.B;

namespace QuestCodex.Tests.Briefing;

public class RaidPlanTests
{
    [Fact]
    public void MapGroup_merges_variants_and_drops_develop()
    {
        Assert.Equal("factory4_day", RaidPlan.MapGroup("factory4_night"));
        Assert.Equal("sandbox", RaidPlan.MapGroup("Sandbox_high"));
        Assert.Equal("laboratory", RaidPlan.MapGroup("laboratory_dark"));
        Assert.Null(RaidPlan.MapGroup("develop"));
        Assert.Equal("woods", RaidPlan.MapGroup("woods"));
    }

    [Fact]
    public void LocationIds_lists_every_location_of_a_tab()
    {
        Assert.Equal(new[] { "factory4_day", "factory4_night" }, RaidPlan.LocationIds("factory4_day", ["factory4_day", "factory4_night", "woods"]));
        Assert.Equal(new[] { "sandbox", "sandbox_high", "sandbox_start" }, RaidPlan.LocationIds("sandbox", ["Sandbox", "Sandbox_high"]));
        Assert.Equal(new[] { "icebreaker" }, RaidPlan.LocationIds("icebreaker", ["icebreaker"]));
        Assert.Equal(new[] { "bigmap" }, RaidPlan.LocationIds("bigmap", []));
    }

    [Fact]
    public void ObjectiveMaps_falls_back_to_quest_map_then_any()
    {
        var q = Quest("q", [], locationKey: "shoreline");
        Assert.Equal(new[] { "factory4_day" }, RaidPlan.ObjectiveMaps(q, Obj("a", "PlaceBeacon", Prep(mapKeys: ["factory4_day", "factory4_night"]))));
        Assert.Equal(new[] { "shoreline" }, RaidPlan.ObjectiveMaps(q, Obj("b", "PlaceBeacon", Prep())));
        Assert.Empty(RaidPlan.ObjectiveMaps(Quest("r", []), Obj("c", "CounterCreator")));
    }

    [Fact]
    public void MapsFromText_uses_ascii_word_boundaries_and_korean_names()
    {
        Assert.Equal(new[] { "woods" }, RaidPlan.MapsFromText("삼림(Woods)에 있는 첫 번째 종교의식 현장에 MS2000 마커 설치하기"));
        Assert.Equal(new[] { "bigmap" }, RaidPlan.MapsFromText("Stash the AK-50 body at the specified spot on Customs"));
        Assert.Equal(new[] { "sandbox" }, RaidPlan.MapsFromText("Scout the intersection of Mira Ave and the overpass on Ground Zero"));
        Assert.Equal(new[] { "tarkovstreets", "sandbox" }, RaidPlan.MapsFromText("Use the transit from Ground Zero to Streets of Tarkov"));
        Assert.Empty(RaidPlan.MapsFromText("Locate the checkpoint with seized cargo"));
        Assert.Empty(RaidPlan.MapsFromText("Hand over the Lab journal"));
        Assert.Empty(RaidPlan.MapsFromText("keep them in reserve"));
        // JS 의 \b 는 영문 기준이라 바로 뒤에 한글이 붙어도 단어 경계로 본다. .NET 기본 \b 로는 이 테스트가 깨진다.
        Assert.Equal(new[] { "bigmap" }, RaidPlan.MapsFromText("Customs에서 정찰하기"));
    }

    [Fact]
    public void RaidEntries_infers_maps_from_text_only_when_data_has_none()
    {
        var cult = Quest("cult", [
            Obj("w", "PlaceBeacon", Prep(Item("plant")), text: "삼림(Woods)에 있는 첫 번째 현장"),
            Obj("s", "PlaceBeacon", Prep(Item("plant")), text: "해안선(Shoreline)에 있는 현장"),
            Obj("k", "CounterCreator", Prep(mapKeys: ["woods"]), text: "Kill on Customs"),   // 데이터(woods)가 이긴다
        ]);
        var e = RaidPlan.RaidEntries(B.Catalog([cult]), B.Progress(new() { ["cult"] = Qp("Started") }));
        Assert.Equal(new[] { ("w", "woods", true), ("s", "shoreline", true), ("k", "woods", false) },
            e.Select(x => (x.Objective.ConditionId, string.Join(",", x.Maps), x.Inferred)));
    }

    [Fact]
    public void RaidEntries_skip_objectives_the_game_still_hides()
    {
        var lotus = Quest("lotus", [
            Obj("kill", "CounterCreator"),
            Obj("plant", "LeaveItemAtLocation", Prep(mapKeys: ["factory4_day"])) with { VisibleAfter = ["kill"] },
            Obj("exit", "CounterCreator", Prep(mapKeys: ["factory4_day"])) with { VisibleAfter = ["plant"] },
            Obj("odd", "VisitPlace") with { VisibleAfter = ["gone"] }, // 이 퀘스트에 없는 조건을 가리키면 보이는 것으로 본다
        ]);
        var catalog = B.Catalog([lotus]);

        var before = RaidPlan.RaidEntries(catalog, B.Progress(new() { ["lotus"] = Qp("Started", new() { ["kill"] = Op(0, 1) }) }));
        var after = RaidPlan.RaidEntries(catalog, B.Progress(new() { ["lotus"] = Qp("Started", new() { ["kill"] = Op(1, 1, done: true) }) }));

        Assert.Equal(new[] { "kill", "odd" }, before.Select(e => e.Objective.ConditionId));
        Assert.Equal(new[] { "plant", "odd" }, after.Select(e => e.Objective.ConditionId));
    }

    [Fact]
    public void RaidEntries_take_quest_item_finds_on_the_map_where_the_item_spawns()
    {
        var db = Quest("db", [
            Obj("find", "FindItem", locations: At("interchange", 1, 2, 3)),
            Obj("hand", "HandoverItem"),
            Obj("bolts", "FindItem"),   // 좌표가 없는 일반 아이템 찾기는 그대로 뺀다
        ], locationKey: "bigmap");     // 퀘스트 맵보다 아이템이 생기는 맵이 맞다
        var catalog = B.Catalog([db]);

        var open = RaidPlan.RaidEntries(catalog, B.Progress(new() { ["db"] = Qp("Started", new() { ["find"] = Op(0, 1) }) }));
        var found = RaidPlan.RaidEntries(catalog, B.Progress(new() { ["db"] = Qp("Started", new() { ["find"] = Op(1, 1, done: true) }) }));

        var e = Assert.Single(open);
        Assert.Equal(("find", "interchange", false), (e.Objective.ConditionId, string.Join(",", e.Maps), e.Inferred));
        Assert.Empty(found);
    }

    // 아래는 derive.test.ts 의 raidEntries, mapTabs, mapBrief 테스트를 옮긴 것

    private static readonly CatalogQuest Cult = Quest("cult", [
        Obj("c1", "PlaceBeacon", Prep(Item("plant", count: 1))),
        Obj("c2", "PlaceBeacon", Prep(Item("plant", count: 1))),
        Obj("h", "HandoverItem", Prep(Item(items: [Jammer]))),
    ], locationKey: "woods");
    private static readonly CatalogQuest Kills = Quest("kills", [
        Obj("k", "CounterCreator", Prep(weapons: [new ItemRef("svd", "SVD")], oneRaid: true), 15),
    ]);
    private static readonly CatalogQuest LockedQ = Quest("locked", [Obj("l", "CounterCreator", Prep(mapKeys: ["woods"]))]);

    private static (List<RaidEntry> Entries, Dictionary<string, ItemHolding> Inv) Fixture()
    {
        var prog = B.Progress(new()
        {
            ["cult"] = Qp("Started", new() { ["c1"] = Op(0, 1), ["c2"] = Op(0, 1), ["h"] = Op(0, 1) }),
            ["kills"] = Qp("Started", new() { ["k"] = Op(7, 15) }),
            ["locked"] = Qp("Locked"),
        });
        return (RaidPlan.RaidEntries(B.Catalog([Cult, Kills, LockedQ]), prog), Inv(("ms2000", 1, 1)));
    }

    [Fact]
    public void RaidEntries_keeps_raid_objectives_of_started_quests()
        => Assert.Equal(new[] { "c1", "c2", "k" }, Fixture().Entries.Select(e => e.Objective.ConditionId));

    [Fact]
    public void GroupByQuest_keeps_first_seen_order()
    {
        var e = Fixture().Entries;
        var groups = RaidPlan.GroupByQuest([e[0], e[2], e[1]]);
        Assert.Equal(new[] { ("cult", "c1,c2"), ("kills", "k") },
            groups.Select(g => (g.Quest.Id, string.Join(",", g.Entries.Select(x => x.Objective.ConditionId)))));
    }

    [Fact]
    public void MapTabs_known_maps_always_in_fixed_order_and_server_maps_add_tabs()
    {
        var e = Fixture().Entries;
        var tabs = RaidPlan.MapTabs(e);
        Assert.Equal(new MapTab("bigmap", 0), tabs[0]);
        Assert.Equal(2, tabs.Single(t => t.Key == "woods").Count);
        Assert.Equal(new MapTab("modmap", 1), RaidPlan.MapTabs([e[0] with { Maps = ["modmap"] }])[^1]);
        var withServer = RaidPlan.MapTabs(e, ["icebreaker", "factory4_night", "sandbox_high"]);
        Assert.Equal(new MapTab("icebreaker", 0), withServer[^1]);
        Assert.Equal(tabs.Count + 1, withServer.Count);
    }

    [Fact]
    public void MapBrief_sums_plant_items_and_collects_gear_and_special()
    {
        var (e, inv) = Fixture();
        var b = RaidPlan.MapBrief(e, "woods", inv);
        Assert.Equal(new[] { "c1", "c2" }, b.Here.Select(x => x.Objective.ConditionId));
        Assert.Equal(new[] { "k" }, b.Anywhere.Select(x => x.Objective.ConditionId));
        var bring = Assert.Single(b.Bring);
        Assert.Equal(("ms2000", 2.0, 1.0), (bring.Key, bring.Need, bring.Have));
        Assert.Empty(b.BringAnywhere);
        Assert.Equal(new[] { "k" }, b.Gear.Select(x => x.Objective.ConditionId));
        Assert.Equal(new[] { "k" }, b.Special.Select(x => x.Objective.ConditionId));
        Assert.Empty(RaidPlan.MapBrief(e, "bigmap", new Dictionary<string, ItemHolding>()).Here);
    }

    [Fact]
    public void RaidMapPlan_numbers_quests_with_coordinates_on_this_map()
    {
        var a = Quest("a", [Obj("a1", "VisitPlace", locations: At("factory4_night")), Obj("a2", "VisitPlace", locations: At("factory4_day"))]);
        var b = Quest("b", [Obj("b1", "VisitPlace", locations: At("woods"))]);
        var c = Quest("c", [Obj("c1", "VisitPlace"), Obj("c2", "VisitPlace", locations: At("factory4_day"))]);
        var here = new[] { a, b, c }.SelectMany(q => q.Objectives.Select(o => new RaidEntry(q, o, null, ["factory4_day"], false))).ToList();
        var plan = RaidPlan.RaidMapPlan(here, "factory4_day");
        Assert.Equal(new[] { ("a", 1), ("c", 2) }, plan.Numbers.OrderBy(kv => kv.Value).Select(kv => (kv.Key, kv.Value)));
        Assert.Equal(new[] { (1, "a1"), (1, "a2"), (2, "c2") }, plan.Items.Select(i => (i.N, i.Objective.ConditionId)));
    }

    [Fact]
    public void OrderRaidQuests_mapped_then_counters_by_ratio_then_name()
    {
        RaidEntry Entry(CatalogQuest q, Objective o, double current, double? target)
            => new(q, o, Op(current, target), ["woods"], false);
        var zebra = Quest("zebra", [Obj("z", "VisitPlace")]);
        var apple = Quest("apple", [Obj("a", "VisitPlace")]);
        var low = Quest("low", [Obj("l", "CounterCreator")]);
        var high = Quest("high", [Obj("h1", "CounterCreator"), Obj("h2", "CounterCreator")]);
        var mapped = Quest("mapped", [Obj("m", "VisitPlace", locations: At("woods"))]);
        var here = new List<RaidEntry>
        {
            Entry(zebra, zebra.Objectives[0], 0, 1),          // target 1 은 카운터로 치지 않는다
            Entry(low, low.Objectives[0], 2, 20),
            Entry(apple, apple.Objectives[0], 0, null),
            Entry(high, high.Objectives[0], 5, 10), Entry(high, high.Objectives[1], 10, 10),
            Entry(mapped, mapped.Objectives[0], 0, 1),
        };
        Assert.Equal(new[] { "mapped", "high", "low", "apple", "zebra" }, RaidPlan.OrderRaidQuests(here, "woods").Select(g => g.Quest.Id));
        Assert.Equal(new[] { ("mapped", 1) }, RaidPlan.RaidMapPlan(here, "woods").Numbers.Select(kv => (kv.Key, kv.Value)));
    }

    [Fact]
    public void NameOrderFor_follows_request_language()
    {
        Assert.True(RaidNeeds.NameOrderFor("kr").Compare("광신도", "Hobby Club") < 0);   // 웹 ko-KR 정렬처럼 한글이 영문보다 앞
        Assert.True(RaidNeeds.NameOrderFor("en").Compare("Hobby Club", "광신도") < 0);
        Assert.Same(RaidNeeds.NameOrder, RaidNeeds.NameOrderFor("en"));
    }

    [Fact]
    public void OrderRaidQuests_name_tie_break_uses_given_comparer()
    {
        var hobby = Quest("hobby", [Obj("h", "VisitPlace")], name: "Hobby Club");
        var cult = Quest("cult", [Obj("c", "VisitPlace")], name: "광신도");
        var here = new List<RaidEntry>
        {
            new(hobby, hobby.Objectives[0], Op(0, 1), ["woods"], false),
            new(cult, cult.Objectives[0], Op(0, 1), ["woods"], false),
        };
        Assert.Equal(new[] { "hobby", "cult" }, RaidPlan.OrderRaidQuests(here, "woods").Select(g => g.Quest.Id));
        Assert.Equal(new[] { "cult", "hobby" }, RaidPlan.OrderRaidQuests(here, "woods", RaidNeeds.NameOrderFor("kr")).Select(g => g.Quest.Id));
    }

    [Fact]
    public void MapBrief_keeps_plant_items_without_map_separate()
    {
        var noMap = Quest("nomap", [Obj("n", "PlaceBeacon", Prep(Item("plant", items: [Jammer])))]);
        var e = RaidPlan.RaidEntries(B.Catalog([noMap]), B.Progress(new() { ["nomap"] = Qp("Started") }));
        var b = RaidPlan.MapBrief(e, "woods", new Dictionary<string, ItemHolding>());
        Assert.Empty(b.Bring);
        Assert.Equal(new[] { "jam" }, b.BringAnywhere.Select(r => r.Key));
    }
}
