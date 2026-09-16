using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;

namespace QuestCodex.Catalog.Rewards;

/// <summary>
/// TemplateItem.Parent 체인을 루트까지 올라가며 BaseClasses 상수와 대조해 카테고리 태그를 만든다.
/// 결과 [0] 은 대분류(rewardIndex 키), [1] 은 무기 세부 분류(있을 때만).
/// </summary>
public sealed class ItemCategorizer(IReadOnlyDictionary<MongoId, TemplateItem> items)
{
    public const string Unknown = "unknown";
    public const string Other = "other";

    private const int MaxDepth = 32;

    private static readonly (MongoId Id, string Tag)[] WeaponSub =
    [
        (BaseClasses.PISTOL, "pistol"), (BaseClasses.SMG, "smg"), (BaseClasses.ASSAULT_RIFLE, "assaultRifle"),
        (BaseClasses.ASSAULT_CARBINE, "assaultCarbine"), (BaseClasses.SHOTGUN, "shotgun"),
        (BaseClasses.SNIPER_RIFLE, "sniperRifle"), (BaseClasses.MARKSMAN_RIFLE, "marksmanRifle"),
        (BaseClasses.MACHINE_GUN, "machinegun"), (BaseClasses.GRENADE_LAUNCHER, "grenadeLauncher"),
        (BaseClasses.KNIFE, "melee"),
    ];

    // 순서 = 우선순위. 체인에서 먼저 만나는 것이 아니라 이 표에서 먼저 나오는 것이 이긴다.
    private static readonly (MongoId Id, string Tag)[] Major =
    [
        (BaseClasses.WEAPON, "weapon"), (BaseClasses.ARMOR, "armor"), (BaseClasses.ARMORED_EQUIPMENT, "armor"),
        (BaseClasses.HEADWEAR, "headwear"), (BaseClasses.VEST, "rig"), (BaseClasses.BACKPACK, "backpack"),
        (BaseClasses.KEY, "key"), (BaseClasses.AMMO, "ammo"), (BaseClasses.MEDS, "medical"),
        (BaseClasses.FOOD_DRINK, "food"), (BaseClasses.BARTER_ITEM, "barter"), (BaseClasses.MOD, "mod"),
        (BaseClasses.SIMPLE_CONTAINER, "container"),
    ];

    public IReadOnlyList<string> Categorize(string tpl)
    {
        if (!MongoId.IsValidMongoId(tpl)) return [Unknown];
        var id = new MongoId(tpl);
        if (!items.ContainsKey(id)) return [Unknown];

        var chain = new HashSet<MongoId>();
        var cursor = id;
        for (var depth = 0; depth < MaxDepth; depth++)
        {
            if (!items.TryGetValue(cursor, out var template) || !chain.Add(cursor)) break;
            cursor = template.Parent;
            if (cursor == default) break;
        }

        foreach (var (majorId, tag) in Major)
        {
            if (!chain.Contains(majorId)) continue;
            if (tag != "weapon") return [tag];

            foreach (var (subId, subTag) in WeaponSub)
            {
                if (chain.Contains(subId)) return ["weapon", subTag];
            }

            return ["weapon"];
        }

        return [Other];
    }
}
