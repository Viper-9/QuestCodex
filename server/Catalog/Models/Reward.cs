using System.Text.Json.Serialization;

namespace QuestCodex.Catalog.Models;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ItemReward), "item")]
[JsonDerivedType(typeof(AssortUnlockReward), "assortUnlock")]
[JsonDerivedType(typeof(ProductionReward), "production")]
[JsonDerivedType(typeof(TraderUnlockReward), "traderUnlock")]
[JsonDerivedType(typeof(TraderStandingReward), "traderStanding")]
[JsonDerivedType(typeof(ExperienceReward), "experience")]
[JsonDerivedType(typeof(SkillReward), "skill")]
[JsonDerivedType(typeof(AchievementReward), "achievement")]
[JsonDerivedType(typeof(OtherReward), "other")]
public abstract record CatalogReward;

public sealed record ItemReward(
    string Tpl, string Name, string? IconUrl, double Count, IReadOnlyList<string> Categories) : CatalogReward;

public sealed record AssortUnlockReward(
    string TraderId, string? Tpl, string? Name, string? IconUrl, int LoyaltyLevel, IReadOnlyList<string> Categories) : CatalogReward;

public sealed record ProductionReward(int AreaType, string? Tpl, string? Name, string? IconUrl) : CatalogReward;

public sealed record TraderUnlockReward(string TraderId) : CatalogReward;

public sealed record TraderStandingReward(string TraderId, double Value) : CatalogReward;

public sealed record ExperienceReward(double Value) : CatalogReward;

public sealed record SkillReward(string Skill, double Value) : CatalogReward;

public sealed record AchievementReward(string AchievementId) : CatalogReward;

public sealed record OtherReward(string RewardType) : CatalogReward;
