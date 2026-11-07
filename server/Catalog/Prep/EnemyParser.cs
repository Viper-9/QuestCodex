using QuestCodex.Catalog.Models;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace QuestCodex.Catalog.Prep;

/// <summary>
/// AvailableForFinish 조건 하나 → 인정하는 적 목록. 게임 문장은 사람이 쓴 것(dynamicLocale false)이라
/// "보스 처치"처럼 뭉뚱그리거나 경호원 포함 여부를 빠뜨리므로 판정에 쓰는 값을 그대로 꺼낸다.
/// </summary>
public static class EnemyParser
{
    public static IReadOnlyList<EnemyRule> Parse(QuestCondition c, EnemyNames names)
    {
        var rules = new List<EnemyRule>();
        foreach (var sub in c.Counter?.Conditions ?? [])
        {
            var kind = sub.ConditionType switch
            {
                "Kills" => "kills",
                "Shots" => "shots",
                _ => null,
            };
            if (kind is null) continue;

            var target = sub.Target is { IsItem: true } t ? t.Item : sub.Target?.List?.FirstOrDefault();
            var roles = (sub.SavageRole ?? []).Where(r => !string.IsNullOrWhiteSpace(r)).Distinct().ToList();
            rules.Add(new EnemyRule(kind, target ?? "", roles, names.Of(roles)));
        }

        return rules.Count == 0 ? Array.Empty<EnemyRule>() : rules;
    }

    /// <summary>택1 묶음을 합칠 때 선택지들의 목록을 순서대로 합치고 같은 것은 한 번만 남긴다.</summary>
    public static IReadOnlyList<EnemyRule> Merge(IEnumerable<IReadOnlyList<EnemyRule>> lists)
    {
        var merged = lists.SelectMany(l => l)
            .DistinctBy(e => (e.Kind, e.Target, string.Join('\u0001', e.Roles)))
            .ToList();
        return merged.Count == 0 ? Array.Empty<EnemyRule>() : merged;
    }
}
