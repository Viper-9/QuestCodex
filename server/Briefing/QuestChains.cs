using QuestCodex.Catalog.Models;
using CatalogModel = QuestCodex.Catalog.Models.Catalog;

namespace QuestCodex.Briefing;

// 목표 퀘스트를 열려면 끝내야 하는 퀘스트를 모은다. 웹 카파 트래커 kappaGraph(web/src/progress/kappa.ts)의 ids 와 같은 규칙이다.
// Success 를 요구하는 퀘스트 조건만 거꾸로 끝까지 따라가고, 목표 자신과 카탈로그에 없는 퀘스트는 뺀다.
public static class QuestChains
{
    // 등대지기 첫 퀘스트. Knock-Knock 을 요구한다.
    public const string InformationSourceId = "63966faeea19ac7ed845db2c";

    public static IReadOnlySet<string> PrerequisitesOf(CatalogModel catalog, string goalId)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (!catalog.Quests.TryGetValue(goalId, out var goal)) return ids;

        var stack = new Stack<string>(SuccessRequirements(goal));
        while (stack.TryPop(out var id))
        {
            if (id == goalId || ids.Contains(id) || !catalog.Quests.TryGetValue(id, out var q)) continue;
            ids.Add(id);
            foreach (var r in SuccessRequirements(q)) stack.Push(r);
        }
        return ids;
    }

    private static IEnumerable<string> SuccessRequirements(CatalogQuest q)
        => q.Requirements.OfType<QuestRequirement>().Where(r => r.NeedStatuses.Contains("Success")).Select(r => r.QuestId);
}
