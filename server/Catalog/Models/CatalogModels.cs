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
    public const string QuestZoneSnapshotMissing = "questZoneSnapshotMissing";
    public const string QuestZoneNotFound = "questZoneNotFound";
    /// <summary>서버 DB 의 탈출구·활성 환승인데 스냅샷(tarkov.dev)에 좌표가 없다. Detail = "map/key", 환승은 "map/transit id".</summary>
    public const string ExitPositionMissing = "exitPositionMissing";
}

public sealed record CatalogWarning(string? QuestId, string Code, string Detail);

public sealed record CatalogTrader(string Id, string Name, string? AvatarUrl, bool IsVanilla);

public sealed record Objective(
    string ConditionId, string ConditionType, string Text, double? TargetCount, string? TargetName, ObjectivePrep? Prep = null)
{
    /// <summary>
    /// 목표 위치(맵별 Unity 좌표). 좌표를 알 수 없는 조건이면 빈 목록. 레코드 동등성이 목록을 참조로 비교하므로
    /// 빈 값은 항상 공유 인스턴스(Array.Empty)를 쓴다 — LocationResolver 도 같은 인스턴스를 돌려준다.
    /// </summary>
    public IReadOnlyList<ObjectiveLocation> Locations { get; init; } = Array.Empty<ObjectiveLocation>();
}

/// <summary>Unity 월드 좌표 그대로. Y = 높이. SVG 투영은 브라우저가 한다.</summary>
public sealed record MapPoint(double X, double Y, double Z);

/// <summary>Map = SPT locations 폴더 키(소문자, 예: bigmap, sandbox_high).</summary>
public sealed record ObjectiveLocation(string Map, IReadOnlyList<MapPoint> Points)
{
    /// <summary>
    /// 구역 영역. InZone·LaunchFlare 조건에서만 채운다(08 스펙). 빈 값은 Objective.Locations 와 같은 이유로 Array.Empty.
    /// </summary>
    public IReadOnlyList<MapArea> Areas { get; init; } = Array.Empty<MapArea>();
}

/// <summary>
/// 지도 위 사각형 영역. Center.X·Z 는 영역 중심, Center.Y 는 존 위치의 높이(층 판정용). SizeX·SizeZ 는 월드 x·z 방향 전체 폭(m),
/// Yaw 는 수직축 회전(도, Unity 규약 — 위에서 볼 때 양수가 시계 방향). 바닐라는 축 정렬 Bounds 라 Yaw = 0.
/// MinY~MaxY 는 층 판정용 높이 범위다. 구역 처치는 상자 바닥~꼭대기(걸친 층 모두에서 보임), 신호탄은 리졸버가 바닥 + 1m
/// 한 점으로 줄인다(감지 상자가 땅에서 위로 솟아 있어서 중심 높이로 보면 위층이 된다 — 08 스펙 §0).
/// </summary>
public sealed record MapArea(MapPoint Center, double SizeX, double SizeZ, double Yaw, double MinY, double MaxY);

/// <summary>
/// 스냅샷의 잠긴 문 한 개. Type = 덤프의 컴포넌트 이름("Door" | "KeycardDoor"), 아이스브레이커의 열쇠 없는 문은
/// "Keypad"(KeyTpl 빈 문자열) | "Explosive"(KeyTpl = SZ-1 폭약) | "Hatch"(KeyTpl = 가스 토치)(11 스펙 §3).
/// Code = 고정 키패드 코드, 없으면 null.
/// </summary>
public sealed record SnapshotDoor(string KeyTpl, string Type, MapPoint Position, string? Code = null);

/// <summary>
/// 카탈로그에 싣는 잠긴 문. Kind = "door" | "keycard" | "keypad" | "explosive" | "hatch". KeyTpl 은 열쇠(폭파문·해치는 필요한
/// 아이템, 키패드는 빈 문자열)이고, 나중에 프로필 인벤토리와 대조해 보유 열쇠를 표시할 때의 키다(06 스펙 §4.3).
/// </summary>
public sealed record LockedDoor(string KeyTpl, string KeyName, string Kind, MapPoint Position, string? Code = null);

/// <summary>스냅샷의 탈출구(10 스펙 §1). Key = 서버 allExtracts 의 Name(게임 키), Name = tarkov.dev 영문 이름.</summary>
public sealed record SnapshotExit(string Key, string Name, MapPoint Position);

/// <summary>스냅샷의 환승 지점. Id = 서버 base.transits 의 id.</summary>
public sealed record SnapshotTransit(string Id, MapPoint Position);

public sealed record SnapshotExits(IReadOnlyList<SnapshotExit> Exits, IReadOnlyList<SnapshotTransit> Transits);

/// <summary>
/// 서버 DB allExtracts 한 행. 빌더가 SPT 타입을 모르게 CatalogService 가 옮겨 담는다. Requirement = PassageRequirement 이름
/// (None·TransferItem·ScavCooperation·WorldEvent·Reference·Train·Empty …, base.secretExits 는 합성 값 "Secret"),
/// ItemId = Exit.Id(화폐 tpl, "Alpinist" 등).
/// </summary>
public sealed record LocationExitRow(string Name, string Side, string Requirement, int Count, string? ItemId, string? Tip, double? Chance);

/// <summary>서버 DB base.transits 한 행. Target = 목적지 Location(대소문자 섞임, 예: TarkovStreets).</summary>
public sealed record LocationTransitRow(string Id, bool Active, string Target);

