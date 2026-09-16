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
        Dictionary<MongoId, TraderBase>? traders = null)
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
            VanillaSnapshotSptVersion: snapshotVersion);

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
    public void Objectives_use_condition_locale_and_flags()
    {
        var q = Quest(Id(1), finish: [FinishCond(Id(201), "HandoverItem", value: 3, necessary: false), FinishCond(Id(202), "CounterCreator")]);
        var cat = CatalogBuilder.Build(Input([q], locale: new() { [Id(201).ToString()] = "Hand over 3 mags" }), Now);

        var objs = cat.Quests[Id(1)].Objectives;
        Assert.Equal(2, objs.Count);
        Assert.Equal(new Objective(Id(201), "HandoverItem", "Hand over 3 mags", 3, true), objs[0]);
        Assert.Equal(new Objective(Id(202), "CounterCreator", "CounterCreator", null, false), objs[1]);
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
