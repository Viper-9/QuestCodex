namespace QuestCodex.Catalog.Models;

public static class WarningCodes
{
    public const string DanglingPrereq = "danglingPrereq";
    public const string MissingLocale = "missingLocale";
    public const string UnknownTrader = "unknownTrader";
    public const string BadId = "badId";
    public const string UnparsedCondition = "unparsedCondition";
    public const string UnparsedReward = "unparsedReward";
    public const string EmptyRewardItems = "emptyRewardItems";
    public const string BuildFailed = "buildFailed";
    public const string OrphanQuest = "orphanQuest";
    public const string VanillaSnapshotMissing = "vanillaSnapshotMissing";
    public const string VanillaSnapshotMismatch = "vanillaSnapshotMismatch";
    public const string ModQuestScanFailed = "modQuestScanFailed";
    public const string ModQuestIdCollision = "modQuestIdCollision";
}

public sealed record CatalogWarning(string? QuestId, string Code, string Detail);

public sealed record CatalogTrader(string Id, string Name, string? AvatarUrl, bool IsVanilla);

public sealed record Objective(
    string ConditionId, string ConditionType, string Text, double? TargetCount, string? TargetName);

public sealed record QuestRewards(
    IReadOnlyList<CatalogReward> Started,
    IReadOnlyList<CatalogReward> Success,
    IReadOnlyList<CatalogReward> Fail);

public sealed class CatalogQuest
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string TraderId { get; init; }
    public required string Side { get; init; }
    public string? FactionOnly { get; init; }
    public bool IsVanilla { get; init; }
    /// <summary>IsVanilla=false 인 퀘스트에서만 채워진다. 출처 모드를 못 찾으면(예: CustomQuestService 로 주입) null.</summary>
    public string? ModName { get; init; }
    public string? ImageUrl { get; init; }
    public int? MinLevel { get; init; }
    public required IReadOnlyList<Requirement> Requirements { get; init; }
    public required IReadOnlyList<string> Prerequisites { get; init; }
    /// <summary>빌더가 전체 순회 후 채운다. ID 오름차순.</summary>
    public List<string> Unlocks { get; } = [];
    public required IReadOnlyList<Objective> Objectives { get; init; }
    public required QuestRewards Rewards { get; init; }
    /// <summary>"isolated" | "traderInternal". 빌더가 Unlocks 확정 후 채운다.</summary>
    public List<string> Tags { get; } = [];
}

public sealed record Catalog(
    string SptVersion,
    string ModVersion,
    DateTimeOffset GeneratedAt,
    string Lang,
    SortedDictionary<string, CatalogTrader> Traders,
    SortedDictionary<string, CatalogQuest> Quests,
    SortedDictionary<string, List<string>> RewardIndex,
    IReadOnlyList<CatalogWarning> Warnings);
