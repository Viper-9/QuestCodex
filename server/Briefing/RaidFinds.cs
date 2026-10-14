using QuestCodex.Catalog.Models;
using QuestCodex.Progress.Models;
using CatalogModel = QuestCodex.Catalog.Models.Catalog;

namespace QuestCodex.Briefing;

// 레이드에서 구해 와야 하는 제출 아이템. 웹 derive.ts 의 raidFinds·findMaps·placeFinds 를 옮긴 것.

// Place 값은 "here"(이 맵), "unknown"(맵 모름), "elsewhere"(다른 맵) 셋 중 하나.
public sealed record PlacedRow(NeedRow Row, IReadOnlyList<string> Maps, string Place);

public static class RaidFinds
{
    // 루블·달러·유로. 돈은 레이드에서 줍는 게 아니라 버는 거라 뺀다. 웹 derive.ts 의 CURRENCIES 와 같은 목록이라 바꿀 때 같이 고친다.
    private static readonly HashSet<string> Currencies = new(StringComparer.Ordinal)
        { "5449016a4bdc2d6f028b456f", "5696686a4bdc2da3298b456a", "569668774bdc2da2298b4568" };

    // 진행 중 퀘스트의 남은 제출 중 가진 걸로 못 채우는 것. 아이템별로 합산해서 보고, 부족한 수가 많은 순.
    public static List<NeedRow> Rows(CatalogModel catalog, ProfileProgress progress, IReadOnlyDictionary<string, ItemHolding> inventory, StringComparer? names = null)
    {
        var handovers = RaidNeeds.ItemNeeds(catalog, progress, qp => qp.Status == "Started")
            .Where(n => n.Item.Action == "handover" && !n.Item.Items.All(i => Currencies.Contains(i.Tpl)));
        return RaidNeeds.SortNeedRows(RaidNeeds.AggregateNeeds(handovers, inventory).Where(r => RaidNeeds.Missing(r) > 0), names);
    }

    // 이 아이템을 요구하는 퀘스트의 찾기(FindItem) 목표 문장에서 맵을 뽑는다. 못 찾으면 빈 목록.
    public static List<string> FindMaps(NeedRow row, CatalogModel catalog)
    {
        var maps = new List<string>();
        foreach (var questId in row.Sources.Select(s => s.QuestId).Distinct())
        {
            if (!catalog.Quests.TryGetValue(questId, out var quest)) continue;
            foreach (var o in quest.Objectives)
            {
                if (o.ConditionType != "FindItem") continue;
                foreach (var m in RaidPlan.MapsFromText(o.Text)) if (!maps.Contains(m)) maps.Add(m);
            }
        }
        return maps;
    }

    // 이 맵, 맵 모름, 다른 맵 순으로 놓는다. OrderBy 는 안정 정렬이라 같은 묶음 안에서는 들어온 순서가 유지된다.
    public static List<PlacedRow> Place(IEnumerable<NeedRow> rows, CatalogModel catalog, string map)
        => rows
            .Select(row =>
            {
                var maps = FindMaps(row, catalog);
                var place = maps.Count == 0 ? "unknown" : maps.Contains(map) ? "here" : "elsewhere";
                return new PlacedRow(row, maps, place);
            })
            .OrderBy(p => p.Place switch { "here" => 0, "unknown" => 1, _ => 2 })
            .ToList();
}
