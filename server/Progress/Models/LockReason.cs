using System.Text.Json.Serialization;

namespace QuestCodex.Progress.Models;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(QuestLockReason), "quest")]
[JsonDerivedType(typeof(LevelLockReason), "level")]
[JsonDerivedType(typeof(TraderLoyaltyLockReason), "traderLoyalty")]
[JsonDerivedType(typeof(TraderStandingLockReason), "traderStanding")]
[JsonDerivedType(typeof(FactionLockReason), "faction")]
[JsonDerivedType(typeof(OtherLockReason), "other")]
public abstract record LockReason;

public sealed record QuestLockReason(string QuestId, IReadOnlyList<string> NeedStatuses, string CurrentStatus) : LockReason;
public sealed record LevelLockReason(double Need, string Compare, double Current) : LockReason;
public sealed record TraderLoyaltyLockReason(string TraderId, double Need, string Compare, double Current) : LockReason;
public sealed record TraderStandingLockReason(string TraderId, double Need, string Compare, double Current) : LockReason;
public sealed record FactionLockReason(string Need) : LockReason;
public sealed record OtherLockReason(string ConditionType) : LockReason;
