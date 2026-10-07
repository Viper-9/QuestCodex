using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using PointTable = System.Collections.Generic.IReadOnlyDictionary<string, System.Collections.Generic.IReadOnlyDictionary<string, System.Collections.Generic.IReadOnlyList<QuestCodex.Catalog.Models.MapPoint>>>;
using AreaTable = System.Collections.Generic.IReadOnlyDictionary<string, System.Collections.Generic.IReadOnlyDictionary<string, System.Collections.Generic.IReadOnlyList<QuestCodex.Catalog.Models.MapArea>>>;

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
    Func<string, bool>? AvatarIsServable = null,
    /// <summary>map → zoneId → 점. 동봉 스냅샷과 모드 CustomQuestZones 를 합친 것. null 이면 존 좌표 없음.</summary>
    PointTable? QuestZones = null,
    /// <summary>map → 아이템 tpl → looseLoot spawnpointsForced 위치.</summary>
    PointTable? QuestItemSpawns = null,
    /// <summary>로케이션 _Id(MongoId) → map 키(locations 폴더 이름, 소문자). quest.location 이 ID 일 때 쓴다.</summary>
    IReadOnlyDictionary<string, string>? LocationKeys = null,
    bool QuestZoneSnapshotMissing = false,
    /// <summary>map → 스냅샷의 잠긴 문. 빌더가 열쇠 이름을 붙여 Catalog.LockedDoors 로 낸다.</summary>
    IReadOnlyDictionary<string, IReadOnlyList<Models.SnapshotDoor>>? LockedDoors = null,
    /// <summary>핸드북 카테고리 Id → (부모 Id, 아이콘 URL). null 이면 아이템 카테고리를 내보내지 않는다.</summary>
    IReadOnlyDictionary<string, HandbookCategoryInput>? HandbookCategories = null,
    /// <summary>아이템 tpl → 핸드북 카테고리 Id(가장 안쪽).</summary>
    IReadOnlyDictionary<string, string>? HandbookItemParents = null,
    /// <summary>map → zoneId → 영역(스냅샷 Bounds + 모드 WTT Scale·Rotation). InZone·LaunchFlare 목표에만 붙는다.</summary>
    AreaTable? QuestZoneAreas = null,
    /// <summary>map 키 → 활성 지도 변형 ID(MapVariantDetector). 그대로 Catalog.MapVariants 로 낸다.</summary>
    IReadOnlyDictionary<string, string>? MapVariants = null,
    /// <summary>map → 스냅샷의 탈출구·환승 좌표(tarkov.dev). 서버 DB 목록(LocationExits)과 합쳐 Catalog.Exits 로 낸다.</summary>
    IReadOnlyDictionary<string, Models.SnapshotExits>? ExitPositions = null,
    /// <summary>map → 서버 DB 의 탈출구(allExtracts)·환승(base.transits). 무엇을 그릴지는 이 목록이 정한다.</summary>
    IReadOnlyDictionary<string, Models.LocationExits>? LocationExits = null,
    /// <summary>아이템 tpl → 출처(언어 무관, LootSourceIndex). null 이면 LootSources 를 비워 낸다.</summary>
    IReadOnlyDictionary<string, Loot.RawLootSource>? LootSources = null);

public sealed record HandbookCategoryInput(string? ParentId, string? Icon);
