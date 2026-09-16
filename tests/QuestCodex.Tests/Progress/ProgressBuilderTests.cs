using QuestCodex.Catalog;
using QuestCodex.Catalog.Models;
using QuestCodex.Progress;
using QuestCodex.Progress.Models;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using static QuestCodex.Tests.Fixtures;

namespace QuestCodex.Tests.Progress;

public class ProgressBuilderTests
{
    // 카탈로그: 1(Prapor, lvl>=5) → 2(Prapor, needs 1 Success, TraderLoyalty Prapor>=2) ; 3(Therapist, bear only) ; 4 고립 + finish 조건 2개
    private static QuestCodex.Catalog.Models.Catalog BuildCatalog()
    {
        var q1 = Quest(Id(1), start: [LevelCond(Id(101), 5)]);
        var q2 = Quest(Id(2), start: [QuestCond(Id(102), Id(1)), TraderCond(Id(103), "TraderLoyalty", Prapor, 2)]);
        var q3 = Quest(Id(3), Therapist);
        var q4 = Quest(Id(4), finish: [FinishCond(Id(401), value: 3), FinishCond(Id(402), "PlaceBeacon")]);
        var q5 = Quest(Id(5), start: [FinishCond(Id(501), "Skill", value: 10)]);   // other 조건
        var input = new CatalogInput("en", "4.1.5", "0.2.0",
            new[] { q1, q2, q3, q4, q5 }.ToDictionary(q => q.Id),
            new Dictionary<MongoId, TraderBase> { [Prapor] = Trader(Prapor, "Prapor"), [Therapist] = Trader(Therapist, "Therapist") },
            new Dictionary<MongoId, TemplateItem>(), new Dictionary<string, string>(), new Dictionary<string, string>(),
            new HashSet<MongoId> { Id(3) }, new HashSet<MongoId>(), null, null);
        return CatalogBuilder.Build(input, DateTimeOffset.UnixEpoch);
    }

    private static readonly QuestCodex.Catalog.Models.Catalog Cat = BuildCatalog();

    [Fact]
    public void Quests_absent_from_profile_are_locked_with_reasons()
    {
        var pmc = Pmc(level: 3);

        var p = ProgressBuilder.Build(Cat, pmc, "p1", isActive: true);

        var q1 = p.Quests[Id(1)];
        Assert.Equal("Locked", q1.Status);
        Assert.Null(q1.StartTime);
        Assert.Equal(new LevelLockReason(5, ">=", 3), Assert.Single(q1.LockReasons));

        var q2 = p.Quests[Id(2)];
        Assert.Collection(q2.LockReasons,
            r =>
            {
                // record 의 IReadOnlyList 는 참조 비교라 필드별로 확인한다
                var ql = Assert.IsType<QuestLockReason>(r);
                Assert.Equal(Id(1).ToString(), ql.QuestId);
                Assert.Equal(["Success"], ql.NeedStatuses);
                Assert.Equal("Locked", ql.CurrentStatus);
            },
            r => Assert.Equal(new TraderLoyaltyLockReason(Prapor, 2, ">=", 0), r));
        Assert.Equal("p1", p.ProfileId);
        Assert.True(p.IsActive);
        Assert.Equal(3, p.Level);
        Assert.Equal("Usec", p.Side);
    }

    [Fact]
    public void Satisfied_requirements_produce_no_reasons()
    {
        var pmc = Pmc(level: 10);
        pmc.Quests!.Add(ProfileQuest(Id(1), QuestStatusEnum.Success));
        pmc.TradersInfo![Prapor] = new TraderInfo { LoyaltyLevel = 3, Standing = 0.5 };

        var p = ProgressBuilder.Build(Cat, pmc, "p1", false);

        Assert.Empty(p.Quests[Id(1)].LockReasons);   // Success 라 계산 안 함
        Assert.Empty(p.Quests[Id(2)].LockReasons);   // Locked 지만 조건 전부 충족(SPT 가 아직 안 풀어준 상태)
    }

    [Fact]
    public void Faction_mismatch_short_circuits_other_reasons()
    {
        var p = ProgressBuilder.Build(Cat, Pmc(level: 1, side: "Usec"), "p1", false);
        Assert.Equal(new FactionLockReason("bear"), Assert.Single(p.Quests[Id(3)].LockReasons));

        var bear = ProgressBuilder.Build(Cat, Pmc(level: 1, side: "bear"), "p1", false);
        Assert.Empty(bear.Quests[Id(3)].LockReasons);
    }

    [Fact]
    public void Other_requirement_is_reported_as_other_reason()
        => Assert.Equal(new OtherLockReason("Skill"), Assert.Single(ProgressBuilder.Build(Cat, Pmc(), "p", false).Quests[Id(5)].LockReasons));

