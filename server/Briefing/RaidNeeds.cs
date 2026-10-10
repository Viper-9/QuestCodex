using System.Globalization;
using QuestCodex.Catalog.Models;
using QuestCodex.Progress.Models;
using CatalogModel = QuestCodex.Catalog.Models.Catalog;

namespace QuestCodex.Briefing;

// 웹 web/src/progress/derive.ts 의 아이템 합산 부분을 옮긴 것. 함수 이름도 웹과 맞췄다.
// 웹과 결과가 같아야 하니 규칙을 바꿀 때는 양쪽을 같이 고친다.

public sealed record NeedSource(string QuestId, string ConditionId, string Action, double Count, bool Fir);

public sealed class NeedRow
{
    public required string Key { get; init; }
    public required IReadOnlyList<ItemRef> Items { get; init; }
    // 남은 수 합계. FIR 이어야 하는 수도 여기 들어 있다.
    public double Need { get; set; }
    // Need 중 FIR 이어야 하는 수
    public double NeedFir { get; set; }
    public required double Have { get; init; }
    public required double HaveFir { get; init; }
    public List<NeedSource> Sources { get; } = [];
}

// 준비 아이템(Prep.Item)이 있는 목표로만 만든다.
public sealed record ItemNeed(CatalogQuest Quest, Objective Objective, double Count)
{
    public PrepItem Item => Objective.Prep!.Item!;
}

public static class RaidNeeds
{
    // 기본 이름 정렬. 서버 PC 의 문화권 설정에 따라 순서가 달라지지 않게 Invariant 로 둔다.
    public static readonly StringComparer NameOrder = StringComparer.Create(CultureInfo.InvariantCulture, ignoreCase: false);

    private static readonly Dictionary<string, StringComparer> NameOrders = new(StringComparer.Ordinal)
    {
        ["kr"] = CultureOrDefault("ko-KR"),
        ["ru"] = CultureOrDefault("ru-RU"),
    };

    // 리눅스 SPT 처럼 DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 인 환경에서는 ko-KR 같은 문화권을 만들 때 예외가 난다.
    // 정적 필드 초기화에서 예외가 나면 이 클래스를 쓰는 브리핑 요청이 전부 500 이 되므로 그때는 기본 정렬로 넘어간다.
    internal static StringComparer CultureOrDefault(string name)
    {
        try
        {
            return StringComparer.Create(CultureInfo.GetCultureInfo(name), ignoreCase: false);
        }
        catch (CultureNotFoundException)
        {
            return NameOrder;
        }
    }

    // 웹의 localeCompare 는 브라우저 언어를 따른다. 한국어 사용자는 한글 이름이 영문보다 먼저 나오므로
    // 요청 언어에 맞는 문화권으로 정렬한다. kr, ru 말고는 기본 정렬.
    public static StringComparer NameOrderFor(string lang) => NameOrders.GetValueOrDefault(lang ?? "", NameOrder);

    private static readonly QuestProgress Locked = new("Locked", null, null, [], new(StringComparer.Ordinal));

    // 프로필에 없는 퀘스트는 잠김으로 본다.
    public static QuestProgress QuestProgressOf(ProfileProgress p, string questId)
        => p.Quests.TryGetValue(questId, out var qp) ? qp : Locked;

    public static double Remaining(Objective o, ObjectiveProgress? op)
    {
        if (op?.Done == true) return 0;
        var target = op?.Target ?? o.Prep?.Item?.Count ?? o.TargetCount ?? 0;
        return Math.Max(0, target - (op?.Current ?? 0));
    }

    // 대체 아이템 목록은 tpl 묶음 전체를 하나로 센다.
    public static string ItemKey(PrepItem item)
        => string.Join('|', item.Items.Select(i => i.Tpl).Distinct().Order(StringComparer.Ordinal));

    public static ItemHolding HoldingOf(string key, IReadOnlyDictionary<string, ItemHolding> inventory)
    {
        int count = 0, fir = 0;
        foreach (var tpl in key.Split('|'))
        {
            if (!inventory.TryGetValue(tpl, out var h)) continue;
            count += h.Count;
            fir += h.Fir;
        }
        return new ItemHolding(count, fir);
    }

    // 브리핑에서는 진행 중 퀘스트만 보므로 accept 를 항상 넘긴다.
    public static List<ItemNeed> ItemNeeds(CatalogModel catalog, ProfileProgress progress, Func<QuestProgress, bool> accept)
    {
        var list = new List<ItemNeed>();
        foreach (var quest in catalog.Quests.Values)
        {
            var qp = QuestProgressOf(progress, quest.Id);
            if (!accept(qp)) continue;
            foreach (var o in quest.Objectives)
            {
                if (o.Prep?.Item is null) continue;
                var count = Remaining(o, qp.Objectives.GetValueOrDefault(o.ConditionId));
                if (count > 0) list.Add(new ItemNeed(quest, o, count));
            }
        }
        return list;
    }

    // 같은 아이템의 남은 수를 먼저 다 더하고 나서 보유 수와 비교한다. 줄 순서는 처음 나온 순서 그대로.
    public static List<NeedRow> AggregateNeeds(IEnumerable<ItemNeed> needs, IReadOnlyDictionary<string, ItemHolding> inventory)
    {
        var byKey = new Dictionary<string, NeedRow>(StringComparer.Ordinal);
        var rows = new List<NeedRow>();
        foreach (var need in needs)
        {
            var item = need.Item;
            var key = ItemKey(item);
            if (!byKey.TryGetValue(key, out var row))
            {
                var h = HoldingOf(key, inventory);
                row = new NeedRow { Key = key, Items = item.Items, Have = h.Count, HaveFir = h.Fir };
                byKey[key] = row;
                rows.Add(row);
            }
            row.Need += need.Count;
            if (item.FoundInRaid) row.NeedFir += need.Count;
            row.Sources.Add(new NeedSource(need.Quest.Id, need.Objective.ConditionId, item.Action, need.Count, item.FoundInRaid));
        }
        return rows;
    }

    // FIR 이 필요한 몫은 FIR 보유로만 채우고, 나머지는 남은 보유(FIR 포함)로 채운다.
    public static double Missing(double need, double needFir, double have, double haveFir)
    {
        var firUsed = Math.Min(haveFir, needFir);
        var firShort = needFir - firUsed;
        var anyShort = Math.Max(0, (need - needFir) - (have - firUsed));
        return firShort + anyShort;
    }

    public static double Missing(NeedRow r) => Missing(r.Need, r.NeedFir, r.Have, r.HaveFir);

    public static string ItemLabel(NeedRow r) => r.Items.Count > 0 ? r.Items[0].Name : "";

    // 부족한 수가 많은 순, 같으면 필요한 수가 많은 순, 그다음 이름순
    public static List<NeedRow> SortNeedRows(IEnumerable<NeedRow> rows, StringComparer? names = null)
        => rows.OrderByDescending(Missing).ThenByDescending(r => r.Need).ThenBy(ItemLabel, names ?? NameOrder).ToList();
}
