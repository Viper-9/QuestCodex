using QuestCodex.Catalog.Models;

namespace QuestCodex.Briefing.Models;

// 브리핑 응답 형식(schemaVersion 1). Briefing 모드가 이 JSON 을 그대로 읽는다.
// 필드를 추가하는 건 괜찮지만 이름이나 뜻을 바꾸면 BriefingBuilder.SchemaVersion 을 올린다.

public sealed record BriefingResponse(
    int SchemaVersion,
    string QuestCodexVersion,
    DateTimeOffset GeneratedAt,
    string Lang,
    SortedDictionary<string, BriefingTrader> Traders,
    IReadOnlyList<BriefingMap> Maps,
    BriefingAnywhere Anywhere);

public sealed record BriefingTrader(string Name);

// 맵 탭 하나. Key 는 변종을 묶은 맵 키, LocationIds 는 이 탭에 묶이는 로케이션 ID(소문자),
// Count 는 이 맵에서만 하는 남은 목표 수.
// MapFolder 는 /questcodex/maps/{MapFolder}/map.json 의 폴더 이름(맵 변형 반영). 지도가 없으면 null.
public sealed record BriefingMap(
    string Key,
    IReadOnlyList<string> LocationIds,
    string Name,
    int Count,
    bool HasMap,
    string? MapFolder,
    IReadOnlyList<BriefingQuest> Quests,
    IReadOnlyList<BriefingItemRow> Bring,
    IReadOnlyList<BriefingFindRow> Find,
    IReadOnlyList<BriefingGear> Gear,
    IReadOnlyList<BriefingSpecial> Special,
    IReadOnlyList<MapExit> Exits,
    IReadOnlyList<LockedDoor> LockedDoors);

// Marker 는 지도 번호. 좌표가 있는 퀘스트에만 1부터 붙고 나머지는 null. Current, Target 은 QuestLines 에서 계산한다.
public sealed record BriefingQuest(
    string QuestId, string TraderId, string Name, int? Marker, string Summary, double? Current, double? Target,
    IReadOnlyList<BriefingObjective> Objectives);

// Locations 는 월드 좌표 그대로. 맵 탭 안에서는 그 탭 것만 하나로 합치고 Map 을 탭 키로 둔다.
public sealed record BriefingObjective(
    string ConditionId, string Text, double Current, double? Target, bool InferredMap, IReadOnlyList<ObjectiveLocation> Locations);

public sealed record BriefingSource(string QuestId, double Count);

// Fir 는 FIR 이어야 하는 수가 있다는 뜻. 그럴 때 Have 는 FIR 로 가진 수만 센다(웹 표시와 같다).
// Missing 은 RaidNeeds.Missing 으로 계산한 부족한 수.
public record BriefingItemRow(
    IReadOnlyList<ItemRef> Items, double Need, double Have, bool Fir, double Missing, IReadOnlyList<BriefingSource> Sources);

// Place 는 "here", "unknown", "elsewhere" 중 하나.
public sealed record BriefingFindRow(
    IReadOnlyList<ItemRef> Items, double Need, double Have, bool Fir, double Missing, IReadOnlyList<BriefingSource> Sources, string Place)
    : BriefingItemRow(Items, Need, Have, Fir, Missing, Sources);

// Rows 의 Label 은 build, weapon, caliber, weaponMod, equipment, forbidden 중 하나.
// build 줄은 무기와 필수 부착물을 한 총에 묶은 것이라 같은 목표의 weapon·caliber·weaponMod 줄을 대신한다(RaidRules.GearRow 참고).
public sealed record BriefingGear(string QuestId, IReadOnlyList<GearRow> Rows);

public sealed record BriefingSpecial(string QuestId, IReadOnlyList<SpecialRow> Rows);

// 아무 맵에서나 되거나 맵을 모르는 것. 맵마다 넣지 않고 응답에 한 번만 들어간다. 여기 퀘스트는 Marker 가 항상 null.
public sealed record BriefingAnywhere(
    IReadOnlyList<BriefingQuest> Quests, IReadOnlyList<BriefingItemRow> Bring, IReadOnlyList<BriefingGear> Gear, IReadOnlyList<BriefingSpecial> Special);
