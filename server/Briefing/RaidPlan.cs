using QuestCodex.Catalog.Models;
using QuestCodex.Progress.Models;
using CatalogModel = QuestCodex.Catalog.Models.Catalog;

namespace QuestCodex.Briefing;

// 웹 web/src/progress/derive.ts 의 레이드 준비 계산을 옮긴 것. 함수 이름도 웹과 맞췄다.
// 웹 레이드 준비 화면과 같은 결과가 나와야 한다.

// Maps 가 비어 있으면 아무 맵에서나 하거나 맵을 모르는 목표. Inferred 는 맵을 목표 문장에서 짐작했다는 뜻.
public sealed record RaidEntry(CatalogQuest Quest, Objective Objective, ObjectiveProgress? Progress, IReadOnlyList<string> Maps, bool Inferred);

public sealed record QuestGroup(CatalogQuest Quest, List<RaidEntry> Entries);

// Count 는 이 맵에서만 할 수 있는 목표 수. 아무 맵에서나 되는 목표는 넣지 않는다.
public sealed record MapTab(string Key, int Count);

public sealed record MapPlan(
    IReadOnlyDictionary<string, int> Numbers, IReadOnlyList<(int N, CatalogQuest Quest)> Quests, IReadOnlyList<(int N, Objective Objective)> Items);

public sealed record MapPrep(
    IReadOnlyList<RaidEntry> Here, IReadOnlyList<RaidEntry> Anywhere, IReadOnlyList<NeedRow> Bring, IReadOnlyList<NeedRow> BringAnywhere,
    IReadOnlyList<RaidEntry> Gear, IReadOnlyList<RaidEntry> Special);

public static class RaidPlan
{
    // 맵 탭 순서
    public static readonly IReadOnlyList<string> MapOrder =
    [
        "bigmap", "factory4_day", "woods", "shoreline", "interchange", "rezervbase",
        "lighthouse", "tarkovstreets", "sandbox", "laboratory", "labyrinth",
    ];

    // 같은 맵의 변종(야간 공장, 고레벨 그라운드 제로 등)은 한 탭으로 묶는다.
    private static readonly Dictionary<string, string> Variants = new(StringComparer.Ordinal)
    {
        ["factory4_night"] = "factory4_day",
        ["sandbox_high"] = "sandbox",
        ["sandbox_start"] = "sandbox",
        ["laboratory_dark"] = "laboratory",
    };

    private static readonly HashSet<string> Ignored = new(StringComparer.Ordinal) { "develop" };

    public static string? MapGroup(string key)
    {
        var k = key.ToLowerInvariant();
        return Ignored.Contains(k) ? null : Variants.GetValueOrDefault(k, k);
    }

    // 탭 하나에 묶이는 로케이션 ID 목록(소문자). 클라이언트가 레이드 중 MainPlayer.Location 과 비교해서 현재 탭을 고른다.
    // 야간·고레벨 맵의 실제 로케이션 값은 아직 게임에서 확인하지 못해서, 변종 표와 서버에 있는 맵 키를 모두 넣어 둔다.
    public static IReadOnlyList<string> LocationIds(string tab, IEnumerable<string> serverMaps)
    {
        var ids = new SortedSet<string>(StringComparer.Ordinal) { tab };
        foreach (var (variant, group) in Variants) if (group == tab) ids.Add(variant);
        foreach (var m in serverMaps)
        {
            var k = m.ToLowerInvariant();
            if (MapGroup(k) == tab) ids.Add(k);
        }
        return ids.ToList();
    }

    // 레이드 안에서 하는 목표 종류. 제출·레벨 같은 목표는 브리핑에 넣지 않는다.
    private static readonly HashSet<string> RaidTypes = new(StringComparer.Ordinal)
        { "CounterCreator", "LeaveItemAtLocation", "PlaceBeacon", "VisitPlace" };

    // 찾기는 정해진 자리에 생기는 퀘스트 아이템(좌표가 있는 것)만 레이드 목표로 본다.
    // 어디서나 줍는 일반 아이템은 찾을 것 목록에만 둔다.
    public static bool IsRaidObjective(Objective o) => RaidTypes.Contains(o.ConditionType) || IsQuestItemFind(o);

    private static bool IsQuestItemFind(Objective o) => o.ConditionType == "FindItem" && o.Locations.Count > 0;

