using System.Text.Json;
using QuestCodex.Catalog.Locations;
using QuestCodex.Catalog.Models;
using SPTarkov.DI.Annotations;
using PointTable = System.Collections.Generic.IReadOnlyDictionary<string, System.Collections.Generic.IReadOnlyDictionary<string, System.Collections.Generic.IReadOnlyList<QuestCodex.Catalog.Models.MapPoint>>>;
using AreaTable = System.Collections.Generic.IReadOnlyDictionary<string, System.Collections.Generic.IReadOnlyDictionary<string, System.Collections.Generic.IReadOnlyList<QuestCodex.Catalog.Models.MapArea>>>;

namespace QuestCodex.Services;

/// <summary>
/// 모드에 동봉한 바닐라 퀘스트 존 스냅샷(Data/quest-zones.json). 존 트리거는 클라이언트 맵 씬에만 있어서
/// 개발자가 덤프 플러그인(tools/zone-dump)으로 맵마다 한 번 뽑아 둔 것이다. 없으면 Zones == null 이고
/// 카탈로그가 경고를 낸다(기동 실패 아님). VanillaSnapshot 과 같은 처리다.
/// 맵 교체 모드가 로드돼 있으면(MapVariantDetector) 그 맵의 존·영역·문을 스냅샷 variants 의 것으로 통째로 바꾼다.
/// </summary>
[Injectable(InjectionType.Singleton)]
public class QuestZoneSnapshot(MapVariantDetector mapVariants)
{
    private readonly Lazy<Snapshot?> _data = new(() => LoadFromModFolder()?.WithVariants(mapVariants.Active));

    public string? CollectedWith => _data.Value?.CollectedWith;
    public PointTable? Zones => _data.Value?.Zones;
    /// <summary>map → 잠긴 문(Door·KeycardDoor). 스냅샷이 없으면 null, doors 절이 없는 구버전 스냅샷이면 빈 사전.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<SnapshotDoor>>? Doors => _data.Value?.Doors;
    /// <summary>map → zoneId → 영역(덤프 Bounds 의 x·z 크기). 크기가 없는 존은 빠진다.</summary>
    public AreaTable? Areas => _data.Value?.Areas;
    /// <summary>map → 탈출구·환승 좌표(tarkov.dev, 10 스펙). 스냅샷이 없으면 null, exits 절이 없는 구버전이면 빈 사전.</summary>
    public IReadOnlyDictionary<string, SnapshotExits>? Exits => _data.Value?.Exits;

    public sealed record Snapshot(string CollectedWith, PointTable Zones, IReadOnlyDictionary<string, IReadOnlyList<SnapshotDoor>> Doors)
    {
        /// <summary>위치 인수가 아니라 init 속성 — 기존 3-분해(var (_, zones, doors) = …) 호출을 깨지 않으려고.</summary>
        public AreaTable Areas { get; init; } = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<MapArea>>>();

        /// <summary>변형 ID → 그 변형이 바꾸는 맵들만 담은 스냅샷(collectedWith 는 부모 것). 변형 자체의 Variants 는 비어 있다.</summary>
        public IReadOnlyDictionary<string, Snapshot> Variants { get; init; } = new Dictionary<string, Snapshot>();

        public IReadOnlyDictionary<string, SnapshotExits> Exits { get; init; } = new Dictionary<string, SnapshotExits>();

        /// <summary>
        /// 활성 변형(map → 변형 ID)마다 그 맵의 존·영역·문을 변형 것으로 바꾼다. 섞지 않고 통째로 — 확장 씬에서 옮겨진 존이
        /// 바닐라 좌표로 남지 않게, 변형에 없는 존은 위치 없음이 된다. 스냅샷에 없는 변형·맵은 바닐라 그대로 둔다.
        /// </summary>
        public Snapshot WithVariants(IReadOnlyDictionary<string, string> active)
        {
            var swaps = active
                .Where(a => Variants.TryGetValue(a.Value, out var v) && v.Zones.ContainsKey(a.Key))
                .Select(a => (Map: a.Key, Data: Variants[a.Value]))
                .ToList();
            if (swaps.Count == 0) return this;

            var zones = Zones.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
            var areas = Areas.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
            var doors = Doors.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
            var exits = Exits.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
            foreach (var (map, data) in swaps)
            {
                zones[map] = data.Zones[map];
                if (data.Areas.TryGetValue(map, out var a)) areas[map] = a; else areas.Remove(map);
                if (data.Doors.TryGetValue(map, out var d)) doors[map] = d; else doors.Remove(map);
                if (data.Exits.TryGetValue(map, out var e)) exits[map] = e; else exits.Remove(map);
            }

            return new Snapshot(CollectedWith, zones, doors) { Areas = areas, Variants = Variants, Exits = exits };
        }
    }

