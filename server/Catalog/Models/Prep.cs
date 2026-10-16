namespace QuestCodex.Catalog.Models;

/// <summary>아이템 참조. 로케일·템플릿 어디에도 이름이 없으면 Name == Tpl.</summary>
public sealed record ItemRef(string Tpl, string Name);

/// <summary>레이드에 챙겨가거나 제출해야 하는 아이템. Items 는 대안 목록(그중 하나면 된다).</summary>
public sealed record PrepItem(
    string Action,            // "handover" | "plant"
    IReadOnlyList<ItemRef> Items,
    double Count,
    bool FoundInRaid,
    double? MinDurability,    // 0 이면 제한 없음 → null
    double? MaxDurability,    // 100 이면 제한 없음 → null
    int? DogtagLevel,         // 0 이면 제한 없음 → null
    double? PlantSeconds);

/// <summary>
/// 목표 하나를 수행하기 전에 알아야 할 것. 준비할 게 없는 목표는 Objective.Prep 이 null 이다.
/// 장비 조건의 AND/OR 구조를 그대로 보존한다: Equipment[슬롯, AND][대안, OR][묶음, AND].
/// WeaponMods 는 [대안, OR][묶음, AND].
/// Builds 는 부착물 조건이 있는 Kills/Shots 하위 조건마다 하나. 같은 조건을 Weapons·Calibers·WeaponMods 에도 펼쳐 둔다.
/// </summary>
public sealed record ObjectivePrep(
    IReadOnlyList<string> Maps,
    /// 맵 키(조건 Target 소문자, 예: shoreline, factory4_night). Maps 는 표시 이름 기준 중복 제거라 길이가 다를 수 있다.
    IReadOnlyList<string> MapKeys,
    PrepItem? Item,
    IReadOnlyList<ItemRef> Weapons,
    IReadOnlyList<string> Calibers,
    IReadOnlyList<IReadOnlyList<ItemRef>> WeaponMods,
    IReadOnlyList<WeaponBuild> Builds,
    IReadOnlyList<IReadOnlyList<IReadOnlyList<ItemRef>>> Equipment,
    IReadOnlyList<ItemRef> ForbiddenEquipment,
    bool OneRaid,
    IReadOnlyList<string> ExitStatuses,
    string? ExitName);

// 게임은 하위 조건 하나의 무기·구경·부착물을 한 총에 묶어서 본다. 인정 무기 중 하나에 ModOptions 중 한 묶음이 달려 있어야 한다.
// Weapons·Calibers 가 비어 있으면 무기 제한이 없다는 뜻. ModOptions 는 [대안, OR][묶음, AND].
// 게임은 무기 tpl 과 부착물 tpl(중첩 슬롯 포함 전체 부품)을 정확히 맞춰 볼 뿐 부모 분류는 보지 않는다.
// ModParents 는 판정용이 아니라, 장착 총에 인정 목록 밖의 같은 종류 부품이 달렸는지 알려 주려고 둔 부모 분류(_parent) 목록.
public sealed record WeaponBuild(
    IReadOnlyList<ItemRef> Weapons,
    IReadOnlyList<string> Calibers,
    IReadOnlyList<IReadOnlyList<ItemRef>> ModOptions,
    IReadOnlyList<string> ModParents);