    // 목표 조건에 맵이 없으면 퀘스트에 묶인 맵을 쓴다. 퀘스트 아이템 찾기는 아이템이 생기는 맵을 쓴다.
    public static IReadOnlyList<string> ObjectiveMaps(CatalogQuest q, Objective o)
    {
        var keys = o.Prep?.MapKeys ?? [];
        IReadOnlyList<string> src = IsQuestItemFind(o) ? o.Locations.Select(l => l.Map).ToList()
            : keys.Count > 0 ? keys : q.LocationKey is { } l ? [l] : [];
        var list = new List<string>();
        foreach (var k in src)
        {
            if (MapGroup(k) is { } g && !list.Contains(g)) list.Add(g);
        }
        return list;
    }

    // 데이터에 맵이 없는 목표에만 쓴다.
    public static IReadOnlyList<string> MapsFromText(string text) => MapText.Find(text);

    // 진행 중인 퀘스트에서 아직 끝나지 않은 레이드 목표를 전부 모은다. 게임이 아직 숨기는 목표는 뺀다.
    public static List<RaidEntry> RaidEntries(CatalogModel catalog, ProfileProgress progress)
    {
        var list = new List<RaidEntry>();
        foreach (var quest in catalog.Quests.Values)
        {
            var qp = RaidNeeds.QuestProgressOf(progress, quest.Id);
            if (qp.Status != "Started") continue;
            foreach (var o in quest.Objectives)
            {
                if (!IsRaidObjective(o) || IsHidden(quest, o, qp)) continue;
                var op = qp.Objectives.GetValueOrDefault(o.ConditionId);
                if (op?.Done == true) continue;
                var known = ObjectiveMaps(quest, o);
                IReadOnlyList<string> guessed = known.Count == 0 ? MapsFromText(o.Text) : [];
                list.Add(new RaidEntry(quest, o, op, known.Count > 0 ? known : guessed, guessed.Count > 0));
            }
        }
        return list;
    }

    // 먼저 끝나야 하는 조건 중 하나라도 안 끝났으면 숨은 목표다. 이 퀘스트에 없는 조건을 가리키면 끝날 수 없으니 보이는 것으로 본다.
    public static bool IsHidden(CatalogQuest quest, Objective o, QuestProgress qp)
        => o.VisibleAfter.Any(id => quest.Objectives.Any(x => x.ConditionId == id) && qp.Objectives.GetValueOrDefault(id)?.Done != true);

    // 퀘스트별로 묶는다. 퀘스트는 처음 나온 순서, 퀘스트 안의 목표는 원래 순서 그대로.
    public static List<QuestGroup> GroupByQuest(IEnumerable<RaidEntry> entries)
    {
        var byId = new Dictionary<string, QuestGroup>(StringComparer.Ordinal);
        var groups = new List<QuestGroup>();
        foreach (var e in entries)
        {
            if (byId.TryGetValue(e.Quest.Id, out var g)) { g.Entries.Add(e); continue; }
            g = new QuestGroup(e.Quest, [e]);
            byId[e.Quest.Id] = g;
            groups.Add(g);
        }
        return groups;
    }

    // 이 목표에 해당 맵의 좌표가 있는지
    public static bool OnMap(RaidEntry e, string map) => e.Objective.Locations.Any(l => MapGroup(l.Map) == map);

    // 정렬용 진행률. target 이 2 이상인 목표만 본다(방문·설치처럼 1인 목표는 했다/안 했다라서). 없으면 null.
    private static double? CounterRatio(IEnumerable<RaidEntry> entries)
    {
        double current = 0, target = 0;
        foreach (var e in entries)
        {
            if (e.Progress is not { Target: { } t } p || t < 2) continue;
            current += Math.Min(p.Current, t);
            target += t;
        }
        return target > 0 ? current / target : null;
    }