    public static Snapshot Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var collectedWith = doc.RootElement.TryGetProperty("collectedWith", out var c) ? c.GetString() ?? "" : "";
        var variants = new Dictionary<string, Snapshot>(StringComparer.Ordinal);
        if (doc.RootElement.TryGetProperty("variants", out var variantsJson))
        {
            foreach (var v in variantsJson.EnumerateObject()) variants[v.Name] = ParseBody(v.Value, collectedWith);
        }

        return ParseBody(doc.RootElement, collectedWith) with { Variants = variants };
    }

    /// <summary>zones·doors 절 하나(최상위 또는 variants.&lt;변형&gt;)를 읽는다.</summary>
    private static Snapshot ParseBody(JsonElement root, string collectedWith)
    {
        var builder = new PointTableBuilder();
        var areas = new AreaTableBuilder();
        foreach (var map in root.GetProperty("zones").EnumerateObject())
        {
            foreach (var zone in map.Value.EnumerateObject())
            {
                foreach (var p in zone.Value.EnumerateArray())
                {
                    var point = new MapPoint(p.GetProperty("x").GetDouble(), p.GetProperty("y").GetDouble(), p.GetProperty("z").GetDouble());
                    builder.Add(map.Name, zone.Name, point);
                    // 영역: 콜라이더 상자 목록(boxes, 덤프 0.0.2+ — 실제 중심·크기·회전) 또는 예전 형식(점에 바로 붙은 sx/sz 외곽 사각형)
                    if (p.TryGetProperty("boxes", out var boxes))
                    {
                        foreach (var box in boxes.EnumerateArray())
                        {
                            if (AreaOf(box, point) is { } a) areas.Add(map.Name, zone.Name, a);
                        }
                    }
                    else if (AreaOf(p, point) is { } a)
                    {
                        areas.Add(map.Name, zone.Name, a);
                    }
                }
            }
        }

        var doors = new Dictionary<string, IReadOnlyList<SnapshotDoor>>(StringComparer.Ordinal);
        if (root.TryGetProperty("doors", out var doorMaps))
        {
            foreach (var map in doorMaps.EnumerateObject())
            {
                doors[map.Name.ToLowerInvariant()] = map.Value.EnumerateArray()
                    .Select(d => new SnapshotDoor(
                        d.GetProperty("key").GetString() ?? throw new InvalidOperationException("door key is null"),
                        d.GetProperty("type").GetString() ?? throw new InvalidOperationException("door type is null"),
                        new MapPoint(d.GetProperty("x").GetDouble(), d.GetProperty("y").GetDouble(), d.GetProperty("z").GetDouble())))
                    .ToList();
            }
        }

        var exits = new Dictionary<string, SnapshotExits>(StringComparer.Ordinal);
        if (root.TryGetProperty("exits", out var exitMaps))
        {
            foreach (var map in exitMaps.EnumerateObject())
            {
                var list = map.Value.TryGetProperty("exits", out var e) ? e.EnumerateArray()
                    .Select(x => new SnapshotExit(
                        x.GetProperty("key").GetString() ?? throw new InvalidOperationException("exit key is null"),
                        x.GetProperty("name").GetString() ?? "",
                        PointOf(x)))
                    .ToList() : [];
                var transits = map.Value.TryGetProperty("transits", out var t) ? t.EnumerateArray()
                    .Select(x => new SnapshotTransit(x.GetProperty("id").GetString() ?? throw new InvalidOperationException("transit id is null"), PointOf(x)))
                    .ToList() : [];
                exits[map.Name.ToLowerInvariant()] = new SnapshotExits(list, transits);
            }
        }

        return new Snapshot(collectedWith, builder.Build(), doors) { Areas = areas.Build(), Exits = exits };
    }

    private static MapPoint PointOf(JsonElement e) => new(e.GetProperty("x").GetDouble(), e.GetProperty("y").GetDouble(), e.GetProperty("z").GetDouble());

    /// <summary>
    /// sx·sz 가 있으면 영역 하나. 중심은 cx·cz(없으면 존 위치), 높이 Center.Y 는 존 위치의 y, 회전 r(없으면 0),
    /// 높이 범위 y0·y1(없으면 위치 높이 한 점).
    /// </summary>
    private static MapArea? AreaOf(JsonElement e, MapPoint point)
    {
        if (!e.TryGetProperty("sx", out var sx) || !e.TryGetProperty("sz", out var sz)) return null;
        double Or(string name, double fallback) => e.TryGetProperty(name, out var v) ? v.GetDouble() : fallback;
        return new MapArea(
            new MapPoint(Or("cx", point.X), point.Y, Or("cz", point.Z)),
            sx.GetDouble(), sz.GetDouble(), Or("r", 0), Or("y0", point.Y), Or("y1", point.Y));
    }

    public static Snapshot? LoadFromModFolder()
    {
        var modDir = Path.GetDirectoryName(typeof(QuestZoneSnapshot).Assembly.Location);
        if (modDir is null) return null;
        var path = Path.Combine(modDir, "Data", "quest-zones.json");
        return File.Exists(path) ? Parse(File.ReadAllText(path)) : null;
    }
}
