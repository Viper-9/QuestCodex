using QuestCodex.Briefing.Models;
using QuestCodex.Catalog;
using QuestCodex.Catalog.Models;
using QuestCodex.Progress.Models;
using CatalogModel = QuestCodex.Catalog.Models.Catalog;

namespace QuestCodex.Briefing;

// 요청 언어의 카탈로그, 프로필 진행 상태, 지도 index 로 브리핑 응답을 만든다. 상태도 캐시도 없다.
// 계산 순서는 웹 레이드 준비 화면(RaidView)과 같다. MapTabs 로 탭을 만들고 맵마다 MapBrief, RaidMapPlan, placeFinds 를 돌린다.
// 웹과 다른 건 장비·특수 조건을 "이 맵" 과 "아무 맵에서나" 로 나눠 담는 것 하나다.
// 아무 맵 쪽은 응답에 한 번만 넣고 클라이언트가 탭마다 합쳐서 보여 준다.
public static class BriefingBuilder
{
    public const int SchemaVersion = 1;

    public static BriefingResponse Build(CatalogModel catalog, ProfileProgress progress, MapIndex mapIndex, string questCodexVersion, DateTimeOffset generatedAt)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(progress);

        var lang = catalog.Lang;
        var names = RaidNeeds.NameOrderFor(lang);
        var index = mapIndex.WithVariants(catalog.MapVariants);
        var inventory = progress.Inventory;
        var entries = RaidPlan.RaidEntries(catalog, progress);
        var finds = RaidFinds.Rows(catalog, progress, inventory, names);
        var serverMaps = catalog.Exits.Keys.Concat(catalog.LockedDoors.Keys).ToList();
        var flags = new QuestFlags(
            QuestChains.PrerequisitesOf(catalog, CatalogBuilder.CollectorId), QuestChains.PrerequisitesOf(catalog, QuestChains.InformationSourceId));

        var maps = RaidPlan.MapTabs(entries, catalog.Exits.Keys).Select(tab =>
        {
            var brief = RaidPlan.MapBrief(entries, tab.Key, inventory);
            var plan = RaidPlan.RaidMapPlan(brief.Here, tab.Key, names);
            var folder = index.FolderFor(tab.Key);
            return new BriefingMap(
                tab.Key,
                RaidPlan.LocationIds(tab.Key, serverMaps),
                BriefingText.MapName(tab.Key, catalog.MapVariants, lang),
                tab.Count,
                folder is not null,
                folder,
                RaidPlan.OrderRaidQuests(brief.Here, tab.Key, names).Select(g => Quest(g, tab.Key, plan.Numbers, lang, flags)).ToList(),
                brief.Bring.Select(ItemRow).ToList(),
                RaidFinds.Place(finds, catalog, tab.Key).Select(FindRow).ToList(),
                Gear(brief.Gear.Where(e => e.Maps.Count > 0)),
                Special(brief.Special.Where(e => e.Maps.Count > 0)),
                ForTab(catalog.Exits, tab.Key, (x, y) => x.Key == y.Key && x.Kind == y.Kind && x.Position == y.Position),
                ForTab(catalog.LockedDoors, tab.Key, (x, y) => x.KeyTpl == y.KeyTpl && x.Position == y.Position));
        }).ToList();

        // 아무 맵 목록은 탭과 상관없이 한 번만 보내니 맵 키를 "" 로 넘긴다.
        // 그러면 지도 좌표로 앞에 오는 퀘스트가 없고 진행률, 이름 순으로만 정렬된다. 웹의 모든 맵 카드도 이렇게 보인다.
        var anywhere = entries.Where(e => e.Maps.Count == 0).ToList();
        var traders = new SortedDictionary<string, BriefingTrader>(StringComparer.Ordinal);
        foreach (var (id, t) in catalog.Traders) traders[id] = new BriefingTrader(t.Name);

