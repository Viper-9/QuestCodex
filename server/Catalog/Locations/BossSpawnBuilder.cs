using QuestCodex.Catalog.Models;

namespace QuestCodex.Catalog.Locations;

/// <summary>
/// 보스 출현(18 스펙). 값은 서버 표(base.BossLocationSpawn — 위치 모드가 바꾼 확률 포함)를 그대로 읽고, 바닐라 이름만 남긴다.
/// 모드 보스를 빼는 이유: ISB 처럼 레이드마다 다시 뽑아 표에 한 번의 추첨 결과만 남기는 모드가 있어 표를 믿을 수 없다.
/// 지점은 구역마다 하나로 묶는다 — 연구소 레이더처럼 행이 많아도 지도에는 구역 수만큼만 그려진다.
/// 다만 맵 크기에 비해 넓은 구역(연구소 지상·2층·지하)은 마커 하나로는 위치를 짐작할 수 없어 가까운 지점끼리 나눠 찍는다.
/// </summary>
public static class BossSpawnBuilder
{
    private const string Boss = "boss";
    private const string Minor = "minor";

    /// <summary>마커에서 가장 먼 지점이 맵 크기의 이 비율 이상이면 넓은 구역으로 보고 나눈다. 바닐라 기준 연구소 42~68%, 그 밖의 여러 구역 맵은 15% 이하.</summary>
    private const double WideZoneRatio = 0.4;

    /// <summary>넓은 구역을 나눌 때 한 마커로 묶는 거리(m).</summary>
    private const double ClusterRadius = 40;

    /// <summary>높이가 이만큼(m) 차이 나면 다른 층으로 보고 묶지 않는다.</summary>
    private const double SameFloorHeight = 3;

    /// <summary>
    /// 바닐라 이름 → 종류. minor = 레이더·로그처럼 무리로 나오는 준보스. PMC 는 넣지 않는다.
    /// 새 보스가 게임에 추가되면 여기와 웹 i18n(map.boss.name.*)에 한 줄씩 넣는다.
    /// </summary>
    private static readonly Dictionary<string, string> Vanilla = new(StringComparer.Ordinal)
    {
        ["bossBully"] = Boss, ["bossKilla"] = Boss, ["bossGluhar"] = Boss, ["bossKojaniy"] = Boss, ["bossSanitar"] = Boss,
        ["bossTagilla"] = Boss, ["bossTagillaAgro"] = Boss, ["bossKillaAgro"] = Boss, ["bossBoar"] = Boss, ["bossBoarSniper"] = Boss,
        ["bossKolontay"] = Boss, ["bossKnight"] = Boss, ["bossPartisan"] = Boss, ["bossZryachiy"] = Boss, ["sectantPriest"] = Boss,
        ["gifter"] = Boss,
        ["pmcBot"] = Minor, ["exUsec"] = Minor, ["peacemaker"] = Minor, ["arenaFighterEvent"] = Minor, ["crazyAssaultEvent"] = Minor,
        ["tagillaHelperAgro"] = Minor,
    };

    public static SortedDictionary<string, MapBossSpawns> Build(IReadOnlyDictionary<string, LocationBosses>? db)
    {
        var result = new SortedDictionary<string, MapBossSpawns>(StringComparer.Ordinal);
        foreach (var (map, location) in db ?? new Dictionary<string, LocationBosses>())
        {
            var spawns = BuildMap(location);
            if (spawns.Zones.Count > 0 || spawns.Unplaced.Count > 0) result[map] = spawns;
        }

        return result;
    }

