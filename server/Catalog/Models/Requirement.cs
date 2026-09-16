using System.Text.Json.Serialization;

namespace QuestCodex.Catalog.Models;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(QuestRequirement), "quest")]
[JsonDerivedType(typeof(LevelRequirement), "level")]
[JsonDerivedType(typeof(TraderLoyaltyRequirement), "traderLoyalty")]
[JsonDerivedType(typeof(TraderStandingRequirement), "traderStanding")]
[JsonDerivedType(typeof(OtherRequirement), "other")]
public abstract record Requirement;

public sealed record QuestRequirement(
    string QuestId, IReadOnlyList<string> NeedStatuses, int AvailableAfterSec, bool Resolved) : Requirement;

public sealed record LevelRequirement(double Value, string Compare) : Requirement;

public sealed record TraderLoyaltyRequirement(string TraderId, double Value, string Compare) : Requirement;

public sealed record TraderStandingRequirement(string TraderId, double Value, string Compare) : Requirement;

public sealed record OtherRequirement(string ConditionType, string Text) : Requirement;
