using QuestCodex.Catalog.Models;
using QuestCodex.Progress.Models;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Enums;

namespace QuestCodex.Progress;

/// <summary>
/// 캐시된 카탈로그 + 메모리 프로필 → 프로필 진행 상태. 순수 함수, 캐시 없음.
/// </summary>
public static class ProgressBuilder
{
    private const string Locked = nameof(QuestStatusEnum.Locked);

    public static ProfileProgress Build(QuestCodex.Catalog.Models.Catalog catalog, PmcData pmc, string profileId, bool isActive)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(pmc);

        var warnings = new List<CatalogWarning>();
        var statusById = new Dictionary<string, SPTarkov.Server.Core.Models.Eft.Common.Tables.QuestStatus>(StringComparer.Ordinal);
        foreach (var entry in pmc.Quests ?? [])
        {
            var qid = entry.QId.ToString();
            if (!catalog.Quests.ContainsKey(qid))
            {
                warnings.Add(new CatalogWarning(qid, WarningCodes.OrphanQuest, "profile has a quest that is not in the catalog"));
                continue;
            }

            statusById[qid] = entry;
        }

        var counters = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var kv in pmc.TaskConditionCounters ?? [])
        {
            counters[(kv.Value.Id ?? kv.Key).ToString()] = kv.Value.Value ?? 0;
        }

        var level = pmc.Info?.Level;
        var side = pmc.Info?.Side;

        var quests = new SortedDictionary<string, QuestProgress>(StringComparer.Ordinal);
        var stats = new SortedDictionary<string, TraderStats>(StringComparer.Ordinal);
        foreach (var traderId in catalog.Traders.Keys) stats[traderId] = new TraderStats();

        foreach (var cq in catalog.Quests.Values)
        {
            var status = StatusOf(cq.Id, statusById);
            statusById.TryGetValue(cq.Id, out var entry);

            var lockReasons = status == Locked ? LockReasons(cq, statusById, pmc, level ?? 0, side) : [];
            var finished = status is nameof(QuestStatusEnum.Success) or nameof(QuestStatusEnum.Fail);
            var objectives = new SortedDictionary<string, ObjectiveProgress>(StringComparer.Ordinal);
            foreach (var o in cq.Objectives)
            {
                var current = counters.GetValueOrDefault(o.ConditionId, 0);
                var done = finished || (o.TargetCount is { } target && current >= target);
                objectives[o.ConditionId] = new ObjectiveProgress(current, o.TargetCount, done);
            }

            quests[cq.Id] = new QuestProgress(
                status,
                entry is { StartTime: > 0 } ? DateTimeOffset.FromUnixTimeSeconds((long)entry.StartTime) : null,
                FinishTimeOf(entry),
                lockReasons,
                objectives);

            if (!stats.TryGetValue(cq.TraderId, out var ts))
            {
                ts = new TraderStats();
                stats[cq.TraderId] = ts;
            }

            ts.Total++;
            switch (status)
            {
                case nameof(QuestStatusEnum.Success): ts.Success++; break;
                case nameof(QuestStatusEnum.Started): ts.Started++; break;
                case nameof(QuestStatusEnum.AvailableForStart): ts.AvailableForStart++; break;
                case Locked: ts.Locked++; break;
                default: ts.Other++; break;
            }
        }

        return new ProfileProgress(
            profileId,
            pmc.Info?.Nickname ?? profileId,
            level,
            side,
            isActive,
            quests,
            stats,
            warnings);
    }

    /// <summary>CompareMethod 해석. 모르는 연산자는 >= 로 간주.</summary>
    public static bool Compare(double current, string op, double need) => op switch
    {
        ">" => current > need,
        "<=" => current <= need,
        "<" => current < need,
        "=" or "==" => Math.Abs(current - need) < 1e-9,
        _ => current >= need,
    };

    private static string StatusOf(string questId, Dictionary<string, SPTarkov.Server.Core.Models.Eft.Common.Tables.QuestStatus> statusById)
        => statusById.TryGetValue(questId, out var e) ? e.Status.ToString() : Locked;

    private static DateTimeOffset? FinishTimeOf(SPTarkov.Server.Core.Models.Eft.Common.Tables.QuestStatus? entry)
    {
        if (entry?.StatusTimers is null) return null;
        foreach (var s in new[] { QuestStatusEnum.Success, QuestStatusEnum.Fail })
        {
            if (entry.StatusTimers.TryGetValue(s, out var t) && t > 0) return DateTimeOffset.FromUnixTimeSeconds((long)t);
        }

        return null;
    }

    private static List<LockReason> LockReasons(
        CatalogQuest cq,
        Dictionary<string, SPTarkov.Server.Core.Models.Eft.Common.Tables.QuestStatus> statusById,
        PmcData pmc,
        int level,
        string? side)
    {
        if (cq.FactionOnly is not null && !string.Equals(cq.FactionOnly, side, StringComparison.OrdinalIgnoreCase))
        {
            return [new FactionLockReason(cq.FactionOnly)];
        }

        var reasons = new List<LockReason>();
        foreach (var req in cq.Requirements)
        {
            switch (req)
            {
                case QuestRequirement q:
                {
                    var current = StatusOf(q.QuestId, statusById);
                    if (!q.NeedStatuses.Contains(current)) reasons.Add(new QuestLockReason(q.QuestId, q.NeedStatuses, current));
                    break;
                }
                case LevelRequirement l:
                    if (!Compare(level, l.Compare, l.Value)) reasons.Add(new LevelLockReason(l.Value, l.Compare, level));
                    break;
                case TraderLoyaltyRequirement tl:
                {
                    var current = TraderInfoOf(pmc, tl.TraderId)?.LoyaltyLevel ?? 0;
                    if (!Compare(current, tl.Compare, tl.Value)) reasons.Add(new TraderLoyaltyLockReason(tl.TraderId, tl.Value, tl.Compare, current));
                    break;
                }
                case TraderStandingRequirement tsr:
                {
                    var current = TraderInfoOf(pmc, tsr.TraderId)?.Standing ?? 0;
                    if (!Compare(current, tsr.Compare, tsr.Value)) reasons.Add(new TraderStandingLockReason(tsr.TraderId, tsr.Value, tsr.Compare, current));
                    break;
                }
                case OtherRequirement o:
                    reasons.Add(new OtherLockReason(o.ConditionType));
                    break;
            }
        }

        return reasons;
    }

    private static SPTarkov.Server.Core.Models.Eft.Common.Tables.TraderInfo? TraderInfoOf(PmcData pmc, string traderId)
    {
        if (pmc.TradersInfo is null || !MongoId.IsValidMongoId(traderId)) return null;
        return pmc.TradersInfo.TryGetValue(new MongoId(traderId), out var info) ? info : null;
    }
}
