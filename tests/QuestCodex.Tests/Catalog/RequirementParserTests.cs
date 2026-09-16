using QuestCodex.Catalog;
using QuestCodex.Catalog.Models;
using QuestCodex.Catalog.Requirements;
using SPTarkov.Server.Core.Models.Enums;
using static QuestCodex.Tests.Fixtures;

namespace QuestCodex.Tests.Catalog;

public class RequirementParserTests
{
    private static readonly LocaleResolver Locale = new(new Dictionary<string, string>(), new Dictionary<string, string>());

    [Fact]
    public void Quest_condition_maps_target_statuses_and_resolved()
    {
        var quests = Dict((Id(1), Quest(Id(1))));
        var warnings = new List<CatalogWarning>();

        var req = RequirementParser.Parse(QuestCond(Id(100), Id(1), QuestStatusEnum.Started, QuestStatusEnum.Success), Locale, quests, warnings, "q");

        var q = Assert.IsType<QuestRequirement>(req);
        Assert.Equal(Id(1).ToString(), q.QuestId);
        Assert.Equal(["Started", "Success"], q.NeedStatuses.OrderBy(s => s));
        Assert.Equal(0, q.AvailableAfterSec);
        Assert.True(q.Resolved);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Quest_condition_with_unknown_target_is_unresolved_and_warns()
    {
        var warnings = new List<CatalogWarning>();

        var req = RequirementParser.Parse(QuestCond(Id(100), Id(42)), Locale, Dict<SPTarkov.Server.Core.Models.Eft.Common.Tables.Quest>(), warnings, "q");

        Assert.False(Assert.IsType<QuestRequirement>(req).Resolved);
        var w = Assert.Single(warnings);
        Assert.Equal(WarningCodes.DanglingPrereq, w.Code);
        Assert.Equal("q", w.QuestId);
        Assert.Contains(Id(42).ToString(), w.Detail);
    }

    [Fact]
    public void Quest_condition_without_statuses_defaults_to_success()
    {
        var cond = QuestCond(Id(100), Id(1));
        cond.Status = null;

        var req = RequirementParser.Parse(cond, Locale, Dict((Id(1), Quest(Id(1)))), [], "q");

        Assert.Equal(["Success"], Assert.IsType<QuestRequirement>(req).NeedStatuses);
    }

    [Fact]
    public void Level_condition_keeps_compare_method()
    {
        var req = RequirementParser.Parse(LevelCond(Id(100), 15, "<="), Locale, Dict<SPTarkov.Server.Core.Models.Eft.Common.Tables.Quest>(), [], "q");

        var l = Assert.IsType<LevelRequirement>(req);
        Assert.Equal(15, l.Value);
        Assert.Equal("<=", l.Compare);
    }

    [Fact]
    public void Level_condition_without_compare_defaults_to_gte()
    {
        var req = RequirementParser.Parse(LevelCond(Id(100), 5, compare: null!), Locale, Dict<SPTarkov.Server.Core.Models.Eft.Common.Tables.Quest>(), [], "q");
        Assert.Equal(">=", Assert.IsType<LevelRequirement>(req).Compare);
    }

    [Theory]
    [InlineData("TraderLoyalty", typeof(TraderLoyaltyRequirement))]
    [InlineData("TraderStanding", typeof(TraderStandingRequirement))]
    public void Trader_conditions_map_trader_and_value(string type, Type expected)
    {
        var req = RequirementParser.Parse(TraderCond(Id(100), type, Prapor, 2), Locale, Dict<SPTarkov.Server.Core.Models.Eft.Common.Tables.Quest>(), [], "q");

        Assert.IsType(expected, req);
        var (traderId, value) = req switch
        {
            TraderLoyaltyRequirement t => (t.TraderId, t.Value),
            TraderStandingRequirement t => (t.TraderId, t.Value),
            _ => throw new Exception(),
        };
        Assert.Equal(Prapor.ToString(), traderId);
        Assert.Equal(2, value);
    }

    [Fact]
    public void Unknown_type_becomes_other_with_locale_text()
    {
        var locale = new LocaleResolver(new Dictionary<string, string> { [Id(100).ToString()] = "Reach level 10 in Sniper" }, new Dictionary<string, string>());
        var cond = FinishCond(Id(100), "Skill");

        var req = RequirementParser.Parse(cond, locale, Dict<SPTarkov.Server.Core.Models.Eft.Common.Tables.Quest>(), [], "q");

        var o = Assert.IsType<OtherRequirement>(req);
        Assert.Equal("Skill", o.ConditionType);
        Assert.Equal("Reach level 10 in Sniper", o.Text);
    }

    [Fact]
    public void Unknown_type_without_locale_uses_condition_type_as_text()
    {
        var req = RequirementParser.Parse(FinishCond(Id(100), "Skill"), Locale, Dict<SPTarkov.Server.Core.Models.Eft.Common.Tables.Quest>(), [], "q");
        Assert.Equal("Skill", Assert.IsType<OtherRequirement>(req).Text);
    }

    [Fact]
    public void Quest_condition_with_missing_target_becomes_other_and_warns()
    {
        var cond = QuestCond(Id(100), Id(1));
        cond.Target = null;
        var warnings = new List<CatalogWarning>();

        var req = RequirementParser.Parse(cond, Locale, Dict<SPTarkov.Server.Core.Models.Eft.Common.Tables.Quest>(), warnings, "q");

        Assert.IsType<OtherRequirement>(req);
        Assert.Equal(WarningCodes.UnparsedCondition, Assert.Single(warnings).Code);
    }

    [Fact]
    public void TargetOf_reads_item_or_first_list_entry()
    {
        var single = QuestCond(Id(1), Id(2));
        Assert.Equal(Id(2).ToString(), RequirementParser.TargetOf(single));

        var list = QuestCond(Id(1), Id(2));
        list.Target = new SPTarkov.Server.Core.Utils.Json.ListOrT<string>(["a", "b"], null);
        Assert.Equal("a", RequirementParser.TargetOf(list));

        list.Target = null;
        Assert.Null(RequirementParser.TargetOf(list));
    }
}
