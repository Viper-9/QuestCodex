using QuestCodex.Catalog;
using QuestCodex.Catalog.Locations;
using QuestCodex.Catalog.Models;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using static QuestCodex.Tests.Fixtures;

namespace QuestCodex.Tests.Catalog;

public class CatalogBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 0, 0, 0, TimeSpan.Zero);

    private static CatalogInput Input(
        IEnumerable<Quest> quests,
        Dictionary<string, string>? locale = null,
        Dictionary<string, string>? en = null,
        IEnumerable<MongoId>? bear = null,
        IEnumerable<MongoId>? usec = null,
        IReadOnlySet<string>? vanilla = null,
        string? snapshotVersion = "4.1.5",
        Dictionary<MongoId, TemplateItem>? items = null,
        Dictionary<MongoId, TraderBase>? traders = null,
        Dictionary<string, string>? modOrigins = null,
        IReadOnlyList<CatalogWarning>? modWarnings = null,
        Func<string, bool>? avatarIsServable = null)
        => new(
            Lang: "kr",
            SptVersion: "4.1.5",
            ModVersion: "0.2.0",
            Quests: quests.ToDictionary(q => q.Id),
            Traders: traders ?? new() { [Prapor] = Trader(Prapor, "Prapor", "/files/trader/avatar/x.jpg"), [Therapist] = Trader(Therapist, "Therapist") },
            Items: items ?? new(),
            Locale: locale ?? new(),
            FallbackLocale: en ?? new(),
            BearOnly: (bear ?? []).ToHashSet(),
            UsecOnly: (usec ?? []).ToHashSet(),
            VanillaQuestIds: vanilla,
            VanillaSnapshotSptVersion: snapshotVersion,
            ModQuestOrigins: modOrigins,
            ModQuestScanWarnings: modWarnings,
            AvatarIsServable: avatarIsServable);

    [Fact]
    public void Quest_text_uses_locale_chain_and_warns_once_per_quest()
    {
        var q = Quest(Id(1));
        var en = new Dictionary<string, string> { [$"{Id(1)} name"] = "EN name" };

        var cat = CatalogBuilder.Build(Input([q], locale: new(), en: en), Now);

        var cq = cat.Quests[Id(1)];
        Assert.Equal("EN name", cq.Name);
        Assert.Equal(Id(1).ToString(), cq.Description); // 어디에도 없음 → 원문
        Assert.Single(cat.Warnings, w => w.Code == WarningCodes.MissingLocale && w.QuestId == Id(1).ToString());
    }

    /// <summary>
    /// 퀘스트 템플릿의 name/description 필드가 곧 로케일 키다. 바닐라는 "&lt;id&gt; name" 이라 차이가 없지만 Painter 모드는
    /// "painter_1 name" 처럼 자기 키를 쓴다 — "&lt;id&gt; name" 만 찾으면 이름이 ID 로 나온다(실측 12개).
    /// </summary>
    [Fact]
    public void Quest_text_uses_the_template_locale_key_first()
    {
        var q = Quest(Id(1));
        q.Name = "painter_1 name";
        q.Description = "painter_1 description";
        var en = new Dictionary<string, string> { ["painter_1 name"] = "Taped Up", ["painter_1 description"] = "Find tape" };

        var cq = CatalogBuilder.Build(Input([q], locale: new(), en: en), Now).Quests[Id(1)];

        Assert.Equal("Taped Up", cq.Name);
        Assert.Equal("Find tape", cq.Description);
    }

    [Fact]
    public void Trader_metadata_comes_from_locale_then_base()
    {
        var cat = CatalogBuilder.Build(Input([Quest(Id(1), Prapor), Quest(Id(2), Therapist)],
            locale: new() { [$"{Prapor} Nickname"] = "프라퍼" }), Now);

        Assert.Equal("프라퍼", cat.Traders[Prapor].Name);
        Assert.Equal("/files/trader/avatar/x.jpg", cat.Traders[Prapor].AvatarUrl);
        Assert.True(cat.Traders[Prapor].IsVanilla);
        Assert.Equal("Therapist", cat.Traders[Therapist].Name);
        Assert.Null(cat.Traders[Therapist].AvatarUrl);
    }

    [Fact]
    public void Unservable_avatar_becomes_null_so_the_front_never_requests_it()
    {
        // SPT 4.1.5 Storyteller: base.json 이 실제로 없는 이미지를 가리킨다.
        var storyteller = Id(600);
        var traders = new Dictionary<MongoId, TraderBase>
        {
            [Prapor] = Trader(Prapor, "Prapor", "/files/trader/avatar/x.jpg"),
            [storyteller] = Trader(storyteller, "Storyteller", "/files/trader/avatar/missing.png"),
        };

        var cat = CatalogBuilder.Build(Input([Quest(Id(1), Prapor), Quest(Id(2), storyteller)],
            traders: traders,
            avatarIsServable: url => url != "/files/trader/avatar/missing.png"), Now);

        Assert.Equal("/files/trader/avatar/x.jpg", cat.Traders[Prapor].AvatarUrl);
        Assert.Null(cat.Traders[storyteller].AvatarUrl);
    }

    [Fact]
    public void Avatar_outside_the_image_route_is_left_alone()
    {
        // 모드가 외부 URL 을 쓰면 SPT 이미지 라우터 소관이 아니므로 판정하지 않는다.
        var modTrader = Id(601);
        var traders = new Dictionary<MongoId, TraderBase>
        {
            [modTrader] = Trader(modTrader, "Lotus", "https://example.test/lotus.jpg"),
        };

        var cat = CatalogBuilder.Build(Input([Quest(Id(1), modTrader)],
            traders: traders,
            avatarIsServable: _ => false), Now);

        Assert.Equal("https://example.test/lotus.jpg", cat.Traders[modTrader].AvatarUrl);
    }

    [Fact]
    public void Unknown_trader_is_synthesized_and_warned()
    {
        var modTrader = Id(500);
        var cat = CatalogBuilder.Build(Input([Quest(Id(1), modTrader)]), Now);

        var t = cat.Traders[modTrader];
        Assert.Equal($"Unknown ({modTrader})", t.Name);
        Assert.False(t.IsVanilla);
        Assert.Null(t.AvatarUrl);
        Assert.Single(cat.Warnings, w => w.Code == WarningCodes.UnknownTrader);
    }

    [Fact]
    public void Faction_only_comes_from_config_sets()
    {
        var cat = CatalogBuilder.Build(Input([Quest(Id(1)), Quest(Id(2)), Quest(Id(3))], bear: [Id(1)], usec: [Id(2)]), Now);

        Assert.Equal("bear", cat.Quests[Id(1)].FactionOnly);
        Assert.Equal("usec", cat.Quests[Id(2)].FactionOnly);
        Assert.Null(cat.Quests[Id(3)].FactionOnly);
    }

    [Fact]
    public void IsVanilla_uses_snapshot_and_missing_snapshot_warns()
    {
        var withSnapshot = CatalogBuilder.Build(Input([Quest(Id(1)), Quest(Id(2))], vanilla: new HashSet<string> { Id(1) }), Now);
        Assert.True(withSnapshot.Quests[Id(1)].IsVanilla);
        Assert.False(withSnapshot.Quests[Id(2)].IsVanilla);
        Assert.DoesNotContain(withSnapshot.Warnings, w => w.Code == WarningCodes.VanillaSnapshotMissing);

        var noSnapshot = CatalogBuilder.Build(Input([Quest(Id(1))], vanilla: null, snapshotVersion: null), Now);
        Assert.False(noSnapshot.Quests[Id(1)].IsVanilla);
        Assert.Single(noSnapshot.Warnings, w => w.Code == WarningCodes.VanillaSnapshotMissing);

        var mismatch = CatalogBuilder.Build(Input([Quest(Id(1))], vanilla: new HashSet<string>(), snapshotVersion: "4.1.2"), Now);
        Assert.Single(mismatch.Warnings, w => w.Code == WarningCodes.VanillaSnapshotMismatch);
    }

    [Fact]
    public void ModName_is_filled_for_non_vanilla_quests_only()
    {
        var origins = new Dictionary<string, string> { [Id(1).ToString()] = "SomeMod", [Id(2).ToString()] = "OtherMod" };

        var cat = CatalogBuilder.Build(Input([Quest(Id(1)), Quest(Id(2))],
            vanilla: new HashSet<string> { Id(2) }, modOrigins: origins), Now);

        Assert.Equal("SomeMod", cat.Quests[Id(1)].ModName);
        Assert.Null(cat.Quests[Id(2)].ModName); // 바닐라로 확정되면 origins 에 값이 있어도 무시
    }

    [Fact]
    public void Mod_scan_warnings_are_merged_into_catalog_warnings()
    {
        var scanWarning = new CatalogWarning(null, WarningCodes.ModQuestScanFailed, "BrokenMod: bad json");

        var cat = CatalogBuilder.Build(Input([Quest(Id(1))], modWarnings: [scanWarning]), Now);

        Assert.Contains(cat.Warnings, w => w.Code == WarningCodes.ModQuestScanFailed);
    }

    [Fact]
    public void Prerequisites_unlocks_and_tags_are_linked_both_ways()
    {
        // 1 → 2 → 3 (모두 Prapor), 4 는 Therapist 이고 2 를 선행으로 가짐, 5 는 고립
        var q1 = Quest(Id(1));
        var q2 = Quest(Id(2), start: [QuestCond(Id(102), Id(1))]);
        var q3 = Quest(Id(3), start: [QuestCond(Id(103), Id(2)), LevelCond(Id(104), 10)]);
        var q4 = Quest(Id(4), Therapist, start: [QuestCond(Id(105), Id(2))]);
        var q5 = Quest(Id(5));

        var cat = CatalogBuilder.Build(Input([q5, q4, q3, q2, q1]), Now);

        Assert.Equal([Id(1)], cat.Quests[Id(2)].Prerequisites);
        Assert.Equal([Id(3), Id(4)], cat.Quests[Id(2)].Unlocks);
        Assert.Equal([Id(2)], cat.Quests[Id(1)].Unlocks);
        Assert.Empty(cat.Quests[Id(3)].Unlocks);
        Assert.Equal(10, cat.Quests[Id(3)].MinLevel);
        Assert.Null(cat.Quests[Id(2)].MinLevel);

        Assert.Equal(["isolated"], cat.Quests[Id(5)].Tags);
        Assert.Equal(["traderInternal"], cat.Quests[Id(1)].Tags);   // unlocks = [2], 같은 상인
        Assert.Empty(cat.Quests[Id(2)].Tags);                        // unlocks 에 Therapist 퀘스트 포함
        Assert.Equal(["traderInternal"], cat.Quests[Id(3)].Tags);   // prereq = [2] 같은 상인, unlocks 없음
    }

    [Fact]
    public void Dangling_prerequisite_is_kept_unresolved()
    {
        var cat = CatalogBuilder.Build(Input([Quest(Id(1), start: [QuestCond(Id(101), Id(99))])]), Now);

        var req = Assert.IsType<QuestRequirement>(Assert.Single(cat.Quests[Id(1)].Requirements));
        Assert.False(req.Resolved);
        Assert.Equal([Id(99)], cat.Quests[Id(1)].Prerequisites);
        Assert.Single(cat.Warnings, w => w.Code == WarningCodes.DanglingPrereq);
    }

    [Fact]
    public void Fail_quest_conditions_become_fails_when_merged_per_target()
    {
        // 1 은 2 또는 3 이 완료되면 실패. SPT 데이터처럼 같은 대상이 두 번 들어 있어도 한 항목으로 합친다.
        // Quest 가 아닌 Fail 조건(CounterCreator 등)은 무시한다.
        var q1 = Quest(Id(1), fail:
        [
            QuestCond(Id(101), Id(2), QuestStatusEnum.Success),
            QuestCond(Id(102), Id(3), QuestStatusEnum.Success),
            QuestCond(Id(103), Id(3), QuestStatusEnum.Success),
            FinishCond(Id(104)),
        ]);
        var q2 = Quest(Id(2), fail: [QuestCond(Id(201), Id(1), QuestStatusEnum.Success)]);

        var cat = CatalogBuilder.Build(Input([q1, q2, Quest(Id(3))]), Now);

        var fails = cat.Quests[Id(1)].FailsWhen;
        Assert.Equal([Id(2).ToString(), Id(3).ToString()], fails.Select(f => f.QuestId));
        Assert.All(fails, f => Assert.Equal(["Success"], f.Statuses));
        Assert.Equal([Id(1).ToString()], cat.Quests[Id(2)].FailsWhen.Select(f => f.QuestId));
        Assert.Empty(cat.Quests[Id(3)].FailsWhen);
    }

    [Fact]
    public void Objectives_use_condition_locale()
    {
        var q = Quest(Id(1), finish: [FinishCond(Id(201), "HandoverItem", value: 3), FinishCond(Id(202), "CounterCreator")]);
        var cat = CatalogBuilder.Build(Input([q], locale: new() { [Id(201).ToString()] = "Hand over 3 mags" }), Now);

        var objs = cat.Quests[Id(1)].Objectives;
        Assert.Equal(2, objs.Count);
        Assert.Equal(new Objective(Id(201), "HandoverItem", "Hand over 3 mags", 3, null), objs[0]);
        Assert.Equal(new Objective(Id(202), "CounterCreator", "", null, null), objs[1]); // 로케일 없음 → Text 는 빈 문자열, 표시 문구는 프론트가 조립
    }

    [Fact]
    public void Objective_without_locale_falls_back_to_target_item_name_and_warns()
    {
        var tpl = Id(700);
        var q = Quest(Id(1), finish: [FinishCond(Id(201), "HandoverItem", value: 1, target: tpl)]);

        var cat = CatalogBuilder.Build(Input([q], locale: new() { [$"{tpl} Name"] = "황금 아령" }), Now);

        var obj = Assert.Single(cat.Quests[Id(1)].Objectives);
        Assert.Equal("", obj.Text);
        Assert.Equal("황금 아령", obj.TargetName);
        Assert.Single(cat.Warnings, w => w.Code == WarningCodes.MissingLocale && w.Detail.Contains(Id(201).ToString()));
    }

    [Fact]
    public void Handover_items_get_top_handbook_category_in_display_order()
    {
        const string weapons = "5b5f78dc86f77409407a7f8e", barter = "5b47574386f77428ca22b33e", modCat = "ffffffffffffffffffffffff";
        var (bolts, gun, modItem, orphan) = (Id(701), Id(702), Id(703), Id(704));
        var q = Quest(Id(1), finish:
        [
            FinishCond(Id(201), "HandoverItem", value: 1, target: bolts), FinishCond(Id(202), "HandoverItem", value: 1, target: gun),
            FinishCond(Id(203), "HandoverItem", value: 1, target: modItem), FinishCond(Id(204), "HandoverItem", value: 1, target: orphan),
        ]);
        var input = Input([q], avatarIsServable: url => url != "/files/handbook/missing.png") with
        {
            HandbookCategories = new Dictionary<string, HandbookCategoryInput>
            {
                [barter] = new(null, "/files/handbook/icon_barter.png"),
                ["b-tools"] = new(barter, "/files/handbook/icon_barter_tools.png"),
                [weapons] = new(null, "/files/handbook/missing.png"),
                [modCat] = new(null, null),
            },
            HandbookItemParents = new Dictionary<string, string> { [bolts] = "b-tools", [gun] = weapons, [modItem] = modCat },
        };

        var cat = CatalogBuilder.Build(input, Now);

        Assert.Equal(barter, cat.ItemCategoryOf[bolts]);
        Assert.Equal(weapons, cat.ItemCategoryOf[gun]);
        Assert.False(cat.ItemCategoryOf.ContainsKey(orphan));   // 핸드북에 없는 아이템
        Assert.Equal(
            [new CatalogItemCategory(weapons, null), new CatalogItemCategory(barter, "/files/handbook/icon_barter.png"), new CatalogItemCategory(modCat, null)],
            cat.ItemCategories);
    }

    [Fact]
    public void Unlock_reward_items_get_a_category_too()
    {
        const string ammo = "5b47574386f77428ca22b346", gear = "5b47574386f77428ca22b33f";
        var (bullet, case_, gift) = (Id(711), Id(712), Id(713));
        var q = Quest(Id(10), rewards: new()
        {
            ["Started"] = [Reward(RewardType.ProductionScheme, items: [Item(case_)], traderId: new SPTarkov.Server.Core.Utils.Json.StringOrInt(null, 10))],
            ["Success"] = [Reward(RewardType.AssortmentUnlock, items: [Item(bullet)], loyalty: 3, traderId: new SPTarkov.Server.Core.Utils.Json.StringOrInt(Prapor, null))],
            ["Fail"] = [Reward(RewardType.Item, items: [Item(gift)])],   // 일회성 아이템 보상은 매기지 않는다
        });
        var input = Input([q]) with
        {
            HandbookCategories = new Dictionary<string, HandbookCategoryInput> { [ammo] = new(null, null), [gear] = new(null, null) },
            HandbookItemParents = new Dictionary<string, string> { [bullet] = ammo, [case_] = gear, [gift] = gear },
        };

        var cat = CatalogBuilder.Build(input, Now);

        Assert.Equal(ammo, cat.ItemCategoryOf[bullet]);
        Assert.Equal(gear, cat.ItemCategoryOf[case_]);
        Assert.False(cat.ItemCategoryOf.ContainsKey(gift));
        Assert.Equal([new CatalogItemCategory(ammo, null), new CatalogItemCategory(gear, null)], cat.ItemCategories);
    }

    [Fact]
    public void Rewards_are_split_by_phase_and_indexed_by_success_only()
    {
        var m4 = Id(1);
        var items = new Dictionary<MongoId, TemplateItem>
        {
            [m4] = Template(m4, BaseClasses.ASSAULT_RIFLE), [BaseClasses.ASSAULT_RIFLE] = Template(BaseClasses.ASSAULT_RIFLE, BaseClasses.WEAPON), [BaseClasses.WEAPON] = Template(BaseClasses.WEAPON, default),
        };
        var q = Quest(Id(10), rewards: new()
        {
            ["Started"] = [Reward(RewardType.Item, items: [Item(m4)])],
            ["Success"] = [Reward(RewardType.Experience, value: 100), Reward(RewardType.AssortmentUnlock, items: [Item(m4)], loyalty: 1, traderId: new SPTarkov.Server.Core.Utils.Json.StringOrInt(Prapor, null)), Reward(RewardType.TraderUnlock, target: Therapist)],
            ["Fail"] = [],
        });

        var cat = CatalogBuilder.Build(Input([q], items: items), Now);

        var r = cat.Quests[Id(10)].Rewards;
        Assert.Single(r.Started);
        Assert.Equal(3, r.Success.Count);
        Assert.Empty(r.Fail);
        Assert.Equal([Id(10)], cat.RewardIndex["weapon"]);       // success 의 AssortmentUnlock(weapon) 로 인덱스
        Assert.Equal([Id(10)], cat.RewardIndex["assortUnlock"]);
        Assert.Equal([Id(10)], cat.RewardIndex["traderUnlock"]);
        Assert.Empty(cat.RewardIndex["production"]);
        Assert.Empty(cat.RewardIndex["armor"]);
    }

    [Fact]
    public void Missing_rewards_dictionary_yields_empty_lists()
    {
        var q = Quest(Id(1));
        q.Rewards = null;
        var cat = CatalogBuilder.Build(Input([q]), Now);
        Assert.Empty(cat.Quests[Id(1)].Rewards.Success);
    }

    [Fact]
    public void Image_is_passed_through_or_null()
    {
        var cat = CatalogBuilder.Build(Input([Quest(Id(1), image: "/files/quest/icon/a.jpg"), Quest(Id(2), image: "")]), Now);
        Assert.Equal("/files/quest/icon/a.jpg", cat.Quests[Id(1)].ImageUrl);
        Assert.Null(cat.Quests[Id(2)].ImageUrl);
    }

    [Fact]
    public void One_broken_quest_does_not_break_the_build()
    {
        var broken = Quest(Id(2));
        broken.Conditions = null!;   // NRE 유발

        var cat = CatalogBuilder.Build(Input([Quest(Id(1)), broken, Quest(Id(3))]), Now);

        Assert.Equal([Id(1).ToString(), Id(3).ToString()], cat.Quests.Keys);
        var w = Assert.Single(cat.Warnings, w => w.Code == WarningCodes.BuildFailed);
        Assert.Equal(Id(2).ToString(), w.QuestId);
        Assert.Contains("NullReferenceException", w.Detail);
    }

    [Fact]
    public void Output_is_sorted_and_carries_metadata()
    {
        var cat = CatalogBuilder.Build(Input([Quest(Id(3)), Quest(Id(1), Therapist), Quest(Id(2))]), Now);

        Assert.Equal([Id(1).ToString(), Id(2).ToString(), Id(3).ToString()], cat.Quests.Keys);
        Assert.Equal(new[] { Prapor.ToString(), Therapist.ToString() }.Order(StringComparer.Ordinal), cat.Traders.Keys);
        Assert.Equal("kr", cat.Lang);
        Assert.Equal("4.1.5", cat.SptVersion);
        Assert.Equal("0.2.0", cat.ModVersion);
        Assert.Equal(Now, cat.GeneratedAt);
        Assert.Contains("weapon", cat.RewardIndex.Keys);
    }

    [Fact]
    public void Objectives_carry_locations_filtered_by_quest_map()
    {
        var customs = new MapPoint(-334.93, 2.22, -163.46);
        var reserve = new MapPoint(-334.93, -101.46, -163.46);
        var q = Quest(Id(1), finish:
        [
            new QuestCondition { Id = Id(11), ConditionType = "PlaceBeacon", DynamicLocale = false, ZoneId = "fuel4" },
            new QuestCondition { Id = Id(12), ConditionType = "HandoverItem", DynamicLocale = false },
        ]);
        q.Location = "56f40101d2720b2a4d8b45d6";
        var zones = new PointTableBuilder().Add("bigmap", "fuel4", customs).Add("rezervbase", "fuel4", reserve).Build();

        var cat = CatalogBuilder.Build(Input([q]) with
        {
            QuestZones = zones,
            LocationKeys = new Dictionary<string, string> { ["56f40101d2720b2a4d8b45d6"] = "bigmap" },
        }, Now);

        var objectives = cat.Quests[Id(1)].Objectives;
        var location = Assert.Single(objectives[0].Locations);
        Assert.Equal("bigmap", location.Map);
        Assert.Equal([customs], location.Points);
        Assert.Empty(objectives[1].Locations);
        Assert.DoesNotContain(cat.Warnings, w => w.Code == WarningCodes.QuestZoneNotFound);
    }

    [Fact]
    public void Missing_zone_snapshot_warns_once_and_zone_lookups_still_warn()
    {
        var q = Quest(Id(1), finish: [new QuestCondition { Id = Id(11), ConditionType = "PlaceBeacon", DynamicLocale = false, ZoneId = "fuel4" }]);

        var cat = CatalogBuilder.Build(Input([q]) with { QuestZoneSnapshotMissing = true }, Now);

        Assert.Single(cat.Warnings, w => w.Code == WarningCodes.QuestZoneSnapshotMissing && w.QuestId is null);
        Assert.Single(cat.Warnings, w => w.Code == WarningCodes.QuestZoneNotFound && w.QuestId == Id(1).ToString());
        Assert.Empty(cat.Quests[Id(1)].Objectives[0].Locations);
    }

    [Fact]
    public void Locked_doors_carry_localized_key_names_and_kind()
    {
        var key = Id(710);
        var keycard = Id(711);
        var unnamed = Id(712);
        var doors = new Dictionary<string, IReadOnlyList<SnapshotDoor>>
        {
            ["laboratory"] =
            [
                new(keycard.ToString(), "KeycardDoor", new MapPoint(1, 2, 3)),
                new(key.ToString(), "Door", new MapPoint(4, 5, 6)),
            ],
            ["bigmap"] = [new(unnamed.ToString(), "Door", new MapPoint(7, 8, 9))],
        };
        var locale = new Dictionary<string, string> { [$"{key} Name"] = "기숙사 314호 열쇠", [$"{keycard} Name"] = "빨간 키카드" };

        var cat = CatalogBuilder.Build(Input([], locale: locale) with { LockedDoors = doors }, Now);

        Assert.Equal(["bigmap", "laboratory"], cat.LockedDoors.Keys);
        Assert.Equal(
            [
                new LockedDoor(keycard.ToString(), "빨간 키카드", "keycard", new MapPoint(1, 2, 3)),
                new LockedDoor(key.ToString(), "기숙사 314호 열쇠", "door", new MapPoint(4, 5, 6)),
            ],
            cat.LockedDoors["laboratory"]);
        Assert.Equal(unnamed.ToString(), cat.LockedDoors["bigmap"][0].KeyName); // 이름이 어디에도 없으면 tpl 그대로
    }

    [Fact]
    public void Icebreaker_keyless_doors_carry_their_kind_item_and_code()
    {
        var charge = Id(720);
        var torch = Id(721);
        var doors = new Dictionary<string, IReadOnlyList<SnapshotDoor>>
        {
            ["icebreaker"] =
            [
                new("", "Keypad", new MapPoint(1, 2, 3), "312220"),
                new("", "Keypad", new MapPoint(4, 5, 6)),
                new(charge.ToString(), "Explosive", new MapPoint(7, 8, 9)),
                new(torch.ToString(), "Hatch", new MapPoint(10, 11, 12)),
            ],
        };
        var locale = new Dictionary<string, string> { [$"{charge} Name"] = "SZ-1 폭약", [$"{torch} Name"] = "가스 토치" };

        var cat = CatalogBuilder.Build(Input([], locale: locale) with { LockedDoors = doors }, Now);

        Assert.Equal(
            [
                new LockedDoor("", "", "keypad", new MapPoint(1, 2, 3), "312220"),
                new LockedDoor("", "", "keypad", new MapPoint(4, 5, 6)),
                new LockedDoor(charge.ToString(), "SZ-1 폭약", "explosive", new MapPoint(7, 8, 9)),
                new LockedDoor(torch.ToString(), "가스 토치", "hatch", new MapPoint(10, 11, 12)),
            ],
            cat.LockedDoors["icebreaker"]);
    }

    [Fact]
    public void Locked_doors_are_empty_without_input()
        => Assert.Empty(CatalogBuilder.Build(Input([]), Now).LockedDoors);

    [Fact]
    public void Null_input_throws()
        => Assert.Throws<ArgumentNullException>(() => CatalogBuilder.Build(null!, Now));
}
