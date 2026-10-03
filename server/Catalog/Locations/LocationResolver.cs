using QuestCodex.Catalog.Models;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Utils.Json;
using PointTable = System.Collections.Generic.IReadOnlyDictionary<string, System.Collections.Generic.IReadOnlyDictionary<string, System.Collections.Generic.IReadOnlyList<QuestCodex.Catalog.Models.MapPoint>>>;
using AreaTable = System.Collections.Generic.IReadOnlyDictionary<string, System.Collections.Generic.IReadOnlyDictionary<string, System.Collections.Generic.IReadOnlyList<QuestCodex.Catalog.Models.MapArea>>>;

namespace QuestCodex.Catalog.Locations;

/// <summary>
/// AvailableForFinish 조건 하나 → 맵별 좌표 목록(스펙 §3.2). 존 ID 는 존 표에서, 퀘스트 아이템은 looseLoot 강제 스폰
/// 표에서 찾는다. 같은 ID 가 여러 맵에 있으면 ID 마다 규칙 1·2 로 맵을 고른다:
///   규칙 1 — 퀘스트 맵(또는 그 짝)에서 찾았으면 그 맵들의 좌표만 쓴다(BSG 가비지 복사본 제거).
///   규칙 2 — 퀘스트 맵이 없거나(any, marathon) 거기서 못 찾았으면 발견된 모든 맵을 쓴다.
/// </summary>
public sealed class LocationResolver
{
    /// <summary>한 맵 정의를 공유하는 짝 맵. 서버는 웹의 맵 정의를 읽지 않으므로 고정 표로 둔다.</summary>
    public static readonly IReadOnlyDictionary<string, string> PairedMaps = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["factory4_day"] = "factory4_night",
        ["factory4_night"] = "factory4_day",
        ["sandbox"] = "sandbox_high",
        ["sandbox_high"] = "sandbox",
    };

    private readonly Dictionary<string, List<(string Map, IReadOnlyList<MapPoint> Points)>> _zones;
    private readonly Dictionary<string, List<(string Map, IReadOnlyList<MapPoint> Points)>> _itemSpawns;
    private readonly AreaTable _areas;
    private readonly IReadOnlyDictionary<string, string> _locationKeys;
    private readonly IReadOnlyDictionary<MongoId, TemplateItem> _items;

    /// <param name="zones">map → zoneId → 점. map 은 소문자 키, zoneId 는 trim 된 값이어야 한다.</param>
    /// <param name="questItemSpawns">map → 아이템 tpl → looseLoot 강제 스폰 점.</param>
    /// <param name="locationKeys">로케이션 _Id(MongoId) → map 키. quest.location 해석용.</param>
    /// <param name="areas">map → zoneId → 영역. InZone·LaunchFlare 조건에만 붙인다(08 스펙). null 이면 영역 없음.</param>
    public LocationResolver(
        PointTable zones,
        PointTable questItemSpawns,
        IReadOnlyDictionary<string, string> locationKeys,
        IReadOnlyDictionary<MongoId, TemplateItem> items,
        AreaTable? areas = null)
    {
        _zones = Invert(zones);
        _itemSpawns = Invert(questItemSpawns);
        _areas = areas ?? new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<MapArea>>>();
        _locationKeys = locationKeys;
        _items = items;
    }

    /// <summary>quest.location → 맵 키. ID 면 로케이션 표, 이름 문자열이면 소문자. any·marathon·빈 값은 null.</summary>
    public string? QuestMap(string? location)
    {
        if (string.IsNullOrWhiteSpace(location)) return null;
        if (location.Equals("any", StringComparison.OrdinalIgnoreCase) || location.Equals("marathon", StringComparison.OrdinalIgnoreCase)) return null;
        return _locationKeys.TryGetValue(location, out var key) ? key : location.ToLowerInvariant();
    }

    public IReadOnlyList<ObjectiveLocation> Resolve(QuestCondition c, string? questMap, List<CatalogWarning> warnings, string questId)
    {
        // 맵 순서: 퀘스트 맵, 그 짝, 나머지는 이름순. 점 순서: 조건에 나온 ID 순서.
        var byMap = new Dictionary<string, List<MapPoint>>(StringComparer.Ordinal);
        var areasByMap = new Dictionary<string, List<MapArea>>(StringComparer.Ordinal);

        foreach (var (zoneId, kind) in ZoneIdsOf(c))
        {
            if (_zones.TryGetValue(zoneId, out var hits))
            {
                var picked = Pick(hits, questMap).ToList();
                Add(byMap, picked);
                // 영역은 점과 같은 맵(규칙 1·2 결과)에서만 가져온다 — Reserve 가비지 복사본 같은 것이 영역으로 새지 않게
                if (kind != AreaKind.None) AddAreas(areasByMap, zoneId, picked.Select(h => h.Map), kind);
            }
            else warnings.Add(new CatalogWarning(questId, WarningCodes.QuestZoneNotFound, $"zone '{zoneId}' ({c.ConditionType}, condition {c.Id})"));
        }

        foreach (var tpl in QuestItemsOf(c))
        {
            if (_itemSpawns.TryGetValue(tpl, out var hits)) Add(byMap, Pick(hits, questMap));
        }

        if (byMap.Count == 0) return Array.Empty<ObjectiveLocation>();

        var pair = questMap is null ? null : PairedMaps.GetValueOrDefault(questMap);
        return byMap
            .OrderBy(kv => kv.Key == questMap ? 0 : kv.Key == pair ? 1 : 2)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new ObjectiveLocation(kv.Key, kv.Value)
            {
                Areas = areasByMap.TryGetValue(kv.Key, out var areas) ? areas : Array.Empty<MapArea>(),
            })
            .ToList();
    }

    private static IEnumerable<(string Map, IReadOnlyList<MapPoint> Points)> Pick(
        List<(string Map, IReadOnlyList<MapPoint> Points)> hits, string? questMap)
    {
        if (questMap is null) return hits;
        var pair = PairedMaps.GetValueOrDefault(questMap);
        var onQuestMap = hits.Where(h => h.Map == questMap || h.Map == pair).ToList();
        return onQuestMap.Count > 0 ? onQuestMap : hits;
    }

    private static void Add(Dictionary<string, List<MapPoint>> byMap, IEnumerable<(string Map, IReadOnlyList<MapPoint> Points)> hits)
    {
        foreach (var (map, points) in hits)
        {
            if (!byMap.TryGetValue(map, out var list)) byMap[map] = list = [];
            foreach (var p in points)
            {
                if (!list.Contains(p)) list.Add(p);
            }
        }
    }

    /// <summary>영역으로 그리는 방식. Volume = 구역 처치(높이 범위 그대로), Ground = 신호탄(바닥 높이 한 점).</summary>
    private enum AreaKind { None, Ground, Volume }

    private void AddAreas(Dictionary<string, List<MapArea>> areasByMap, string zoneId, IEnumerable<string> maps, AreaKind kind)
    {
        foreach (var map in maps)
        {
            if (!_areas.TryGetValue(map, out var ids) || !ids.TryGetValue(zoneId, out var areas)) continue;
            if (!areasByMap.TryGetValue(map, out var list)) areasByMap[map] = list = [];
            foreach (var raw in areas)
            {
                // 신호탄 감지 상자는 땅에서 위로 솟아 있다 — 바닥 + 1m 한 점으로 줄여 지상 발사 지점의 층에 보이게 한다
                var a = kind == AreaKind.Ground ? raw with { MinY = Math.Min(raw.MinY + 1, raw.MaxY), MaxY = Math.Min(raw.MinY + 1, raw.MaxY) } : raw;
                if (!list.Contains(a)) list.Add(a);
            }
        }
    }

    /// <summary>
    /// 존 ID 와 영역 방식(InZone = Volume, LaunchFlare = Ground, 나머지 None). 존 ID 는 앞뒤 공백을 제거한다(Keeper's Word 의
    /// "…_place_03 " 사례). 같은 ID 가 여러 조건에 나오면 Volume > Ground > None 순으로 남긴다.
    /// </summary>
    private static IEnumerable<(string Id, AreaKind Kind)> ZoneIdsOf(QuestCondition c)
    {
        var ids = new List<(string Id, AreaKind Kind)>();
        switch (c.ConditionType)
        {
            case "PlaceBeacon":
            case "LeaveItemAtLocation":
                ids.Add((c.ZoneId ?? "", AreaKind.None));
                break;
            // 바닐라는 CounterCreator 안에만 두지만, 모드는 최상위에도 쓴다(Icebreaker "배에 선을 대다" 차단기 수리 fix_element_*).
            case "VisitPlace":
                ids.AddRange(Targets(c.Target).Select(t => (t, AreaKind.None)));
                break;
            case "CounterCreator":
                foreach (var sub in c.Counter?.Conditions ?? [])
                {
                    switch (sub.ConditionType)
                    {
                        case "VisitPlace":
                            ids.AddRange(Targets(sub.Target).Select(t => (t, AreaKind.None)));
                            break;
                        case "LaunchFlare":
                            ids.AddRange(Targets(sub.Target).Select(t => (t, AreaKind.Ground)));
                            break;
                        case "InZone":
                            ids.AddRange((sub.Zones ?? []).Select(z => (z, AreaKind.Volume)));
                            break;
                    }
                }

                break;
        }

        return ids
            .Select(x => (Id: x.Id.Trim(), x.Kind))
            .Where(x => x.Id.Length > 0)
            .GroupBy(x => x.Id, StringComparer.Ordinal)
            .Select(g => (g.Key, g.Max(x => x.Kind)));
    }

    private IEnumerable<string> QuestItemsOf(QuestCondition c)
    {
        if (c.ConditionType != "FindItem") return [];
        return Targets(c.Target).Where(tpl =>
            MongoId.IsValidMongoId(tpl)
            && _items.TryGetValue(new MongoId(tpl), out var template)
            && template.Properties?.QuestItem == true);
    }

    private static IEnumerable<string> Targets(ListOrT<string>? target)
    {
        if (target is null) return [];
        if (target.IsItem) return string.IsNullOrWhiteSpace(target.Item) ? [] : [target.Item];
        return target.List?.Where(t => !string.IsNullOrWhiteSpace(t)) ?? [];
    }

    /// <summary>map → id → 점 을 id → (map, 점) 목록으로 뒤집는다. 조회가 ID 기준이기 때문이다.</summary>
    private static Dictionary<string, List<(string Map, IReadOnlyList<MapPoint> Points)>> Invert(PointTable table)
    {
        var inverted = new Dictionary<string, List<(string, IReadOnlyList<MapPoint>)>>(StringComparer.Ordinal);
        foreach (var (map, ids) in table)
        {
            foreach (var (id, points) in ids)
            {
                if (!inverted.TryGetValue(id, out var list)) inverted[id] = list = [];
                list.Add((map, points));
            }
        }

        return inverted;
    }
}