        return new BriefingResponse(
            SchemaVersion, questCodexVersion, generatedAt, lang, traders, maps,
            new BriefingAnywhere(
                RaidPlan.OrderRaidQuests(anywhere, "", names).Select(g => Quest(g, null, null, lang, flags)).ToList(),
                RaidNeeds.AggregateNeeds(RaidPlan.PlantNeeds(anywhere), inventory).Select(ItemRow).ToList(),
                Gear(anywhere.Where(e => e.Objective.Prep is { } p && RaidPlan.HasGear(p))),
                Special(anywhere.Where(e => e.Objective.Prep is { } p && RaidPlan.HasSpecial(p)))),
            flags.Kappa.Order(StringComparer.Ordinal).ToList(),
            flags.Lightkeeper.Order(StringComparer.Ordinal).ToList());
    }

    // map 이 null 이면 아무 맵 목록용이라 지도 번호를 붙이지 않고 좌표도 손대지 않는다.
    private static BriefingQuest Quest(QuestGroup g, string? map, IReadOnlyDictionary<string, int>? numbers, string lang, QuestFlags flags)
    {
        var line = QuestLines.Of(g.Entries, lang);
        int? marker = numbers is not null && numbers.TryGetValue(g.Quest.Id, out var n) ? n : null;
        var objectives = g.Entries.Select(e => new BriefingObjective(
            e.Objective.ConditionId, BriefingText.ObjectiveText(e.Objective), e.Progress?.Current ?? 0, e.Progress?.Target,
            e.Inferred, map is null ? e.Objective.Locations : LocationsFor(e.Objective, map))).ToList();
        return new BriefingQuest(g.Quest.Id, g.Quest.TraderId, g.Quest.Name, marker, line.Summary, line.Current, line.Target, objectives,
            flags.Kappa.Contains(g.Quest.Id), flags.Lightkeeper.Contains(g.Quest.Id));
    }

    // Build 에서 한 번만 계산해 퀘스트마다 넘기는 선행 퀘스트 목록.
    private sealed record QuestFlags(IReadOnlySet<string> Kappa, IReadOnlySet<string> Lightkeeper);

    // 이 탭에 속한 위치만 골라 하나로 합친다. 공장 주간·야간처럼 짝이 있는 맵은 같은 존이 같은 좌표로 양쪽에 들어 있어서
    // 그냥 두면 마커가 두 번 찍힌다. 웹 mapProjection 의 buildNumberedTabs 도 같은 이유로 합친다.
    private static IReadOnlyList<ObjectiveLocation> LocationsFor(Objective o, string map)
    {
        var points = new List<MapPoint>();
        var areas = new List<MapArea>();
        foreach (var l in o.Locations)
        {
            if (RaidPlan.MapGroup(l.Map) != map) continue;
            foreach (var p in l.Points) if (!points.Contains(p)) points.Add(p);
            foreach (var a in l.Areas) if (!areas.Contains(a)) areas.Add(a);
        }
        return points.Count == 0 && areas.Count == 0 ? [] : [new ObjectiveLocation(map, points) { Areas = areas }];
    }

    private static BriefingItemRow ItemRow(NeedRow r)
    {
        var fir = r.NeedFir > 0;
        return new BriefingItemRow(r.Items, r.Need, fir ? r.HaveFir : r.Have, fir, RaidNeeds.Missing(r), Sources(r));
    }

    private static BriefingFindRow FindRow(PlacedRow p)
    {
        var row = ItemRow(p.Row);
        return new BriefingFindRow(row.Items, row.Need, row.Have, row.Fir, row.Missing, row.Sources, p.Place);
    }

    // 출처는 퀘스트별로 수만 합쳐서 처음 나온 순서로 보낸다. FIR 여부는 줄의 fir 값으로 이미 알 수 있다.
    private static IReadOnlyList<BriefingSource> Sources(NeedRow r)
    {
        var merged = new List<BriefingSource>();
        foreach (var s in r.Sources)
        {
            var i = merged.FindIndex(m => m.QuestId == s.QuestId);
            if (i < 0) merged.Add(new BriefingSource(s.QuestId, s.Count));
            else merged[i] = merged[i] with { Count = merged[i].Count + s.Count };
        }
        return merged;
    }

    private static List<BriefingGear> Gear(IEnumerable<RaidEntry> entries)
        => RaidPlan.GroupByQuest(entries)
            .Select(g => new BriefingGear(g.Quest.Id, RaidRules.DedupeGear(g.Entries.SelectMany(e => RaidRules.GearRows(e.Objective.Prep!)))))
            .ToList();

    private static List<BriefingSpecial> Special(IEnumerable<RaidEntry> entries)
        => RaidPlan.GroupByQuest(entries)
            .Select(g => new BriefingSpecial(g.Quest.Id, RaidRules.DedupeSpecial(g.Entries.SelectMany(e => RaidRules.SpecialRows(e.Objective.Prep!)))))
            .ToList();

    // 탭에 묶이는 서버 맵(짝 맵 포함)의 탈출구나 잠긴 문을 모은다.
    // 짝 맵에서는 같은 것이 같은 좌표로 두 번 들어오니 한 번만 넣는다. 웹 exitsForTab, doorsForTab 과 같다.
    private static List<T> ForTab<T>(SortedDictionary<string, List<T>> byMap, string tab, Func<T, T, bool> same)
    {
        var list = new List<T>();
        foreach (var (map, items) in byMap)
        {
            if (RaidPlan.MapGroup(map) != tab) continue;
            foreach (var x in items) if (!list.Any(y => same(x, y))) list.Add(x);
        }
        return list;
    }
}
