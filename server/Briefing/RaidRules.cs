using QuestCodex.Catalog.Models;

namespace QuestCodex.Briefing;

// 장비 조건과 특수 조건을 줄 단위로 만든다. 웹 RaidView 의 gearRows·specialRows 와 derive.ts dedupeRows 를 옮긴 것.

// 장비 조건 한 줄. Label 은 코드값이고 번역은 클라이언트가 한다.
// Names[i] 는 i 번째 대안의 표시 문구("A + B"), Tpls[i] 는 그 대안에 함께 있어야 하는 tpl 묶음.
// 웹은 문구만 쓰지만 Briefing 쪽 장착 확인(GearCheck)이 묶음 단위로 맞춰 봐야 해서 tpl 도 같이 보낸다.
// 구경은 tpl 이 없으니 Tpls[i] 가 빈 목록이다.
// "build" 줄은 무기와 필수 부착물을 한 총으로 묶은 것. Names/Tpls 는 weaponMod 줄처럼 부착물 대안이고,
// Weapons/Calibers 는 그 부착물을 달아야 하는 총이다(둘 다 비어 있으면 아무 총).
// ModParents 는 부착물 대안의 부모 분류(_parent) 목록. 장착 총에 인정 목록 밖의 같은 종류 부품이 있는지 찾는 데 쓴다.
// 다른 줄은 Weapons/Calibers/ModParents 가 null.
public sealed record GearRow(
    string Label, IReadOnlyList<string> Names, IReadOnlyList<IReadOnlyList<string>> Tpls,
    IReadOnlyList<ItemRef>? Weapons = null, IReadOnlyList<string>? Calibers = null, IReadOnlyList<string>? ModParents = null);

// Kind 는 "oneRaid" 아니면 "exit". oneRaid 줄은 ExitStatuses 가 비어 있고 ExitName 이 null.
public sealed record SpecialRow(string Kind, IReadOnlyList<string> ExitStatuses, string? ExitName);

public static class RaidRules
{
    public static List<GearRow> GearRows(ObjectivePrep p)
    {
        var rows = new List<GearRow>();
        // 예전 카탈로그 JSON(녹화 데이터)에는 builds 가 없어서 null 로 들어온다
        var builds = p.Builds ?? [];
        foreach (var b in builds) rows.Add(Options("build", b.ModOptions) with { Weapons = b.Weapons, Calibers = b.Calibers, ModParents = b.ModParents });

        // build 에 들어간 무기·구경·부착물은 따로 줄을 만들지 않는다. 다른 하위 조건의 무기 조건만 남는다.
        var buildWeapons = builds.SelectMany(b => b.Weapons).Select(w => w.Tpl).ToHashSet();
        var buildCalibers = builds.SelectMany(b => b.Calibers).ToHashSet();
        var buildMods = builds.SelectMany(b => b.ModOptions).Select(TplKey).ToHashSet();
        var weapons = p.Weapons.Where(w => !buildWeapons.Contains(w.Tpl)).ToList();
        var calibers = p.Calibers.Where(c => !buildCalibers.Contains(c)).ToList();
        var mods = p.WeaponMods.Where(o => !buildMods.Contains(TplKey(o))).ToList();

        if (weapons.Count > 0) rows.Add(Singles("weapon", weapons));
        if (calibers.Count > 0) rows.Add(new GearRow("caliber", calibers, calibers.Select(_ => (IReadOnlyList<string>)[]).ToList()));
        if (mods.Count > 0) rows.Add(Options("weaponMod", mods));
        // 슬롯은 전부 맞춰야 하니 슬롯마다 한 줄. 한 줄 안의 대안은 그중 하나만 있으면 된다.
        foreach (var slot in p.Equipment) rows.Add(Options("equipment", slot));
        if (p.ForbiddenEquipment.Count > 0) rows.Add(Singles("forbidden", p.ForbiddenEquipment));
        return rows;
    }

    public static List<SpecialRow> SpecialRows(ObjectivePrep p)
    {
        var rows = new List<SpecialRow>();
        if (p.OneRaid) rows.Add(new SpecialRow("oneRaid", [], null));
        if (p.ExitStatuses.Count > 0 || p.ExitName is not null) rows.Add(new SpecialRow("exit", p.ExitStatuses, p.ExitName));
        return rows;
    }

    // 라벨과 값이 같은 줄은 한 번만 보여 준다. New Day, New Paths 처럼 목표 4개가 전부 "한 레이드 안에" 인 경우가 있다.
    public static List<GearRow> DedupeGear(IEnumerable<GearRow> rows)
        => Dedupe(rows, r => $"{r.Label}\u0000{string.Join('\u0001', r.Names)}"
            + $"\u0000{string.Join('\u0001', r.Weapons?.Select(w => w.Tpl) ?? [])}\u0000{string.Join('\u0001', r.Calibers ?? [])}");

    public static List<SpecialRow> DedupeSpecial(IEnumerable<SpecialRow> rows)
        => Dedupe(rows, r => $"{r.Kind}\u0000{string.Join('\u0001', r.ExitStatuses)}\u0000{r.ExitName}");

    private static List<T> Dedupe<T>(IEnumerable<T> rows, Func<T, string> key)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return rows.Where(r => seen.Add(key(r))).ToList();
    }

    private static string TplKey(IReadOnlyList<ItemRef> option) => string.Join('\u0001', option.Select(i => i.Tpl));

    private static GearRow Singles(string label, IReadOnlyList<ItemRef> items)
        => new(label, items.Select(i => i.Name).ToList(), items.Select(i => (IReadOnlyList<string>)[i.Tpl]).ToList());

    private static GearRow Options(string label, IReadOnlyList<IReadOnlyList<ItemRef>> options)
        => new(label,
            options.Select(o => string.Join(" + ", o.Select(i => i.Name))).ToList(),   // 웹 prep.ts optionText 와 같은 형식
            options.Select(o => (IReadOnlyList<string>)o.Select(i => i.Tpl).ToList()).ToList());
}
