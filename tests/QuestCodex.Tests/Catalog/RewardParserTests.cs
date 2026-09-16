using QuestCodex.Catalog;
using QuestCodex.Catalog.Models;
using QuestCodex.Catalog.Rewards;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Utils.Json;
using static QuestCodex.Tests.Fixtures;

namespace QuestCodex.Tests.Catalog;

public class RewardParserTests
{
    private static readonly SPTarkov.Server.Core.Models.Common.MongoId M4 = Id(1);

    private static RewardParser NewParser()
    {
        var items = Dict(
            (M4, Template(M4, BaseClasses.ASSAULT_RIFLE, "weapon_colt_m4a1")),
            (BaseClasses.ASSAULT_RIFLE, Template(BaseClasses.ASSAULT_RIFLE, BaseClasses.WEAPON)),
            (BaseClasses.WEAPON, Template(BaseClasses.WEAPON, default)));
        var locale = new LocaleResolver(new Dictionary<string, string> { [$"{M4} Name"] = "Colt M4A1" }, new Dictionary<string, string>());
        return new RewardParser(new ItemCategorizer(items), locale, items);
    }

    [Fact]
    public void Item_reward_uses_first_item_template_name_and_categories()
    {
        var reward = Reward(RewardType.Item, value: 2, items: [Item(M4), Item(Id(50))]);

        var r = Assert.IsType<ItemReward>(NewParser().Parse(reward, [], "q"));

        Assert.Equal(M4.ToString(), r.Tpl);
        Assert.Equal("Colt M4A1", r.Name);
        Assert.Equal(2, r.Count);
        Assert.Equal(["weapon", "assaultRifle"], r.Categories);
        Assert.Equal(RewardParser.IconUrl(M4), r.IconUrl);
    }

    [Fact]
    public void Item_reward_without_locale_falls_back_to_template_name_then_tpl()
    {
        var items = Dict((Id(2), Template(Id(2), BaseClasses.WEAPON, "tpl_name")), (Id(3), Template(Id(3), BaseClasses.WEAPON)));
        var parser = new RewardParser(new ItemCategorizer(items), new LocaleResolver(new Dictionary<string, string>(), new Dictionary<string, string>()), items);

        Assert.Equal("tpl_name", Assert.IsType<ItemReward>(parser.Parse(Reward(RewardType.Item, items: [Item(Id(2))]), [], "q")).Name);
        Assert.Equal(Id(3).ToString(), Assert.IsType<ItemReward>(parser.Parse(Reward(RewardType.Item, items: [Item(Id(3))]), [], "q")).Name);
    }

    [Fact]
    public void Item_reward_with_empty_items_becomes_other_and_warns()
    {
        var warnings = new List<CatalogWarning>();

        var r = NewParser().Parse(Reward(RewardType.Item, items: []), warnings, "q");

        Assert.Equal("Item", Assert.IsType<OtherReward>(r).RewardType);
        Assert.Equal(WarningCodes.EmptyRewardItems, Assert.Single(warnings).Code);
    }

    [Fact]
    public void AssortUnlock_uses_items_template_not_target()
    {
        var reward = Reward(RewardType.AssortmentUnlock, items: [Item(M4)], target: "synthetic-preview-id", loyalty: 2,
            traderId: new StringOrInt(Prapor, null));

        var r = Assert.IsType<AssortUnlockReward>(NewParser().Parse(reward, [], "q"));

        Assert.Equal(M4.ToString(), r.Tpl);
        Assert.Equal(Prapor.ToString(), r.TraderId);
        Assert.Equal(2, r.LoyaltyLevel);
        Assert.Equal(["weapon", "assaultRifle"], r.Categories);
    }

    [Fact]
    public void AssortUnlock_with_empty_items_keeps_null_tpl_and_warns()
    {
        var warnings = new List<CatalogWarning>();
        var reward = Reward(RewardType.AssortmentUnlock, items: [], loyalty: 1, traderId: new StringOrInt(Prapor, null));

        var r = Assert.IsType<AssortUnlockReward>(NewParser().Parse(reward, warnings, "q"));

        Assert.Null(r.Tpl);
        Assert.Null(r.Name);
        Assert.Empty(r.Categories);
        Assert.Equal(WarningCodes.EmptyRewardItems, Assert.Single(warnings).Code);
    }

    [Theory]
    [InlineData(10, null)]
    [InlineData(null, "10")]
    public void ProductionScheme_reads_area_type_from_trader_id_int_or_string(int? num, string? str)
    {
        var reward = Reward(RewardType.ProductionScheme, items: [Item(M4)], traderId: new StringOrInt(str, num));

        var r = Assert.IsType<ProductionReward>(NewParser().Parse(reward, [], "q"));

        Assert.Equal(10, r.AreaType);
        Assert.Equal(M4.ToString(), r.Tpl);
    }

    [Fact]
    public void Simple_rewards_map_values()
    {
        var p = NewParser();
        Assert.Equal(1500, Assert.IsType<ExperienceReward>(p.Parse(Reward(RewardType.Experience, value: 1500), [], "q")).Value);
        var ts = Assert.IsType<TraderStandingReward>(p.Parse(Reward(RewardType.TraderStanding, value: 0.02, target: Prapor), [], "q"));
        Assert.Equal(Prapor.ToString(), ts.TraderId);
        Assert.Equal(0.02, ts.Value);
        Assert.Equal(Prapor.ToString(), Assert.IsType<TraderUnlockReward>(p.Parse(Reward(RewardType.TraderUnlock, target: Prapor), [], "q")).TraderId);
        var sk = Assert.IsType<SkillReward>(p.Parse(Reward(RewardType.Skill, value: 100, target: "Sniper"), [], "q"));
        Assert.Equal("Sniper", sk.Skill);
        Assert.Equal("ach-1", Assert.IsType<AchievementReward>(p.Parse(Reward(RewardType.Achievement, target: "ach-1"), [], "q")).AchievementId);
    }

    [Fact]
    public void Unhandled_and_null_types_become_other()
    {
        var p = NewParser();
        Assert.Equal("StashRows", Assert.IsType<OtherReward>(p.Parse(Reward(RewardType.StashRows, value: 2), [], "q")).RewardType);
        var nullType = Reward(RewardType.Item);
        nullType.Type = null;
        Assert.Equal("unknown", Assert.IsType<OtherReward>(p.Parse(nullType, [], "q")).RewardType);
    }
}
