using QuestCodex.Briefing;
using QuestCodex.Catalog.Models;
using QuestCodex.Progress.Models;
using static QuestCodex.Tests.Briefing.B;

namespace QuestCodex.Tests.Briefing;

public class RaidFindsTests
{
    [Fact]
    public void Rows_are_started_handovers_not_covered_by_holdings_sorted_by_shortage()
    {
        var cowboy = new ItemRef("cowboy", "Cowboy hat");
        var hats = Quest("hats", [
            Obj("f", "FindItem", null, 2),
            Obj("h1", "HandoverItem", Prep(Item(items: [cowboy], count: 2, fir: true))),
            Obj("h2", "HandoverItem", Prep(Item(items: [Jammer], count: 3))),
            Obj("p", "PlaceBeacon", Prep(Item("plant", count: 5))),
        ]);
        var later = Quest("later", [Obj("x", "HandoverItem", Prep(Item(items: [cowboy], count: 9)))]);
        var prog = B.Progress(new()
        {
            // 찾기 카운터는 0 이지만 하나는 이미 제출했으니 남은 제출 1 개로 계산해야 한다
            ["hats"] = Qp("Started", new() { ["f"] = Op(0, 2), ["h1"] = Op(1, 2) }),
            ["later"] = Qp("Locked"),
        });
        var rows = RaidFinds.Rows(B.Catalog([hats, later]), prog, Inv(("cowboy", 3, 0), ("jam", 1, 0)));
        Assert.Equal(new[] { ("jam", 3.0, 2.0), ("cowboy", 1.0, 1.0) }, rows.Select(r => (r.Key, r.Need, RaidNeeds.Missing(r))).ToArray());
    }

    [Fact]
    public void Place_reads_maps_from_find_objectives_here_then_unknown_then_elsewhere()
    {
        var letter = new ItemRef("letter", "Registered letter");
        var pump = new ItemRef("pump", "Pump data");
        var hat = new ItemRef("hat", "Cowboy hat");
        Objective Hand(string id, ItemRef it) => Obj(id, "HandoverItem", Prep(Item(items: [it])));
        Objective Find(string id, string text) => Obj(id, "FindItem", text: text);
        var cat = B.Catalog([
            Quest("mail", [Find("f1", "Find the letter on Streets of Tarkov"), Hand("h1", letter)]),
            Quest("pumps", [Find("f2", "등대(Lighthouse) 지역의 펌프장에서 정보 찾기"), Hand("h2", pump)]),
            Quest("hats", [Find("f3", "레이드에서 [카우보이 모자] 획득하기"), Hand("h3", hat)]),
        ]);
        var prog = B.Progress(new() { ["mail"] = Qp("Started"), ["pumps"] = Qp("Started"), ["hats"] = Qp("Started") });
        var placed = RaidFinds.Place(RaidFinds.Rows(cat, prog, new Dictionary<string, ItemHolding>()), cat, "lighthouse");
        Assert.Equal(new[] { ("pump", "lighthouse", "here"), ("hat", "", "unknown"), ("letter", "tarkovstreets", "elsewhere") },
            placed.Select(p => (p.Row.Key, string.Join(",", p.Maps), p.Place)).ToArray());
    }

    [Fact]
    public void Money_handovers_are_not_finds()
    {
        var euro = new ItemRef("569668774bdc2da2298b4568", "Euros");
        var q = Quest("mentor", [Obj("m", "HandoverItem", Prep(Item(items: [euro], count: 50000)))]);
        Assert.Empty(RaidFinds.Rows(B.Catalog([q]), B.Progress(new() { ["mentor"] = Qp("Started") }), new Dictionary<string, ItemHolding>()));
    }
}
