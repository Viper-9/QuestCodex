using QuestCodex.Briefing;
using static QuestCodex.Tests.Briefing.B;

namespace QuestCodex.Tests.Briefing;

public class RaidNeedsTests
{
    [Fact]
    public void Remaining_is_target_minus_current_and_zero_when_done()
    {
        var o = Obj("a", "HandoverItem", Prep(Item(count: 5)));
        Assert.Equal(3, RaidNeeds.Remaining(o, Op(2, 5)));
        Assert.Equal(0, RaidNeeds.Remaining(o, Op(7, 5)));
        Assert.Equal(0, RaidNeeds.Remaining(o, Op(0, 5, done: true)));
    }

    [Fact]
    public void Remaining_without_counter_uses_prep_count_then_target_count()
    {
        Assert.Equal(4, RaidNeeds.Remaining(Obj("a", "HandoverItem", Prep(Item(count: 4))), null));
        Assert.Equal(15, RaidNeeds.Remaining(Obj("b", "CounterCreator", null, 15), null));
    }

    [Fact]
    public void ItemKey_sorts_distinct_tpls()
    {
        Assert.Equal("tagA|tagB", RaidNeeds.ItemKey(Item(items: [TagB, TagA, TagB])));
        Assert.Equal("ms2000", RaidNeeds.ItemKey(Item()));
    }

    // 광신도 2부: MS2000 을 목표 3개로 나눠 요구. 다른 퀘스트도 2개 요구.
    // 웹 원본은 isPending 으로 못 받는 퀘스트를 거르지만 브리핑은 진행 중 퀘스트만 보니까 accept 를 직접 넘기고 진영 사례는 뺐다.
    [Fact]
    public void AggregateNeeds_sums_remaining_of_same_tpl_before_comparing_holdings()
    {
        var cult = Quest("cult", new[] { 1, 2, 3 }.Select(n => Obj($"c{n}", "PlaceBeacon", Prep(Item("plant")))).ToList());
        var other = Quest("other", [Obj("o1", "PlaceBeacon", Prep(Item("plant", count: 2)))]);
        var done = Quest("done", [Obj("d1", "HandoverItem", Prep(Item(count: 9)))]);
        var cat = B.Catalog([cult, other, done]);
        var prog = B.Progress(new()
        {
            ["cult"] = Qp("Started", new() { ["c1"] = Op(1, 1, true), ["c2"] = Op(0, 1), ["c3"] = Op(0, 1) }),
            ["other"] = Qp("Locked"),
            ["done"] = Qp("Success"),
        }, Inv(("ms2000", 1, 0)));

        var rows = RaidNeeds.AggregateNeeds(RaidNeeds.ItemNeeds(cat, prog, qp => qp.Status != "Success"), prog.Inventory);

        var row = Assert.Single(rows);
        Assert.Equal(("ms2000", 4.0, 0.0, 1.0, 0.0), (row.Key, row.Need, row.NeedFir, row.Have, row.HaveFir));
        Assert.Equal(new[] { "c2", "c3", "o1" }, row.Sources.Select(s => s.ConditionId));
        Assert.Equal(3, RaidNeeds.Missing(row));
    }

    [Fact]
    public void AggregateNeeds_counts_holdings_of_all_alternatives()
    {
        var q = Quest("tags", [Obj("t", "HandoverItem", Prep(Item(items: [TagA, TagB], count: 5, fir: true)))]);
        var rows = RaidNeeds.AggregateNeeds(
            RaidNeeds.ItemNeeds(B.Catalog([q]), B.Progress(new() { ["tags"] = Qp("Started") }), _ => true),
            Inv(("tagA", 2, 2), ("tagB", 3, 3)));
        Assert.Equal((5.0, 5.0, 5.0, 5.0), (rows[0].Need, rows[0].NeedFir, rows[0].Have, rows[0].HaveFir));
        Assert.Equal(0, RaidNeeds.Missing(rows[0]));
    }

    [Fact]
    public void Missing_fills_fir_only_from_fir_holdings()
    {
        Assert.Equal(2, RaidNeeds.Missing(5, 3, 4, 1));
        Assert.Equal(1, RaidNeeds.Missing(5, 0, 4, 0));
        Assert.Equal(3, RaidNeeds.Missing(3, 3, 10, 0));
        Assert.Equal(1, RaidNeeds.Missing(4, 2, 3, 3));
    }

    [Fact]
    public void SortNeedRows_orders_by_shortage()
    {
        NeedRow Row(string key, string name, double need, double have, double needFir = 0)
            => new() { Key = key, Items = [new(key, name)], Need = need, NeedFir = needFir, Have = have, HaveFir = 0 };
        var rows = new[] { Row("a", "Alpha", 1, 1), Row("b", "Bravo", 5, 0), Row("c", "T H I C C Item case", 2, 1, 2) };
        Assert.Equal(new[] { "b", "c", "a" }, RaidNeeds.SortNeedRows(rows).Select(r => r.Key));
    }
}
