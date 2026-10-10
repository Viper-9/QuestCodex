using QuestCodex.Briefing;
using QuestCodex.Catalog.Models;
using QuestCodex.Progress.Models;
using static QuestCodex.Tests.Briefing.B;

namespace QuestCodex.Tests.Briefing;

public class QuestLinesTests
{
    private static RaidEntry E(Objective o, ObjectiveProgress? op) => new(Quest("q", [o]), o, op, ["woods"], false);

    [Fact]
    public void Single_objective_uses_its_text_and_counter()
    {
        var line = QuestLines.Of([E(Obj("k", "CounterCreator", text: "Eliminate Scavs", targetCount: 12), Op(3, 12))], "kr");
        Assert.Equal(new QuestLine("Eliminate Scavs ×12", 3, 12), line);
    }

    [Fact]
    public void Single_objective_without_target_has_no_numbers()
        => Assert.Equal(new QuestLine("Visit", null, null), QuestLines.Of([E(Obj("v", "VisitPlace", text: "Visit"), Op(0, null))], "en"));

    [Fact]
    public void Several_objectives_sum_only_those_with_target_and_cap_current()
    {
        var line = QuestLines.Of([
            E(Obj("a", "CounterCreator"), Op(5, 10)),
            E(Obj("b", "CounterCreator"), Op(9, 4)),       // 넘친 카운터는 target 까지만
            E(Obj("c", "VisitPlace"), Op(0, null)),        // 수치 없는 목표는 빠진다
        ], "kr");
        Assert.Equal(new QuestLine("목표 3개", 9, 14), line);
    }

    [Fact]
    public void Several_objectives_without_any_target_have_no_numbers()
        => Assert.Equal(new QuestLine("2 objectives", null, null),
            QuestLines.Of([E(Obj("a", "VisitPlace"), null), E(Obj("b", "VisitPlace"), Op(0, null))], "en"));

    [Theory]
    [InlineData("kr", "목표 4개")]
    [InlineData("ru", "4 целей")]
    [InlineData("en", "4 objectives")]
    [InlineData("jp", "4 objectives")]
    public void ObjectiveCount_by_language(string lang, string expected) => Assert.Equal(expected, BriefingText.ObjectiveCount(4, lang));

    [Fact]
    public void ObjectiveText_falls_back_to_type_and_target_and_formats_count()
    {
        Assert.Equal("Find ×1,000", BriefingText.ObjectiveText(Obj("a", "FindItem", text: "Find", targetCount: 1000)));
        Assert.Equal("VisitPlace", BriefingText.ObjectiveText(new Objective("b", "VisitPlace", "", null, null)));
        Assert.Equal("HandoverItem: Salewa ×2", BriefingText.ObjectiveText(new Objective("c", "HandoverItem", "", 2, "Salewa")));
    }

    [Fact]
    public void MapName_by_language_variant_and_unknown_key()
    {
        var none = new Dictionary<string, string>();
        Assert.Equal("세관", BriefingText.MapName("bigmap", none, "kr"));
        Assert.Equal("Таможня", BriefingText.MapName("bigmap", none, "ru"));
        Assert.Equal("Customs", BriefingText.MapName("bigmap", none, "jp"));
        Assert.Equal("확장된 인터체인지", BriefingText.MapName("interchange", new Dictionary<string, string> { ["interchange"] = "manimal" }, "kr"));
        Assert.Equal("인터체인지", BriefingText.MapName("interchange", new Dictionary<string, string> { ["interchange"] = "other" }, "kr"));
        Assert.Equal("modmap", BriefingText.MapName("modmap", none, "kr"));
    }
}
