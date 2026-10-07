namespace QuestCodex.Catalog.Loot;

/// <summary>컨테이너 종류 하나의 루트 테이블(맵 하나). Counts = (개수, 상대 비중), Items = (tpl, 상대 비중).</summary>
public sealed record StaticLootTable(IReadOnlyList<(int Count, double Weight)> Counts, IReadOnlyList<(string Tpl, double Weight)> Items);

/// <summary>맵 하나의 입력. Loot = 컨테이너 tpl → 루트 테이블, Placed = 컨테이너 tpl → 맵에 놓인 개수, Multiplier = staticLootMultiplier.</summary>
public sealed record LootMapInput(
    string Map,
    IReadOnlyDictionary<string, StaticLootTable> Loot,
    IReadOnlyDictionary<string, int> Placed,
    double Multiplier);

/// <summary>아이템 하나의 출처(언어 무관). Containers = (컨테이너 tpl, 하나 열었을 때 들어 있을 확률 0~1) 높은 순, Bots = 봇 묶음 키.</summary>
public sealed record RawLootSource(IReadOnlyList<(string ContainerTpl, double Chance)> Containers, IReadOnlyList<string> Bots);

/// <summary>
/// 카파 제출 아이템 출처(12 kappa-loot-sources 스펙 §2). 컨테이너 루트 테이블과 봇 인벤토리로 "주로 나오는 곳"을 계산한다.
/// 순수 함수 — SPT 타입을 모른다(CatalogService 가 변환해 넘긴다).
/// </summary>
public static class LootSourceIndex
{
    /// <summary>봇 묶음 표시 순서. 웹 i18n 키 kappa.bot.&lt;key&gt;</summary>
    public static readonly IReadOnlyList<string> BotGroupOrder = ["scav", "pmc", "boss", "raider", "cultist", "other"];

    /// <summary>
    /// chance(c, m) = Σ_k P(k) · (1 − (1 − p)^round(k·x)), 맵마다 다른 값은 그 컨테이너가 놓인 개수로 가중 평균.
    /// 맵에 하나도 안 놓인 컨테이너 종류는 뺀다. 비복원 추첨·스폰 상한·칸 부족은 무시하는 근사.
    /// </summary>
    public static Dictionary<string, RawLootSource> Build(IEnumerable<LootMapInput> maps, IReadOnlyDictionary<string, IReadOnlySet<string>> botItems)
    {
        // tpl → 컨테이너 tpl → (Σ 개수×확률, Σ 개수)
        var acc = new Dictionary<string, Dictionary<string, (double Sum, int N)>>(StringComparer.Ordinal);
        foreach (var map in maps)
        {
            foreach (var (container, table) in map.Loot)
            {
                if (!map.Placed.TryGetValue(container, out var placed) || placed <= 0) continue;
                var itemTotal = table.Items.Sum(i => i.Weight);
                var countTotal = table.Counts.Sum(c => c.Weight);
                if (itemTotal <= 0 || countTotal <= 0) continue;
                foreach (var (tpl, weight) in table.Items)
                {
                    if (weight <= 0) continue;
                    var chance = Chance(weight / itemTotal, table.Counts, countTotal, map.Multiplier);
                    if (!acc.TryGetValue(tpl, out var byContainer)) acc[tpl] = byContainer = new(StringComparer.Ordinal);
                    var prev = byContainer.GetValueOrDefault(container);
                    byContainer[container] = (prev.Sum + chance * placed, prev.N + placed);
                }
            }
        }

        var bots = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (botType, tpls) in botItems)
        {
            var group = BotGroup(botType);
            foreach (var tpl in tpls)
            {
                if (!bots.TryGetValue(tpl, out var set)) bots[tpl] = set = new(StringComparer.Ordinal);
                set.Add(group);
            }
        }

        var result = new Dictionary<string, RawLootSource>(StringComparer.Ordinal);
        foreach (var tpl in acc.Keys.Union(bots.Keys))
        {
            var containers = acc.TryGetValue(tpl, out var byContainer)
                ? byContainer.Select(kv => (kv.Key, kv.Value.Sum / kv.Value.N)).Where(c => c.Item2 > 0)
                    .OrderByDescending(c => c.Item2).ThenBy(c => c.Key, StringComparer.Ordinal).ToList()
                : [];
            var groups = bots.TryGetValue(tpl, out var set) ? BotGroupOrder.Where(set.Contains).ToList() : [];
            result[tpl] = new RawLootSource(containers, groups);
        }

        return result;
    }

    /// <summary>아이템 비중 p 인 컨테이너를 하나 열었을 때 그 아이템이 하나 이상 있을 확률</summary>
    public static double Chance(double p, IReadOnlyList<(int Count, double Weight)> counts, double countTotal, double multiplier)
    {
        var sum = 0.0;
        foreach (var (count, weight) in counts)
        {
            var n = (int)Math.Round(count * multiplier, MidpointRounding.AwayFromZero);
            if (n <= 0 || weight <= 0) continue;
            sum += weight / countTotal * (1 - Math.Pow(1 - p, n));
        }

        return sum;
    }

    /// <summary>봇 종류(BotTable.Types 키, 소문자) → 묶음 키</summary>
    public static string BotGroup(string botType)
    {
        var t = botType.ToLowerInvariant();
        if (t is "assault" or "cursedassault" or "crazyassaultevent" or "marksman" or "assaultgroup") return "scav";
        if (t is "pmcusec" or "pmcbear" or "usec" or "bear") return "pmc";
        if (t.StartsWith("boss", StringComparison.Ordinal) || t.StartsWith("follower", StringComparison.Ordinal) || t.Contains("tagilla", StringComparison.Ordinal)) return "boss";
        if (t is "pmcbot" or "exusec" || t.StartsWith("arenafighter", StringComparison.Ordinal)) return "raider";
        if (t.StartsWith("sectant", StringComparison.Ordinal)) return "cultist";
        return "other";
    }
}
