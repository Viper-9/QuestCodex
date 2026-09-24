using QuestCodex.Catalog;
using System.Text.Json;
using QuestCodex.Catalog.Models;
using QuestCodex.Web;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Utils.Json;
using static QuestCodex.Tests.Fixtures;

namespace QuestCodex.Tests.Catalog;

public class PrepParserTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 0, 0, 0, TimeSpan.Zero);

    private static readonly MongoId Svds = Id(701);
    private static readonly MongoId Tkpd = Id(702);
    private static readonly MongoId ScavVest = Id(703);
    private static readonly MongoId SecurityVest = Id(704);
    private static readonly MongoId Balaclava = Id(705);
    private static readonly MongoId Knife = Id(706);
    private static readonly MongoId Marker = Id(707);

    private static readonly Dictionary<string, string> Locale = new()
    {
        [$"{Svds} Name"] = "SVDS",
        [$"{ScavVest} Name"] = "스캐브 조끼",
        [$"{SecurityVest} Name"] = "보안 조끼",
        [$"{Balaclava} Name"] = "발라클라바",
        [$"{Knife} Name"] = "Bars 나이프",
        [$"{Marker} Name"] = "MS2000 마커",
        ["Lighthouse"] = "등대",
        ["Sandbox"] = "그라운드 제로",
        ["Sandbox_high"] = "그라운드 제로",
        ["5704e4dad2720bb55b8b4567 Name"] = "등대",
        ["E9_sniper"] = "클리모프 거리",
    };

    /// <summary>Tkpd 는 로케일 없이 아이템 테이블에만 있다(모드 아이템이 kr 번역을 안 넣은 경우) → en 템플릿 이름.</summary>
    private static readonly Dictionary<MongoId, TemplateItem> Items = new()
    {
        [Tkpd] = Template(Tkpd, Id(1), "TKPD 9.3x64 carbine"),
    };

    private static CatalogQuest Build(Quest q) => CatalogBuilder.Build(
        new CatalogInput("kr", "4.1.5", "test", new Dictionary<MongoId, Quest> { [q.Id] = q },
            new Dictionary<MongoId, TraderBase> { [Prapor] = Trader(Prapor, "Prapor") },
            Items, Locale, new Dictionary<string, string>(),
            new HashSet<MongoId>(), new HashSet<MongoId>(), null, null),
        Now).Quests[q.Id.ToString()];

    private static QuestCondition Counter(MongoId id, bool oneSession = false, params QuestConditionCounterCondition[] subs) => new()
    {
        Id = id, ConditionType = "CounterCreator", DynamicLocale = false, Value = 10, OneSessionOnly = oneSession,
        Counter = new QuestConditionCounter { Id = "c", Conditions = [.. subs] },
    };

    private static QuestConditionCounterCondition Kills(params MongoId[] weapons) => new()
    {
        ConditionType = "Kills", Target = new ListOrT<string>(null, "Savage"),
        Weapon = weapons.Select(w => w.ToString()).ToHashSet(),
    };

    private static QuestConditionCounterCondition Location(params string[] maps) => new()
    {
        ConditionType = "Location", Target = new ListOrT<string>([.. maps], null),
    };

    private static QuestConditionCounterCondition Equipment(params MongoId[][] options) => new()
    {
        ConditionType = "Equipment",
        EquipmentInclusive = options.Select(o => o.Select(x => x.ToString()).ToList()).ToList(),
    };

    private static QuestCondition Handover(MongoId id, MongoId tpl, double count, bool fir,
        double minDur = 0, double maxDur = 100, int dogtag = 0) => new()
    {
        Id = id, ConditionType = "HandoverItem", DynamicLocale = false, Value = count,
        Target = new ListOrT<string>([tpl.ToString()], null), OnlyFoundInRaid = fir,
        MinDurability = minDur, MaxDurability = maxDur, DogtagLevel = dogtag,
    };

    [Fact]
    public void Kill_counter_yields_map_weapons_and_equipment_slots()
    {
        var q = Quest(Id(1), finish:
        [
            Counter(Id(201), subs: [Kills(Svds, Tkpd), Location("Lighthouse")]),
            Counter(Id(202), subs: [Kills(), Location("Lighthouse"), Equipment([ScavVest], [SecurityVest]), Equipment([Balaclava])]),
        ]);

        var objs = Build(q).Objectives;

        var p1 = objs[0].Prep!;
        Assert.Equal(["등대"], p1.Maps);
        Assert.Equal([new ItemRef(Svds.ToString(), "SVDS"), new ItemRef(Tkpd.ToString(), "TKPD 9.3x64 carbine")], p1.Weapons);
        Assert.Empty(p1.Equipment);

        var p2 = objs[1].Prep!;
        Assert.Empty(p2.Weapons);
        Assert.Equal(2, p2.Equipment.Count); // 슬롯끼리는 AND
        Assert.Equal(["스캐브 조끼", "보안 조끼"], p2.Equipment[0].Select(opt => Assert.Single(opt).Name)); // 슬롯 안은 OR
        Assert.Equal("발라클라바", Assert.Single(Assert.Single(p2.Equipment[1])).Name);
    }

    [Fact]
    public void Maps_are_deduplicated_by_display_name()
    {
        var q = Quest(Id(1), finish: [Counter(Id(201), subs: [Kills(), Location("Sandbox", "Sandbox_high")])]);

        Assert.Equal(["그라운드 제로"], Build(q).Objectives[0].Prep!.Maps);
    }

    [Fact]
    public void Handover_item_carries_count_fir_and_only_meaningful_limits()
    {
        var q = Quest(Id(1), finish:
        [
            Handover(Id(201), Knife, 5, fir: true),
            Handover(Id(202), Knife, 1, fir: false, minDur: 60, maxDur: 100, dogtag: 15),
        ]);

        var objs = Build(q).Objectives;

        var item = objs[0].Prep!.Item!;
        Assert.Equal("handover", item.Action);
        Assert.Equal([new ItemRef(Knife.ToString(), "Bars 나이프")], item.Items);
        Assert.Equal(5, item.Count);
        Assert.True(item.FoundInRaid);
        Assert.Null(item.MinDurability);   // 0 은 제한 없음
        Assert.Null(item.MaxDurability);   // 100 은 제한 없음
        Assert.Null(item.DogtagLevel);

        var limited = objs[1].Prep!.Item!;
        Assert.False(limited.FoundInRaid);
        Assert.Equal(60, limited.MinDurability);
        Assert.Equal(15, limited.DogtagLevel);
    }

    [Fact]
    public void Plant_objectives_list_the_item_to_bring_with_plant_time()
    {
        var plant = new QuestCondition
        {
            Id = Id(201), ConditionType = "LeaveItemAtLocation", DynamicLocale = false, Value = 1,
            Target = new ListOrT<string>([Marker.ToString()], null), PlantTime = 30, ZoneId = "zone",
        };

        var item = Build(Quest(Id(1), finish: [plant])).Objectives[0].Prep!.Item!;

        Assert.Equal("plant", item.Action);
        Assert.Equal("MS2000 마커", Assert.Single(item.Items).Name);
        Assert.Equal(30, item.PlantSeconds);
    }

    [Fact]
    public void Raid_rules_one_session_and_exit_are_prep()
    {
        var exit = new QuestConditionCounterCondition { ConditionType = "ExitStatus", Status = ["Survived", "Runner"] };
        var exitName = new QuestConditionCounterCondition { ConditionType = "ExitName", ExitName = "E9_sniper" };

        var prep = Build(Quest(Id(1), finish: [Counter(Id(201), oneSession: true, subs: [exit, exitName])])).Objectives[0].Prep!;

        Assert.True(prep.OneRaid);
        Assert.Equal(["Survived", "Runner"], prep.ExitStatuses);
        Assert.Equal("클리모프 거리", prep.ExitName);
    }

    [Fact]
    public void Forbidden_equipment_is_flattened()
    {
        var sub = new QuestConditionCounterCondition
        {
            ConditionType = "Equipment",
            EquipmentExclusive = [[ScavVest.ToString()], [Balaclava.ToString()]],
        };

        var prep = Build(Quest(Id(1), finish: [Counter(Id(201), subs: [Kills(), sub])])).Objectives[0].Prep!;

        Assert.Equal(["스캐브 조끼", "발라클라바"], prep.ForbiddenEquipment.Select(i => i.Name));
    }

    [Fact]
    public void Nothing_to_prepare_yields_null()
    {
        var find = new QuestCondition
        {
            Id = Id(202), ConditionType = "FindItem", DynamicLocale = false, Value = 1,
            Target = new ListOrT<string>([Knife.ToString()], null), OnlyFoundInRaid = true,
        };

        var objs = Build(Quest(Id(1), finish: [Counter(Id(201), subs: [Kills()]), find])).Objectives;

        Assert.Null(objs[0].Prep); // 제한 없는 사살
        Assert.Null(objs[1].Prep); // 레이드에서 찾는 것은 챙겨갈 물건이 아니다
    }

    [Fact]
    public void Unknown_item_keeps_its_tpl_as_name()
    {
        var ghost = Id(799);

        var prep = Build(Quest(Id(1), finish: [Counter(Id(201), subs: [Kills(ghost)])])).Objectives[0].Prep!;

        Assert.Equal(new ItemRef(ghost.ToString(), ghost.ToString()), Assert.Single(prep.Weapons));
    }

    [Fact]
    public void Quest_location_is_resolved_and_any_is_null()
    {
        var onMap = Quest(Id(1));
        onMap.Location = "5704e4dad2720bb55b8b4567";

        Assert.Equal("등대", Build(onMap).Location);
        Assert.Null(Build(Quest(Id(2))).Location); // Fixtures 기본값 "any"
    }

    [Fact]
    public void Json_shape_matches_the_front_types()
    {
        var q = Quest(Id(1), finish: [Handover(Id(201), Knife, 5, fir: true), Counter(Id(202), subs: [Kills(Svds)])]);

        var json = JsonSerializer.SerializeToElement(Build(q).Objectives, QuestCodexJson.Options);

        var prep = json[0].GetProperty("prep");
        Assert.Equal(
            ["maps", "item", "weapons", "calibers", "weaponMods", "equipment", "forbiddenEquipment", "oneRaid", "exitStatuses", "exitName"],
            prep.EnumerateObject().Select(p => p.Name));
        Assert.Equal(
            ["action", "items", "count", "foundInRaid", "minDurability", "maxDurability", "dogtagLevel", "plantSeconds"],
            prep.GetProperty("item").EnumerateObject().Select(p => p.Name));
        Assert.Equal(JsonValueKind.Null, prep.GetProperty("exitName").ValueKind); // null 도 생략하지 않는다 — 프론트는 !== null 로 판단
        Assert.Equal("SVDS", json[1].GetProperty("prep").GetProperty("weapons")[0].GetProperty("name").GetString());
    }
}
