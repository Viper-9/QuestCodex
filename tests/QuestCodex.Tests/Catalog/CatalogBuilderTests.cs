using QuestCodex.Catalog;
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
    public void Null_input_throws()
        => Assert.Throws<ArgumentNullException>(() => CatalogBuilder.Build(null!, Now));
}
