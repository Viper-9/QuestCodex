using System.Globalization;
using QuestCodex.Catalog.Models;

namespace QuestCodex.Catalog.Locations;

/// <summary>
/// 탈출구·환승(10 스펙 §2.2). 무엇을 그릴지는 서버 DB(allExtracts·base.transits — 모드가 바꾼 목록 포함)가 정하고,
/// 스냅샷(tarkov.dev)은 같은 게임 키로 좌표와 영문 이름만 준다. 그래서 라이브에만 있는 탈출구는 자연히 빠진다.
/// </summary>
public static class ExitBuilder
{
    public static SortedDictionary<string, List<MapExit>> Build(
        IReadOnlyDictionary<string, SnapshotExits>? positions,
        IReadOnlyDictionary<string, LocationExits>? db,
        LocaleResolver locale,
        Func<string, string> nameOf,
        List<CatalogWarning> warnings)
    {
        var result = new SortedDictionary<string, List<MapExit>>(StringComparer.Ordinal);
        foreach (var (map, rows) in db ?? new Dictionary<string, LocationExits>())
        {
            // 스냅샷에 맵 자체가 없으면(모드 신규 맵 등) 그릴 지도도 없다 — 경고 없이 넘어간다.
            if (positions is null || !positions.TryGetValue(map, out var snap)) continue;

            var exitAt = snap.Exits.GroupBy(e => e.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            var list = new List<MapExit>();
            // 같은 Name 이 진영마다 한 행씩 있다(예: SE Exfil 이 Pmc·Scav 두 행). 순서는 DB 첫 등장 순.
            foreach (var group in rows.Exits.GroupBy(r => r.Name, StringComparer.Ordinal))
            {
                if (!exitAt.TryGetValue(group.Key, out var at))
                {
                    warnings.Add(new CatalogWarning(null, WarningCodes.ExitPositionMissing, $"{map}/{group.Key}"));
                    continue;
                }

                // 진영마다 조건이 다를 수 있다. 퀘스트는 PMC 로 하니 PMC 행을 보여 준다.
                var row = group.FirstOrDefault(r => r.Side == "Pmc") ?? group.First();
                var (requirement, requirementKind) = RequirementOf(row, locale, nameOf);
                list.Add(new MapExit(group.Key, at.Name, KindOf(group.Select(r => r.Side)), at.Position, requirement, requirementKind,
                    row.Chance is { } c && c < 100 ? (int)Math.Round(c) : null, null));
            }

            MergeSidePairs(list);

            var transitAt = snap.Transits.GroupBy(t => t.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            foreach (var t in rows.Transits.Where(t => t.Active))
            {
                if (!transitAt.TryGetValue(t.Id, out var at))
                {
                    // tarkov.dev 에 환승이 하나도 없는 맵은 경고하지 않는다 — 미궁 base.json 은 삼림 환승(15·16·17·41)을 그대로 복사해 들고 있다.
                    if (snap.Transits.Count == 0) continue;
                    warnings.Add(new CatalogWarning(null, WarningCodes.ExitPositionMissing, $"{map}/transit {t.Id}"));
                    continue;
                }

                list.Add(new MapExit(t.Id, "", "transit", at.Position, null, null, null, t.Target.ToLowerInvariant()));
            }

            if (list.Count > 0) result[map] = list;
        }

        return result;
    }

    /// <summary>
    /// SPT 는 같은 탈출구를 진영마다 다른 키로 두기도 한다(해안선 Road to Customs / Scav Road to Customs, 등대 Shorl_free /
    /// Shorl_free_scav, 세관 RUAF Roadblock / RUAF Roadblock_scav). 영문 이름이 같고 5m 안이면 공용 하나로 합친다 — 안 그러면
    /// 아이콘이 겹쳐 나중에 그린 스캐브 것만 보인다. 남는 쪽은 PMC 행(조건·확률도 PMC 기준). 이름이 다르면(세관 Dorms V-Ex
    /// 와 Old Road Gate) 같은 자리라도 별개 탈출구다.
    /// </summary>
    private static void MergeSidePairs(List<MapExit> list)
    {
        foreach (var scav in list.Where(e => e.Kind == "scav").ToList())
        {
            var pmcIndex = list.FindIndex(e => e.Kind == "pmc" && e.Name == scav.Name
                && Math.Sqrt(Math.Pow(e.Position.X - scav.Position.X, 2) + Math.Pow(e.Position.Z - scav.Position.Z, 2)) < 5);
            if (pmcIndex < 0) continue;
            list[pmcIndex] = list[pmcIndex] with { Kind = "shared" };
            list.Remove(scav);
        }
    }

    /// <summary>협동(Coop)은 PMC·스캐브 양쪽이 쓰는 공용으로 본다(tarkov.dev 의 shared 와 같다).</summary>
    private static string KindOf(IEnumerable<string> sides)
    {
        var set = sides.ToHashSet(StringComparer.Ordinal);
        if (set.Contains("Coop") || (set.Contains("Pmc") && set.Contains("Scav"))) return "shared";
        if (set.Contains("Scav")) return "scav";
        return set.Contains("Pmc") ? "pmc" : "shared";
    }

    /// <summary>
    /// 로케일에 문구가 있으면 그 문구(TransferItem 은 {0} 에 "5,000 루블"). 없으면 웹이 번역할 코드:
    /// 협동 coop, 열차 train(팁이 "TIP IS HARDCODED"), 비밀 탈출구 secret, 등반 장비 alpinist(Reference + Id "Alpinist"), 문구 없는 이벤트 switch.
    /// </summary>
    private static (string? Text, string? Kind) RequirementOf(LocationExitRow row, LocaleResolver locale, Func<string, string> nameOf)
    {
        switch (row.Requirement)
        {
            case "ScavCooperation": return (null, "coop");
            case "Train": return (null, "train");
            case "Secret": return (null, "secret"); // CatalogService 가 base.secretExits 에 붙인 합성 값
            case "Reference": return (null, row.ItemId == "Alpinist" ? "alpinist" : null);
        }

        var tip = string.IsNullOrWhiteSpace(row.Tip) ? null : locale.TryResolve(row.Tip);
        if (tip is not null)
        {
            if (row.Requirement == "TransferItem" && !string.IsNullOrEmpty(row.ItemId))
            {
                var currency = locale.TryResolve($"{row.ItemId} ShortName") ?? nameOf(row.ItemId);
                tip = tip.Replace("{0}", $"{row.Count.ToString("N0", CultureInfo.InvariantCulture)} {currency}", StringComparison.Ordinal);
            }

            return (tip, null);
        }

        return (null, row.Requirement == "WorldEvent" ? "switch" : null);
    }
}