public sealed record LocationExits(IReadOnlyList<LocationExitRow> Exits, IReadOnlyList<LocationTransitRow> Transits);

/// <summary>
/// 카탈로그에 싣는 탈출구·환승(10 스펙 §2.2). Kind = "pmc" | "shared" | "scav" | "transit".
/// Requirement 는 로케일로 푼 조건 문구, 문구가 없는 조건은 RequirementKind("coop" | "train" | "secret" | "alpinist" | "switch")로 웹이 번역한다.
/// Chance 는 100 미만일 때만, Target 은 환승 목적지 맵 키(소문자). 환승의 Key 는 transit id, Name 은 빈 문자열.
/// </summary>
public sealed record MapExit(
    string Key, string Name, string Kind, MapPoint Position, string? Requirement, string? RequirementKind, int? Chance, string? Target);

public sealed record FailTrigger(string QuestId, IReadOnlyList<string> Statuses);

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
    /// <summary>
    /// IsVanilla=true 인데 어떤 모드의 퀘스트 DB 파일에 같은 ID 가 있으면 그 모드 폴더명(예: sptQuestLive 가 바닐라
    /// 퀘스트를 고쳐 덮어쓴 경우). 파일이 있다는 사실만 보며 내용이 바닐라와 실제로 다른지는 비교하지 않는다.
    /// </summary>
    public string? OverriddenBy { get; init; }
    public string? ImageUrl { get; init; }
    public int? MinLevel { get; init; }
    /// <summary>퀘스트가 묶인 맵의 표시 이름. "any" 이거나 이름을 못 찾으면 null.</summary>
    public string? Location { get; init; }
    /// <summary>
    /// 퀘스트가 묶인 맵의 키(locations 폴더 이름, 소문자, 예: bigmap). 언어와 무관해 목표의 Prep.MapKeys 와 대조할 수 있다.
    /// Location(표시 이름)은 "&lt;id&gt; Name" 로케일이라 목표 쪽 맵 이름과 다를 수 있다(해안가 ↔ 해안선). any·알 수 없으면 null.
    /// </summary>
    public string? LocationKey { get; init; }
    public required IReadOnlyList<Requirement> Requirements { get; init; }
    public required IReadOnlyList<string> Prerequisites { get; init; }
    /// <summary>빌더가 전체 순회 후 채운다. ID 오름차순.</summary>
    public List<string> Unlocks { get; } = [];
    /// <summary>conditions.Fail 의 Quest 조건 — 이 퀘스트들이 Statuses 가 되면 이 퀘스트는 실패한다(배타 분기). 대상 ID 오름차순.</summary>
    public IReadOnlyList<FailTrigger> FailsWhen { get; init; } = [];
    public required IReadOnlyList<Objective> Objectives { get; init; }
    public required QuestRewards Rewards { get; init; }
    /// <summary>"isolated" | "traderInternal". 빌더가 Unlocks 확정 후 채운다.</summary>
    public List<string> Tags { get; } = [];
}

/// <summary>핸드북 최상위 카테고리. 이름은 게임 로케일에 번역이 없어(kr 도 영어) 프론트 i18n 이 Id 로 붙인다.</summary>
public sealed record CatalogItemCategory(string Id, string? IconUrl);

public sealed record Catalog(
    string SptVersion,
    string ModVersion,
    DateTimeOffset GeneratedAt,
    string Lang,
    SortedDictionary<string, CatalogTrader> Traders,
    SortedDictionary<string, CatalogQuest> Quests,
    SortedDictionary<string, List<string>> RewardIndex,
    IReadOnlyList<CatalogWarning> Warnings,
    /// 제출·설치 아이템이 속한 최상위 카테고리만, 인게임 필터 순서대로.
    IReadOnlyList<CatalogItemCategory> ItemCategories,
    /// 제출·설치 아이템 tpl → 최상위 카테고리 Id. 핸드북에 없는 아이템은 빠진다.
    SortedDictionary<string, string> ItemCategoryOf,
    /// <summary>map 키 → 잠긴 문. 퀘스트와 무관하게 맵마다 한 번만 싣는다(위치정보 팝업의 잠긴 문 토글).</summary>
    SortedDictionary<string, List<LockedDoor>> LockedDoors,
    /// <summary>map 키 → 활성 지도 변형 ID(맵 교체 모드가 로드됐을 때만, 예: interchange → manimal). 웹이 maps/index.json 의 변형 폴더를 고른다.</summary>
    SortedDictionary<string, string> MapVariants,
    /// <summary>map 키 → 탈출구·환승. 잠긴 문처럼 맵마다 한 번만 싣는다(지도의 탈출구 토글).</summary>
    SortedDictionary<string, List<MapExit>> Exits,
    /// <summary>아이템 tpl → 주로 나오는 곳(컨테이너·봇). Collector 제출 아이템만(12 kappa-loot-sources 스펙).</summary>
    SortedDictionary<string, LootSource> LootSources);

/// <summary>Containers = 하나 열었을 때 들어 있을 확률 높은 순, Bots = 봇 묶음 키(scav·pmc·boss·raider·cultist·other).</summary>
public sealed record LootSource(IReadOnlyList<LootContainer> Containers, IReadOnlyList<string> Bots);

/// <summary>Chance = 그 컨테이너 하나를 열었을 때 아이템이 하나 이상 들어 있을 확률(0~1, 맵 개수 가중 평균).</summary>
public sealed record LootContainer(string Tpl, string Name, double Chance);