    private static MapBossSpawns BuildMap(LocationBosses location)
    {
        var zonePoints = location.Points
            .GroupBy(p => p.Zone, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => Spots(g.ToList()), StringComparer.Ordinal);

        // "안 나올 확률"을 곱해 둔다: 행마다 독립으로 굴리므로 합칠 때 1 - ∏(1 - p).
        var zoneOrder = new List<string>();
        var inZone = new Dictionary<(string Zone, string Key, bool Conditional), double>();
        var unplaced = new Dictionary<(string Key, bool Conditional), double>();
        var onMap = new Dictionary<(string Key, bool Conditional), double>();
        var rotating = new HashSet<(string Key, bool Conditional)>();

        foreach (var row in location.Rows.Where(r => r.Chance > 0 && Vanilla.ContainsKey(r.Name)))
        {
            var chance = Math.Min(row.Chance, 100) / 100;
            var who = (row.Name, row.Conditional);
            Roll(onMap, who, chance);
            if (row.Rotating) rotating.Add(who);

            // 후보 구역 중 하나를 고르므로 구역 몫 = 나온 횟수 / 후보 수. 해안선처럼 같은 구역을 여러 번 적으면 그만큼 자주 뽑힌다.
            var zones = row.Zone.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (zones.Length == 0)
            {
                Roll(unplaced, who, chance);
                continue;
            }

            foreach (var group in zones.GroupBy(z => z, StringComparer.Ordinal))
            {
                var share = chance * group.Count() / zones.Length;
                if (!zonePoints.ContainsKey(group.Key))
                {
                    Roll(unplaced, who, share);
                    continue;
                }

                if (!zoneOrder.Contains(group.Key)) zoneOrder.Add(group.Key);
                Roll(inZone, (group.Key, row.Name, row.Conditional), share);
            }
        }

        // 구역이 하나뿐인 맵(공장, 그라운드 제로)은 맵 전체가 후보라 나눠도 얻는 게 없어 마커 하나로 둔다.
        var mapSize = zoneOrder.Count > 1 ? MapSize(location.Points) : double.PositiveInfinity;
        var markers = zoneOrder
            .SelectMany(zone =>
            {
                var bosses = Sorted(inZone
                    .Where(e => e.Key.Zone == zone)
                    .Select(e => Entry((e.Key.Key, e.Key.Conditional), e.Value)));
                // 짝 맵은 웹에서 이름으로 합치므로 나눈 마커도 이름이 겹치지 않으면서 맵마다 같아야 한다.
                return Places(zonePoints[zone], mapSize).Select((position, i) => new MapBossZone(i == 0 ? zone : $"{zone}#{i}", position, bosses));
            })
            .ToList();
        var free = Sorted(unplaced.Select(e => Entry(e.Key, e.Value)));
        return new MapBossSpawns(markers, free);

        MapBossEntry Entry((string Key, bool Conditional) who, double miss) => new(
            who.Key, Vanilla[who.Key], Percent(miss), Percent(onMap[who]), who.Conditional, rotating.Contains(who));
    }

    /// <summary>값 = 지금까지 이 묶음이 한 번도 안 나올 확률.</summary>
    private static void Roll<TKey>(Dictionary<TKey, double> misses, TKey key, double chance) where TKey : notnull
        => misses[key] = misses.GetValueOrDefault(key, 1) * (1 - chance);

    /// <summary>1% 미만도 나오긴 하므로 0% 로 쓰지 않는다.</summary>
    private static int Percent(double miss) => Math.Max(1, (int)Math.Round((1 - miss) * 100, MidpointRounding.AwayFromZero));

    private static List<MapBossEntry> Sorted(IEnumerable<MapBossEntry> entries) => entries
        .OrderBy(e => e.Kind == Boss ? 0 : 1)
        .ThenBy(e => e.Conditional)
        .ThenByDescending(e => e.MapChance)
        .ThenBy(e => e.Key, StringComparer.Ordinal)
        .ToList();

    /// <summary>세관 ZoneGasStation 처럼 보스 지점이 하나도 없는 구역은 일반 지점을 쓴다.</summary>
    private static List<MapPoint> Spots(IReadOnlyList<BossZonePoint> points)
    {
        var bossSpots = points.Where(p => p.BossSpot).ToList();
        return (bossSpots.Count > 0 ? bossSpots : points).Select(p => p.Position).ToList();
    }

    /// <summary>구역의 마커 위치들. 보통은 하나, 넓은 구역이면 가까운 지점 묶음마다 하나.</summary>
    private static List<MapPoint> Places(List<MapPoint> spots, double mapSize)
    {
        var middle = Middle(spots);
        return spots.Max(p => Distance(p, middle)) < mapSize * WideZoneRatio ? [middle] : Clusters(spots).Select(Middle).ToList();
    }

    /// <summary>평균에 가장 가까운 실제 지점. 평균 자체를 쓰면 건물 밖이나 다른 층으로 떨어질 수 있다.</summary>
    private static MapPoint Middle(List<MapPoint> spots)
    {
        var (x, y, z) = (spots.Average(p => p.X), spots.Average(p => p.Y), spots.Average(p => p.Z));
        return spots.MinBy(p => Math.Pow(p.X - x, 2) + Math.Pow(p.Y - y, 2) + Math.Pow(p.Z - z, 2))!;
    }

    /// <summary>같은 층에서 ClusterRadius 안으로 이어지는 지점끼리 묶는다.</summary>
    private static List<List<MapPoint>> Clusters(List<MapPoint> spots)
    {
        var left = new List<MapPoint>(spots);
        var clusters = new List<List<MapPoint>>();
        while (left.Count > 0)
        {
            var cluster = new List<MapPoint> { left[0] };
            left.RemoveAt(0);
            for (var i = 0; i < cluster.Count; i++)
            {
                var near = left.Where(p => Distance(p, cluster[i]) <= ClusterRadius && Math.Abs(p.Y - cluster[i].Y) < SameFloorHeight).ToList();
                cluster.AddRange(near);
                left.RemoveAll(near.Contains);
            }

            clusters.Add(cluster);
        }

        return clusters;
    }

    private static double Distance(MapPoint a, MapPoint b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Z - b.Z, 2));

    private static double MapSize(IReadOnlyList<BossZonePoint> points) => points.Count == 0
        ? 0
        : Math.Max(points.Max(p => p.Position.X) - points.Min(p => p.Position.X), points.Max(p => p.Position.Z) - points.Min(p => p.Position.Z));
}
