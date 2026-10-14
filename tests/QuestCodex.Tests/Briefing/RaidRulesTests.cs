using QuestCodex.Briefing;
using QuestCodex.Catalog.Models;
using static QuestCodex.Tests.Briefing.B;

namespace QuestCodex.Tests.Briefing;

public class RaidRulesTests
{
    private static readonly ItemRef Svd = new("svd", "SVD");
    private static readonly ItemRef Scope = new("scope", "PSO");
    private static readonly ItemRef Silencer = new("sil", "PBS-1");

    [Fact]
    public void GearRows_keeps_alternatives_and_and_bundles_side_by_side()
    {
        var p = Prep(
            weapons: [Svd],
            calibers: ["Caliber762x54R"],
            weaponMods: [[Scope, Silencer], [Silencer]],
            equipment: [[[new("helm", "6B47")], [new("helm2", "SSh-68")]], [[new("vest", "6B3TM"), new("bag", "Pilgrim")]]],
            forbidden: [new("ear", "Comtacs")]);
        var rows = RaidRules.GearRows(p);
        Assert.Equal(new[] { "weapon", "caliber", "weaponMod", "equipment", "equipment", "forbidden" }, rows.Select(r => r.Label).ToArray());
        Assert.Equal(new[] { "PSO + PBS-1", "PBS-1" }, rows[2].Names.ToArray());
        Assert.Equal(new[] { new[] { "scope", "sil" }, new[] { "sil" } }, rows[2].Tpls.Select(t => t.ToArray()).ToArray());
        Assert.Equal(new[] { new[] { "vest", "bag" } }, rows[4].Tpls.Select(t => t.ToArray()).ToArray());
        Assert.Equal(new[] { Array.Empty<string>() }, rows[1].Tpls.Select(t => t.ToArray()).ToArray());   // 구경은 tpl 이 없어서 빈 목록으로 개수만 맞춘다
        Assert.All(rows, r => Assert.Equal(r.Names.Count, r.Tpls.Count));
    }

    [Fact]
    public void SpecialRows_one_raid_and_exit()
    {
        Assert.Equal(new[] { new SpecialRow("oneRaid", [], null) }, RaidRules.SpecialRows(Prep(oneRaid: true)), SpecialEq);
        var both = RaidRules.SpecialRows(Prep(oneRaid: true, exitStatuses: ["Survived"], exitName: "EXFIL_ZB013"));
        Assert.Equal(new[] { "oneRaid", "exit" }, both.Select(r => r.Kind).ToArray());
        Assert.Equal(("Survived", "EXFIL_ZB013"), (string.Join(",", both[1].ExitStatuses), both[1].ExitName));
        Assert.Empty(RaidRules.SpecialRows(Prep()));
    }

    [Fact]
    public void Dedupe_keeps_first_of_equal_rows_in_order()
    {
        var raid = new SpecialRow("oneRaid", [], null);
        var a = new SpecialRow("exit", ["Survived", "Transit"], null);
        var b = new SpecialRow("exit", ["Transit"], "TRANSIT01");
        var rows = RaidRules.DedupeSpecial([raid, a, raid, b, new SpecialRow("exit", ["Survived", "Transit"], null)]);
        Assert.Equal(new[] { raid, a, b }, rows, SpecialEq);
    }

    [Fact]
    public void Dedupe_does_not_mix_name_boundaries()
    {
        var rows = RaidRules.DedupeGear([new GearRow("W", ["a b", "c"], [[], []]), new GearRow("W", ["a", "b c"], [[], []])]);
        Assert.Equal(2, rows.Count);
    }

    // 레코드의 목록 필드는 참조 비교라 내용으로 비교한다
    private static readonly IEqualityComparer<SpecialRow> SpecialEq = EqualityComparer<SpecialRow>.Create(
        (x, y) => x!.Kind == y!.Kind && x.ExitName == y.ExitName && x.ExitStatuses.SequenceEqual(y.ExitStatuses));
}
