using QuestCodex.Briefing.Models;
using QuestCodex.Catalog.Models;

namespace QuestCodex.Briefing;

// 맵 탭의 보스 출현. 웹 mapProjection.ts bossesForTab 과 같은 규칙이다.
// 짝 맵(공장 주간·야간, 그라운드 제로 저·고레벨)은 한 지도를 나눠 쓰므로 구역 이름으로 합친다.
// 모든 짝 맵에서 값이 같은 보스는 한 줄로, 한쪽에만 있거나 값이 다르면 맵마다 한 줄씩 OnlyOn 을 단다.
// 야간에만 나오는 광신도가 주간에도 나오는 것처럼 보이면 안 되기 때문이다.
public static class BossTabs
{
    public static readonly BriefingBosses Empty = new([], []);

    // mapCount 는 탭에 묶이는 서버 맵 수. 서버는 보스가 없는 맵을 보내지 않을 수 있어서 받은 맵만 세면
    // 한쪽에만 있는 보스를 "모든 맵" 으로 잘못 본다. 그래서 부르는 쪽이 따로 센다.
    public static BriefingBosses ForTab(SortedDictionary<string, MapBossSpawns>? bosses, string tab, int mapCount)
    {
        if (bosses is null) return Empty;
        var sources = bosses.Where(kv => RaidPlan.MapGroup(kv.Key) == tab).ToList();
        if (sources.Count == 0) return Empty;

        // 구역 이름이 같으면 한 마커. 위치는 먼저 나온 맵 것, 순서는 처음 나온 순서.
        var zoneOrder = new List<string>();
        var zones = new Dictionary<string, (MapPoint Position, List<(string Map, IReadOnlyList<MapBossEntry> Bosses)> Lists)>(StringComparer.Ordinal);
        foreach (var (map, spawns) in sources)
        {
            foreach (var z in spawns.Zones ?? [])
            {
                if (zones.TryGetValue(z.Zone, out var seen)) seen.Lists.Add((map, z.Bosses));
                else
                {
                    zoneOrder.Add(z.Zone);
                    zones[z.Zone] = (z.Position, [(map, z.Bosses)]);
                }
            }
        }

        return new BriefingBosses(
            zoneOrder.Select(name => new BriefingBossZone(name, zones[name].Position, Merge(zones[name].Lists, mapCount))).ToList(),
            Merge(sources.Select(kv => (kv.Key, kv.Value.Unplaced ?? [])).ToList(), mapCount));
    }

    // 같은 보스(key + conditional)를 맵끼리 묶는다. 묶음 순서는 처음 나온 순서.
    private static List<BriefingBoss> Merge(List<(string Map, IReadOnlyList<MapBossEntry> Bosses)> lists, int mapCount)
    {
        var order = new List<(string Key, bool Conditional)>();
        var groups = new Dictionary<(string Key, bool Conditional), List<(string Map, MapBossEntry Entry)>>();
        foreach (var (map, list) in lists)
        {
            foreach (var entry in list ?? [])
            {
                var id = (entry.Key, entry.Conditional);
                if (!groups.TryGetValue(id, out var group))
                {
                    order.Add(id);
                    groups[id] = group = [];
                }
                group.Add((map, entry));
            }
        }

        var result = new List<BriefingBoss>();
        foreach (var id in order)
        {
            var group = groups[id];
            var first = group[0].Entry;
            var same = group.Count == mapCount && group.All(g => g.Entry.MapChance == first.MapChance && g.Entry.Rotating == first.Rotating);
            if (same) result.Add(Boss(first, null));
            else result.AddRange(group.Select(g => Boss(g.Entry, g.Map)));
        }
        return result;
    }

    private static BriefingBoss Boss(MapBossEntry e, string? onlyOn) => new(e.Key, e.Kind, e.MapChance, e.Conditional, e.Rotating, onlyOn);
}
