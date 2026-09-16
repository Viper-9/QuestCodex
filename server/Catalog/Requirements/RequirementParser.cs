using QuestCodex.Catalog.Models;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;

namespace QuestCodex.Catalog.Requirements;

/// <summary>
/// AvailableForStart 조건 하나를 Requirement 로 바꾼다. 모르는 타입은 버리지 않고 OtherRequirement 로 보존한다.
/// SPT 4.1.5 의 QuestCondition 은 평면(하위 조건 없음)이므로 재귀가 없다.
/// </summary>
public static class RequirementParser
{
    private const string DefaultCompare = ">=";

    public static Requirement Parse(
        QuestCondition c,
        LocaleResolver locale,
        IReadOnlyDictionary<MongoId, Quest> quests,
        List<CatalogWarning> warnings,
        string questId)
    {
        try
        {
            switch (c.ConditionType)
            {
                case "Quest":
                {
                    var target = TargetOf(c);
                    if (target is null)
                    {
                        warnings.Add(new CatalogWarning(questId, WarningCodes.UnparsedCondition, $"Quest condition {c.Id} has no target"));
                        return Other(c, locale);
                    }

                    var statuses = c.Status is { Count: > 0 }
                        ? c.Status.Select(s => s.ToString()).ToList()
                        : [QuestStatusEnum.Success.ToString()];
                    var resolved = MongoId.IsValidMongoId(target) && quests.ContainsKey(new MongoId(target));
                    if (!resolved)
                    {
                        warnings.Add(new CatalogWarning(questId, WarningCodes.DanglingPrereq, $"prerequisite {target} not in quest table"));
                    }

                    return new QuestRequirement(target, statuses, c.AvailableAfter ?? 0, resolved);
                }
                case "Level":
                    return new LevelRequirement(c.Value ?? 0, c.CompareMethod ?? DefaultCompare);
                case "TraderLoyalty":
                case "TraderStanding":
                {
                    var trader = TargetOf(c);
                    if (trader is null)
                    {
                        warnings.Add(new CatalogWarning(questId, WarningCodes.UnparsedCondition, $"{c.ConditionType} condition {c.Id} has no target"));
                        return Other(c, locale);
                    }

                    return c.ConditionType == "TraderLoyalty"
                        ? new TraderLoyaltyRequirement(trader, c.Value ?? 0, c.CompareMethod ?? DefaultCompare)
                        : new TraderStandingRequirement(trader, c.Value ?? 0, c.CompareMethod ?? DefaultCompare);
                }
                default:
                    return Other(c, locale);
            }
        }
        catch (Exception ex)
        {
            warnings.Add(new CatalogWarning(questId, WarningCodes.UnparsedCondition, $"{c.ConditionType} {c.Id}: {ex.GetType().Name}: {ex.Message}"));
            return Other(c, locale);
        }
    }

    /// <summary>ListOrT 의 단일 값 또는 리스트 첫 항목. 둘 다 없으면 null.</summary>
    public static string? TargetOf(QuestCondition c)
    {
        if (c.Target is null) return null;
        if (c.Target.IsItem) return c.Target.Item;
        return c.Target.List is { Count: > 0 } list ? list[0] : null;
    }

    private static OtherRequirement Other(QuestCondition c, LocaleResolver locale)
        => new(c.ConditionType, locale.TryResolve(c.Id.ToString()) ?? c.ConditionType);
}