    [Fact]
    public void Status_and_times_come_from_profile()
    {
        var pmc = Pmc();
        var entry = ProfileQuest(Id(1), QuestStatusEnum.Success, start: 1_700_000_000);
        entry.StatusTimers[QuestStatusEnum.Success] = 1_700_003_600;
        pmc.Quests!.Add(entry);
        pmc.Quests.Add(ProfileQuest(Id(2), QuestStatusEnum.Started, start: 1_700_010_000));

        var p = ProgressBuilder.Build(Cat, pmc, "p", false);

        Assert.Equal("Success", p.Quests[Id(1)].Status);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000), p.Quests[Id(1)].StartTime);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_003_600), p.Quests[Id(1)].FinishTime);
        Assert.Equal("Started", p.Quests[Id(2)].Status);
        Assert.Null(p.Quests[Id(2)].FinishTime);
        Assert.Null(ProgressBuilder.Build(Cat, Pmc(), "p", false).Quests[Id(1)].StartTime);   // StartTime 0 → null
    }

    [Fact]
    public void Objectives_match_task_condition_counters()
    {
        var pmc = Pmc();
        pmc.Quests!.Add(ProfileQuest(Id(4), QuestStatusEnum.Started));
        pmc.TaskConditionCounters![Id(900)] = new TaskConditionCounter { Id = Id(401), Value = 2 };

        var objs = ProgressBuilder.Build(Cat, pmc, "p", false).Quests[Id(4)].Objectives;

        Assert.Equal(new ObjectiveProgress(2, 3, false), objs[Id(401)]);
        Assert.Equal(new ObjectiveProgress(0, null, false), objs[Id(402)]);

        pmc.TaskConditionCounters[Id(900)].Value = 3;
        Assert.True(ProgressBuilder.Build(Cat, pmc, "p", false).Quests[Id(4)].Objectives[Id(401)].Done);
    }

    [Fact]
    public void Finished_quest_marks_all_objectives_done()
    {
        var pmc = Pmc();
        pmc.Quests!.Add(ProfileQuest(Id(4), QuestStatusEnum.Success));
        var objs = ProgressBuilder.Build(Cat, pmc, "p", false).Quests[Id(4)].Objectives;
        Assert.All(objs.Values, o => Assert.True(o.Done));
    }

    [Fact]
    public void Trader_stats_are_aggregated()
    {
        var pmc = Pmc();
        pmc.Quests!.Add(ProfileQuest(Id(1), QuestStatusEnum.Success));
        pmc.Quests.Add(ProfileQuest(Id(2), QuestStatusEnum.Started));
        pmc.Quests.Add(ProfileQuest(Id(4), QuestStatusEnum.Fail));

        var stats = ProgressBuilder.Build(Cat, pmc, "p", false).TraderStats;

        var prapor = stats[Prapor];
        Assert.Equal(4, prapor.Total);      // 1,2,4,5
        Assert.Equal(1, prapor.Success);
        Assert.Equal(1, prapor.Started);
        Assert.Equal(1, prapor.Locked);     // 5
        Assert.Equal(1, prapor.Other);      // 4 Fail
        Assert.Equal(1, stats[Therapist].Total);
        Assert.Equal(1, stats[Therapist].Locked);
    }

    [Fact]
    public void Orphan_profile_quest_is_warned_not_thrown()
    {
        var pmc = Pmc();
        pmc.Quests!.Add(ProfileQuest(Id(999), QuestStatusEnum.Success));
        var p = ProgressBuilder.Build(Cat, pmc, "p", false);
        Assert.Equal(WarningCodes.OrphanQuest, Assert.Single(p.Warnings).Code);
        Assert.False(p.Quests.ContainsKey(Id(999)));
    }

    [Fact]
    public void Null_collections_on_pmc_are_tolerated()
    {
        var pmc = Pmc();
        pmc.Quests = null; pmc.TradersInfo = null; pmc.TaskConditionCounters = null;
        var p = ProgressBuilder.Build(Cat, pmc, "p", false);
        Assert.Equal(5, p.Quests.Count);
    }

    [Theory]
    [InlineData(5, ">=", 5, true)]
    [InlineData(4, ">=", 5, false)]
    [InlineData(6, ">", 5, true)]
    [InlineData(5, "<=", 5, true)]
    [InlineData(4, "<", 5, true)]
    [InlineData(5, "=", 5, true)]
    [InlineData(5, "==", 5, true)]
    [InlineData(5, "weird", 5, true)]
    [InlineData(4, "weird", 5, false)]
    public void Compare_handles_all_operators(double current, string op, double need, bool expected)
        => Assert.Equal(expected, ProgressBuilder.Compare(current, op, need));
}
