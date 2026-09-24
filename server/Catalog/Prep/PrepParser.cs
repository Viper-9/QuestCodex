using QuestCodex.Catalog.Models;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace QuestCodex.Catalog.Prep;

/// <summary>
/// AvailableForFinish 조건 하나 → ObjectivePrep. 인게임 문장이 뭉뚱그리는 목록(인정 무기·장비 전체)을
/// 데이터에서 그대로 꺼낸다. FindItem 은 "레이드에서 찾기"라 챙겨갈 물건이 아니므로 다루지 않는다.
/// 모드가 런타임에 바꾼 조건도 메모리 테이블에서 읽으므로 그대로 반영된다.
/// </summary>
public sealed class PrepParser(LocaleResolver locale, Func<string, string> itemName)
{
    public ObjectivePrep? Parse(QuestCondition c)
    {
        var maps = new List<string>();
        var weapons = new List<ItemRef>();
        var calibers = new List<string>();
        var weaponMods = new List<IReadOnlyList<ItemRef>>();
        var equipment = new List<IReadOnlyList<IReadOnlyList<ItemRef>>>();
        var forbidden = new List<ItemRef>();
        var exitStatuses = new List<string>();
        string? exitName = null;

        foreach (var sub in c.Counter?.Conditions ?? [])
        {
            switch (sub.ConditionType)
            {
                case "Location":
                    foreach (var map in Targets(sub.Target)) AddDistinct(maps, locale.TryResolve(map) ?? map);
                    break;
                case "Kills":
                case "Shots":
                    foreach (var tpl in sub.Weapon ?? []) AddDistinct(weapons, Ref(tpl));
                    foreach (var cal in sub.WeaponCaliber ?? []) AddDistinct(calibers, cal);
                    weaponMods.AddRange(Options(sub.WeaponModsInclusive));
                    break;
                case "Equipment":
                    var slot = Options(sub.EquipmentInclusive);
                    if (slot.Count > 0) equipment.Add(slot);
                    foreach (var group in sub.EquipmentExclusive ?? [])
                    {
                        foreach (var tpl in group) AddDistinct(forbidden, Ref(tpl));
                    }

                    break;
                case "ExitStatus":
                    foreach (var s in sub.Status ?? []) AddDistinct(exitStatuses, s);
                    break;
                case "ExitName" when !string.IsNullOrWhiteSpace(sub.ExitName):
                    exitName = locale.TryResolve(sub.ExitName) ?? sub.ExitName;
                    break;
            }
        }

        var item = ParseItem(c);
        var oneRaid = c.OneSessionOnly == true;

        if (maps.Count == 0 && item is null && weapons.Count == 0 && calibers.Count == 0 && weaponMods.Count == 0
            && equipment.Count == 0 && forbidden.Count == 0 && !oneRaid && exitStatuses.Count == 0 && exitName is null)
        {
            return null;
        }

        return new ObjectivePrep(maps, item, weapons, calibers, weaponMods, equipment, forbidden, oneRaid, exitStatuses, exitName);
    }

    private PrepItem? ParseItem(QuestCondition c)
    {
        var action = c.ConditionType switch
        {
            "HandoverItem" => "handover",
            "LeaveItemAtLocation" or "PlaceBeacon" => "plant",
            _ => null,
        };
        if (action is null) return null;

        var items = Targets(c.Target).Select(Ref).ToList();
        if (items.Count == 0) return null;

        return new PrepItem(
            action,
            items,
            c.Value ?? 1,
            c.OnlyFoundInRaid == true,
            c.MinDurability is > 0 ? c.MinDurability : null,
            c.MaxDurability is < 100 ? c.MaxDurability : null,
            c.DogtagLevel is > 0 ? c.DogtagLevel : null,
            action == "plant" && c.PlantTime is > 0 ? c.PlantTime : null);
    }

    private ItemRef Ref(string tpl) => new(tpl, itemName(tpl));

    private List<IReadOnlyList<ItemRef>> Options(IEnumerable<List<string>>? options)
        => (options ?? [])
            .Where(group => group.Count > 0)
            .Select(group => (IReadOnlyList<ItemRef>)group.Select(Ref).ToList())
            .ToList();

    private static IEnumerable<string> Targets(SPTarkov.Server.Core.Utils.Json.ListOrT<string>? target)
    {
        if (target is null) return [];
        if (target.IsItem) return string.IsNullOrWhiteSpace(target.Item) ? [] : [target.Item];
        return target.List?.Where(t => !string.IsNullOrWhiteSpace(t)) ?? [];
    }

    private static void AddDistinct<T>(List<T> list, T value)
    {
        if (!list.Contains(value)) list.Add(value);
    }
}