    // 퀘스트 순서: 지도에 찍히는 퀘스트, 카운터가 있는 퀘스트, 나머지 순.
    // 앞의 두 묶음은 진행률이 높은 순이고, 같으면 이름순.
    public static List<QuestGroup> OrderRaidQuests(IEnumerable<RaidEntry> entries, string map, StringComparer? names = null)
        => GroupByQuest(entries)
            .Select(g =>
            {
                var ratio = CounterRatio(g.Entries);
                var rank = g.Entries.Any(e => OnMap(e, map)) ? 0 : ratio is not null ? 1 : 2;
                return (Group: g, Rank: rank, Ratio: ratio ?? -1);
            })
            .OrderBy(x => x.Rank).ThenByDescending(x => x.Ratio).ThenBy(x => x.Group.Quest.Name, names ?? RaidNeeds.NameOrder)
            .Select(x => x.Group)
            .ToList();

    // 지도 번호는 퀘스트마다 하나. 목록 순서대로 1부터 매기고, 이 맵 좌표가 없는 퀘스트는 건너뛴다.
    public static MapPlan RaidMapPlan(IEnumerable<RaidEntry> here, string map, StringComparer? names = null)
    {
        var numbers = new Dictionary<string, int>(StringComparer.Ordinal);
        var quests = new List<(int, CatalogQuest)>();
        var items = new List<(int, Objective)>();
        foreach (var g in OrderRaidQuests(here, map, names))
        {
            var placed = g.Entries.Where(e => OnMap(e, map)).ToList();
            if (placed.Count == 0) continue;
            var n = numbers.Count + 1;
            numbers[g.Quest.Id] = n;
            quests.Add((n, g.Quest));
            foreach (var e in placed) items.Add((n, e.Objective));
        }
        return new MapPlan(numbers, quests, items);
    }

    // 기본 맵은 목표가 없어도 항상 정해진 순서로 넣는다. 모드 맵은 목표가 있거나 서버에 있을 때만 뒤에 이름순으로 붙인다.
    public static List<MapTab> MapTabs(IEnumerable<RaidEntry> entries, IEnumerable<string>? serverMaps = null)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var e in entries) foreach (var m in e.Maps) counts[m] = counts.GetValueOrDefault(m) + 1;
        var keys = new HashSet<string>(counts.Keys, StringComparer.Ordinal);
        foreach (var k in serverMaps ?? []) if (MapGroup(k) is { } g) keys.Add(g);
        var extra = keys.Where(k => !MapOrder.Contains(k)).Order(StringComparer.Ordinal);
        return MapOrder.Concat(extra).Select(k => new MapTab(k, counts.GetValueOrDefault(k))).ToList();
    }

    // 챙길 것에는 설치 아이템만 넣는다. 제출 아이템은 상인에게 내는 것이라 레이드에 가져갈 물건이 아니다.
    public static List<ItemNeed> PlantNeeds(IEnumerable<RaidEntry> entries)
    {
        var list = new List<ItemNeed>();
        foreach (var e in entries)
        {
            if (e.Objective.Prep?.Item?.Action != "plant") continue;
            var count = RaidNeeds.Remaining(e.Objective, e.Progress);
            if (count > 0) list.Add(new ItemNeed(e.Quest, e.Objective, count));
        }
        return list;
    }

    public static bool HasGear(ObjectivePrep p)
        => p.Weapons.Count > 0 || p.Calibers.Count > 0 || p.WeaponMods.Count > 0 || p.Equipment.Count > 0 || p.ForbiddenEquipment.Count > 0;

    public static bool HasSpecial(ObjectivePrep p) => p.OneRaid || p.ExitStatuses.Count > 0 || p.ExitName is not null;

    // Gear·Special 은 웹처럼 이 맵 목표와 아무 맵 목표를 함께 담는다. 응답에 넣을 때 BriefingBuilder 가 다시 나눈다.
    public static MapPrep MapBrief(IReadOnlyList<RaidEntry> entries, string map, IReadOnlyDictionary<string, ItemHolding> inventory)
    {
        var here = entries.Where(e => e.Maps.Contains(map)).ToList();
        var anywhere = entries.Where(e => e.Maps.Count == 0).ToList();
        var all = here.Concat(anywhere).ToList();
        return new MapPrep(
            here, anywhere,
            RaidNeeds.AggregateNeeds(PlantNeeds(here), inventory),
            RaidNeeds.AggregateNeeds(PlantNeeds(anywhere), inventory),
            all.Where(e => e.Objective.Prep is { } p && HasGear(p)).ToList(),
            all.Where(e => e.Objective.Prep is { } p && HasSpecial(p)).ToList());
    }
}
