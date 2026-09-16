using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace QuestCodex.Catalog;

/// <summary>CatalogBuilder 입력. SPT 딕셔너리는 참조로 넘긴다(복사 없음).</summary>
public sealed record CatalogInput(
    string Lang,
    string SptVersion,
    string ModVersion,
    IReadOnlyDictionary<MongoId, Quest> Quests,
    IReadOnlyDictionary<MongoId, TraderBase> Traders,
    IReadOnlyDictionary<MongoId, TemplateItem> Items,
    IReadOnlyDictionary<string, string> Locale,
    IReadOnlyDictionary<string, string> FallbackLocale,
    IReadOnlySet<MongoId> BearOnly,
    IReadOnlySet<MongoId> UsecOnly,
    IReadOnlySet<string>? VanillaQuestIds,
    string? VanillaSnapshotSptVersion);
