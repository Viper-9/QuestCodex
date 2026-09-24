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
/// </summary>
public sealed record ObjectivePrep(
    IReadOnlyList<string> Maps,
    PrepItem? Item,
    IReadOnlyList<ItemRef> Weapons,
    IReadOnlyList<string> Calibers,
    IReadOnlyList<IReadOnlyList<ItemRef>> WeaponMods,
    IReadOnlyList<IReadOnlyList<IReadOnlyList<ItemRef>>> Equipment,
    IReadOnlyList<ItemRef> ForbiddenEquipment,
    bool OneRaid,
    IReadOnlyList<string> ExitStatuses,
    string? ExitName);
