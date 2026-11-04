using QuestCodex.Catalog.Models;

namespace QuestCodex.Catalog.Locations;

/// <summary>
/// 보스 출현(18 스펙). 값은 서버 표(base.BossLocationSpawn — 위치 모드가 바꾼 확률 포함)를 그대로 읽고, 바닐라 이름만 남긴다.
/// 모드 보스를 빼는 이유: ISB 처럼 레이드마다 다시 뽑아 표에 한 번의 추첨 결과만 남기는 모드가 있어 표를 믿을 수 없다.
/// 지점은 구역마다 하나로 묶는다 — 연구소 레이더처럼 행이 많아도 지도에는 구역 수만큼만 그려진다.
/// </summary>
public static class BossSpawnBuilder
{
    private const string Boss = "boss";
    private const string Minor = "minor";

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
        var positions = location.Points
            .GroupBy(p => p.Zone, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => MarkerPosition(g.ToList()), StringComparer.Ordinal);

        // "안 나올 확률"을 곱해 둔다: 행마다 독립으로 굴리므로 합칠 때 1 - ∏(1 - p).
        var zoneOrder = new List<string>();
        var inZone = new Dictionary<(string Zone, string Key, bool Conditional), double>();
        var unplaced = new Dictionary<(string Key, bool Conditional), double>();
        var onMap = new Dictionary<(string Key, bool Conditional), double>();

        foreach (var row in location.Rows.Where(r => r.Chance > 0 && Vanilla.ContainsKey(r.Name)))
        {
            var chance = Math.Min(row.Chance, 100) / 100;
            var who = (row.Name, row.Conditional);
            Roll(onMap, who, chance);

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
                if (!positions.ContainsKey(group.Key))
                {
                    Roll(unplaced, who, share);
                    continue;
                }

                if (!zoneOrder.Contains(group.Key)) zoneOrder.Add(group.Key);
                Roll(inZone, (group.Key, row.Name, row.Conditional), share);
            }
        }

        var markers = zoneOrder
            .Select(zone => new MapBossZone(zone, positions[zone], Sorted(inZone
                .Where(e => e.Key.Zone == zone)
                .Select(e => Entry(e.Key.Key, e.Key.Conditional, e.Value, onMap)))))
            .ToList();
        var free = Sorted(unplaced.Select(e => Entry(e.Key.Key, e.Key.Conditional, e.Value, onMap)));
        return new MapBossSpawns(markers, free);
    }

    /// <summary>값 = 지금까지 이 묶음이 한 번도 안 나올 확률.</summary>
    private static void Roll<TKey>(Dictionary<TKey, double> misses, TKey key, double chance) where TKey : notnull
        => misses[key] = misses.GetValueOrDefault(key, 1) * (1 - chance);

    private static MapBossEntry Entry(string key, bool conditional, double miss, Dictionary<(string Key, bool Conditional), double> onMap)
        => new(key, Vanilla[key], Percent(miss), Percent(onMap[(key, conditional)]), conditional);

    /// <summary>1% 미만도 나오긴 하므로 0% 로 쓰지 않는다.</summary>
    private static int Percent(double miss) => Math.Max(1, (int)Math.Round((1 - miss) * 100, MidpointRounding.AwayFromZero));

    private static List<MapBossEntry> Sorted(IEnumerable<MapBossEntry> entries) => entries
        .OrderBy(e => e.Kind == Boss ? 0 : 1)
        .ThenByDescending(e => e.Chance)
        .ThenBy(e => e.Key, StringComparer.Ordinal)
        .ToList();

    /// <summary>
    /// 구역 안 스폰 지점의 평균에 가장 가까운 실제 지점. 평균 자체를 쓰면 건물 밖이나 다른 층으로 떨어질 수 있다.
    /// 세관 ZoneGasStation 처럼 보스 지점이 하나도 없는 구역은 일반 지점을 쓴다.
    /// </summary>
    private static MapPoint MarkerPosition(IReadOnlyList<BossZonePoint> points)
    {
        var bossSpots = points.Where(p => p.BossSpot).ToList();
        var candidates = (bossSpots.Count > 0 ? bossSpots : points).Select(p => p.Position).ToList();
        var (x, y, z) = (candidates.Average(p => p.X), candidates.Average(p => p.Y), candidates.Average(p => p.Z));
        return candidates.MinBy(p => Math.Pow(p.X - x, 2) + Math.Pow(p.Y - y, 2) + Math.Pow(p.Z - z, 2))!;
    }
}
