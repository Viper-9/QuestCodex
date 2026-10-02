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

/// <summary>
/// 해석하지 않는 시작 조건(FindItem 등). Text 는 조건 로케일, 없으면 빈 문자열 — 문구 조립은 프론트(i18n) 몫이라
/// 목표(Objective)와 같은 규칙으로 대상 아이템 이름만 TargetName 에 실어 보낸다.
/// </summary>
public sealed record OtherRequirement(string ConditionType, string Text, string? TargetName = null) : Requirement;
