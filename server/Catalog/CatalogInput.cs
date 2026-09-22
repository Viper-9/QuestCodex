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
    string? VanillaSnapshotSptVersion,
    IReadOnlyDictionary<string, string>? ModQuestOrigins = null,
    IReadOnlyList<Models.CatalogWarning>? ModQuestScanWarnings = null,
    /// <summary>
    /// "/files/…" 아바타 URL 을 SPT 이미지 라우터가 실제로 서빙할 수 있는지. null 이면 검사하지 않는다.
    /// 서빙 불가한 URL 을 그대로 내보내면 프론트가 매번 404 를 때려 서버 로그에 에러가 쌓인다.
    /// </summary>
    Func<string, bool>? AvatarIsServable = null);
