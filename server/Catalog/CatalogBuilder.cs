using QuestCodex.Catalog.Locations;
using QuestCodex.Catalog.Models;
using QuestCodex.Catalog.Prep;
using QuestCodex.Catalog.Requirements;
using QuestCodex.Catalog.Rewards;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;

namespace QuestCodex.Catalog;

/// <summary>
/// CatalogInput → Catalog. 순수 함수. 퀘스트 하나가 실패해도 그 퀘스트만 빼고 계속한다.
/// </summary>
public static class CatalogBuilder
{
    /// <summary>SPT 4.1 동봉 상인. Prapor, Therapist, Fence, Skier, Peacekeeper, Mechanic, Ragman, Jaeger, Lightkeeper, Ref, BTR, Arena.</summary>
    public static readonly IReadOnlySet<string> VanillaTraderIds = new HashSet<string>(StringComparer.Ordinal)
    {
        "54cb50c76803fa8b248b4571", "54cb57776803fa99248b456e", "579dc571d53a0658a154fbec", "58330581ace78e27b8b10cee",
        "5935c25fb3acc3127c3d8cd9", "5a7c2eca46aef81a7ca2145d", "5ac3b934156ae10c4430e83c", "5c0647fdd443bc2504c2d371",
        "638f541a29ffd1183d187f57", "656f0f98d80a697f855d34b1", "6617beeaa9cfa777ca915b7c", "6864e812f9fe664cb8b8e152",
    };

    public static readonly string[] RewardIndexKeys =
        ["weapon", "armor", "headwear", "rig", "backpack", "key", "ammo", "assortUnlock", "production", "traderUnlock"];

    public static Models.Catalog Build(CatalogInput input, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(input);

        var warnings = new List<CatalogWarning>();
        var locale = new LocaleResolver(input.Locale, input.FallbackLocale);
        var categorizer = new ItemCategorizer(input.Items);
        var rewardParser = new RewardParser(categorizer, locale, input.Items);
        var prepParser = new PrepParser(locale, rewardParser.NameOf);
        var empty = new PointTableBuilder().Build();
        var locationResolver = new LocationResolver(
            input.QuestZones ?? empty, input.QuestItemSpawns ?? empty, input.LocationKeys ?? new Dictionary<string, string>(), input.Items,
            input.QuestZoneAreas);

        if (input.VanillaQuestIds is null)
        {
            warnings.Add(new CatalogWarning(null, WarningCodes.VanillaSnapshotMissing, "Data/vanilla-quest-ids.json not loaded; isVanilla is false for all quests"));
        }
        else if (input.VanillaSnapshotSptVersion is not null && input.VanillaSnapshotSptVersion != input.SptVersion)
        {
            warnings.Add(new CatalogWarning(null, WarningCodes.VanillaSnapshotMismatch, $"snapshot {input.VanillaSnapshotSptVersion} vs server {input.SptVersion}"));
        }

        if (input.QuestZoneSnapshotMissing)
        {
            warnings.Add(new CatalogWarning(null, WarningCodes.QuestZoneSnapshotMissing, "Data/quest-zones.json not loaded; only mod zones and quest items have locations"));
        }

        warnings.AddRange(input.ModQuestScanWarnings ?? []);

        var traders = new SortedDictionary<string, CatalogTrader>(StringComparer.Ordinal);
        foreach (var kv in input.Traders)
        {
            traders[kv.Key] = BuildTrader(kv.Key, kv.Value, locale, input.AvatarIsServable);
        }

        var quests = new SortedDictionary<string, CatalogQuest>(StringComparer.Ordinal);
        foreach (var kv in input.Quests)
        {
            var questId = kv.Key.ToString();
            try
            {
                quests[questId] = BuildQuest(questId, kv.Value, input, locale, rewardParser, prepParser, locationResolver, traders, warnings);
            }
            catch (Exception ex)
            {
                warnings.Add(new CatalogWarning(questId, WarningCodes.BuildFailed, $"{ex.GetType().Name}: {ex.Message}"));
            }
        }

        LinkUnlocksAndTags(quests);

        var rewardIndex = BuildRewardIndex(quests);

        var (itemCategories, itemCategoryOf) = BuildItemCategories(quests, input);
        var lockedDoors = BuildLockedDoors(input.LockedDoors, rewardParser.NameOf);

        return new Models.Catalog(input.SptVersion, input.ModVersion, now, input.Lang, traders, quests, rewardIndex, warnings, itemCategories, itemCategoryOf, lockedDoors,
            new SortedDictionary<string, string>(input.MapVariants?.ToDictionary() ?? [], StringComparer.Ordinal));
    }

    /// <summary>스냅샷 문에 열쇠 이름을 붙인다. 이름은 보상 아이템과 같은 규칙(로케일 → 템플릿 이름 → tpl).</summary>
    private static SortedDictionary<string, List<LockedDoor>> BuildLockedDoors(
        IReadOnlyDictionary<string, IReadOnlyList<SnapshotDoor>>? doors, Func<string, string> nameOf)
    {
        var result = new SortedDictionary<string, List<LockedDoor>>(StringComparer.Ordinal);
        foreach (var (map, list) in doors ?? new Dictionary<string, IReadOnlyList<SnapshotDoor>>())
        {
            result[map] = list
                .Select(d => new LockedDoor(d.KeyTpl, nameOf(d.KeyTpl), d.Type == "KeycardDoor" ? "keycard" : "door", d.Position))
                .ToList();
        }

        return result;
    }

    /// <summary>
    /// 핸드북 최상위 카테고리의 표시 순서. 무기, 무기 부품, 탄약, 장비, 방탄판, 의료품, 식량, 물물교환, 정보, 열쇠, 지도, 특수 장비, 퀘스트 아이템, 화폐.
    /// 여기 없는 카테고리(모드)는 뒤에 Id 순으로 붙는다.
    /// </summary>
    public static readonly string[] ItemCategoryOrder =
    [
        "5b5f78dc86f77409407a7f8e", "5b5f71a686f77447ed5636ab", "5b47574386f77428ca22b346", "5b47574386f77428ca22b33f",
        "6564b96a189fe36f356d177c", "5b47574386f77428ca22b344", "5b47574386f77428ca22b340", "5b47574386f77428ca22b33e",
        "5b47574386f77428ca22b341", "5b47574386f77428ca22b342", "5b47574386f77428ca22b343", "5b47574386f77428ca22b345",
        "5b619f1a86f77450a702a6f3", "5b5f78b786f77447ed5636af",
    ];

    /// <summary>제출·설치 아이템과 해금 보상(판매 해금·제작법) tpl 마다 핸드북 부모를 끝까지 따라 올라가 최상위 카테고리를 찾는다.</summary>
    private static (IReadOnlyList<CatalogItemCategory>, SortedDictionary<string, string>) BuildItemCategories(
        SortedDictionary<string, CatalogQuest> quests, CatalogInput input)
    {
        var categoryOf = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (input.HandbookCategories is not { } cats || input.HandbookItemParents is not { } parents) return ([], categoryOf);

        string? TopOf(string categoryId)
        {
            // 부모 고리가 꼬여 있어도 멈추도록 깊이를 제한한다.
            for (var depth = 0; depth < 32 && cats.TryGetValue(categoryId, out var c); depth++)
            {
                if (string.IsNullOrEmpty(c.ParentId)) return categoryId;
                categoryId = c.ParentId;
            }

            return null;
        }

        var unlockTpls = quests.Values
            .SelectMany(q => q.Rewards.Started.Concat(q.Rewards.Success).Concat(q.Rewards.Fail))
            .Select(r => r switch { AssortUnlockReward a => a.Tpl, ProductionReward p => p.Tpl, _ => null })
            .OfType<string>();
        var tpls = quests.Values
            .SelectMany(q => q.Objectives)
            .SelectMany(o => o.Prep?.Item?.Items ?? [])
            .Select(i => i.Tpl)
            .Concat(unlockTpls);
        foreach (var tpl in tpls)
        {
            if (categoryOf.ContainsKey(tpl) || !parents.TryGetValue(tpl, out var parent)) continue;
            if (TopOf(parent) is { } top) categoryOf[tpl] = top;
        }

        var order = ItemCategoryOrder.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i, StringComparer.Ordinal);
        var list = categoryOf.Values
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => order.GetValueOrDefault(id, int.MaxValue))
            .ThenBy(id => id, StringComparer.Ordinal)
            .Select(id => new CatalogItemCategory(id, cats[id].Icon is { Length: > 0 } icon && IsIconServable(icon, input) ? icon : null))
            .ToList();
        return (list, categoryOf);
    }

    private static bool IsIconServable(string url, CatalogInput input) =>
        !url.StartsWith("/files/", StringComparison.OrdinalIgnoreCase) || input.AvatarIsServable is not { } servable || servable(url);

    private static CatalogTrader BuildTrader(MongoId id, TraderBase tb, LocaleResolver locale, Func<string, bool>? avatarIsServable)
    {
        var name = locale.TryResolve($"{id} Nickname")
                   ?? (string.IsNullOrWhiteSpace(tb.Nickname) ? id.ToString() : tb.Nickname);
        var avatar = string.IsNullOrWhiteSpace(tb.Avatar) ? null : tb.Avatar;

        // SPT 4.1.5 Storyteller 처럼 base.json 이 존재하지 않는 이미지를 가리키는 경우가 있다. 그대로 내보내면
        // 프론트가 로드 실패 후에야 이니셜로 폴백하고, 그 사이 요청마다 서버에 "처리되지 않은 응답" 에러가 남는다.
        // 라우트 판정은 SPT 이미지 라우터가 하므로 "/files/" 로 시작하는 URL 에만 적용한다(모드의 외부 URL 은 그대로 둔다).
        if (avatar is not null && avatarIsServable is not null
            && avatar.StartsWith("/files/", StringComparison.OrdinalIgnoreCase) && !avatarIsServable(avatar))
        {
            avatar = null;
        }

        return new CatalogTrader(id, name, avatar, VanillaTraderIds.Contains(id));
    }

    private static CatalogQuest BuildQuest(
        string questId,
        Quest quest,
        CatalogInput input,
        LocaleResolver locale,
        RewardParser rewardParser,
        PrepParser prepParser,
        LocationResolver locationResolver,
        SortedDictionary<string, CatalogTrader> traders,
        List<CatalogWarning> warnings)
    {
        // 템플릿의 name/description 필드가 로케일 키다(게임 클라이언트와 같은 규칙). 바닐라는 "<id> name" 이라 같지만
        // Painter 모드는 "painter_1 name" 처럼 자기 키를 쓴다. 로케일 어디에도 없으면 모드가 적어 둔 QuestName, 그다음 ID.
        var name = locale.Resolve([quest.Name, $"{questId} name"], NonBlank(quest.QuestName) ?? questId, out var nameFellBack);
        var description = locale.Resolve([quest.Description, $"{questId} description"], questId, out var descFellBack);
        if (nameFellBack || descFellBack)
        {
            warnings.Add(new CatalogWarning(questId, WarningCodes.MissingLocale, $"name/description missing in '{input.Lang}'"));
        }

        var traderId = quest.TraderId.ToString();
        if (!traders.ContainsKey(traderId))
        {
            traders[traderId] = new CatalogTrader(traderId, $"Unknown ({traderId})", null, false);
            warnings.Add(new CatalogWarning(questId, WarningCodes.UnknownTrader, $"trader {traderId} not in trader table"));
        }

        var factionOnly = input.BearOnly.Contains(quest.Id) ? "bear"
            : input.UsecOnly.Contains(quest.Id) ? "usec"
            : null;

        var requirements = (quest.Conditions.AvailableForStart ?? [])
            .Select(c => RequirementParser.Parse(c, locale, input.Quests, warnings, questId))
            .ToList();

        var prerequisites = requirements.OfType<QuestRequirement>().Select(r => r.QuestId).Distinct().ToList();
        var minLevel = requirements.OfType<LevelRequirement>().Select(r => (int?)Math.Round(r.Value)).Min();

        var questMap = locationResolver.QuestMap(quest.Location);
        var objectives = (quest.Conditions.AvailableForFinish ?? [])
            .Select(c => BuildObjective(c, locale, warnings, questId) with
            {
                Prep = prepParser.Parse(c),
                Locations = locationResolver.Resolve(c, questMap, warnings, questId),
            })
            .ToList();

        var rewards = new QuestRewards(
            ParseRewards(quest, "Started", rewardParser, warnings, questId),
            ParseRewards(quest, "Success", rewardParser, warnings, questId),
            ParseRewards(quest, "Fail", rewardParser, warnings, questId));

        return new CatalogQuest
        {
            Id = questId,
            Name = name,
            Description = description,
            TraderId = traderId,
            Side = quest.Side,
            FactionOnly = factionOnly,
            IsVanilla = input.VanillaQuestIds?.Contains(questId) ?? false,
            ModName = input.VanillaQuestIds?.Contains(questId) == true ? null : input.ModQuestOrigins?.GetValueOrDefault(questId),
            ImageUrl = string.IsNullOrWhiteSpace(quest.Image) ? null : quest.Image,
            MinLevel = minLevel,
            Location = ResolveLocation(quest.Location, locale),
            LocationKey = LocationKeyOf(quest.Location, input.LocationKeys),
            Requirements = requirements,
            Prerequisites = prerequisites,
            FailsWhen = ParseFailTriggers(quest),
            Objectives = objectives,
            Rewards = rewards,
        };
    }

    /// <summary>
    /// 조건 로케일이 없으면 Text 를 비우고 대상 아이템 이름만 실어 보낸다. 표시 문구 조립은 프론트(i18n) 몫이라
    /// 여기서 ConditionType 을 Text 에 넣지 않는다. 모드가 조건만 추가하고 로케일을 빼먹는 경우를 잡아낸다.
    /// </summary>
    private static Objective BuildObjective(QuestCondition c, LocaleResolver locale, List<CatalogWarning> warnings, string questId)
    {
        var condId = c.Id.ToString();
        var text = locale.TryResolve(condId);
        if (text is not null) return new Objective(condId, c.ConditionType, text, c.Value, null);

        warnings.Add(new CatalogWarning(questId, WarningCodes.MissingLocale, $"condition {condId} ({c.ConditionType}) has no locale"));
        var tpl = RequirementParser.TargetOf(c);
        var targetName = tpl is null ? null : locale.TryResolve($"{tpl} Name");
        return new Objective(condId, c.ConditionType, "", c.Value, targetName);
    }

    /// <summary>
    /// conditions.Fail 중 Quest 조건만. SPT 데이터에 같은 대상이 중복으로 들어 있는 경우가 있어(Ref 분기) 대상별로 상태를 합친다.
    /// 나머지 Fail 조건(CounterCreator 등)은 레이드 중 실패 조건이라 분기 정보가 아니다.
    /// </summary>
    private static List<FailTrigger> ParseFailTriggers(Quest quest)
        => (quest.Conditions.Fail ?? [])
            .Where(c => c.ConditionType == "Quest")
            .Select(c => (Target: RequirementParser.TargetOf(c), Statuses: c.Status is { Count: > 0 }
                ? c.Status.Select(s => s.ToString())
                : [QuestStatusEnum.Success.ToString()]))
            .Where(x => x.Target is not null)
            .GroupBy(x => x.Target!, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new FailTrigger(g.Key, g.SelectMany(x => x.Statuses).Distinct().ToList()))
            .ToList();

    /// <summary>quest.location 은 맵 MongoId(로케일 "<id> Name") 또는 "any"/"marathon" 같은 비지도 값이다.</summary>
    private static string? ResolveLocation(string? location, LocaleResolver locale)
        => string.IsNullOrWhiteSpace(location) || location == "any" ? null : locale.TryResolve($"{location} Name");

    /// <summary>
    /// quest.location → 맵 키. 로케이션 표에 있는 ID 면 그 키, 표에 없는 ID(Transition 같은 가상 로케이션)는 null,
    /// 그 밖의 문자열(모드가 맵 키를 직접 넣은 경우)은 소문자. any·marathon·빈 값은 null.
    /// </summary>
    private static string? LocationKeyOf(string? location, IReadOnlyDictionary<string, string>? locationKeys)
    {
        if (string.IsNullOrWhiteSpace(location)) return null;
        if (location.Equals("any", StringComparison.OrdinalIgnoreCase) || location.Equals("marathon", StringComparison.OrdinalIgnoreCase)) return null;
        if (locationKeys is not null && locationKeys.TryGetValue(location, out var key)) return key;
        return IsObjectId(location) ? null : location.ToLowerInvariant();
    }

    private static string? NonBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static bool IsObjectId(string s) => s.Length == 24 && s.All(Uri.IsHexDigit);

    private static List<CatalogReward> ParseRewards(Quest quest, string phase, RewardParser parser, List<CatalogWarning> warnings, string questId)
    {
        if (quest.Rewards is null || !quest.Rewards.TryGetValue(phase, out var list) || list is null) return [];
        return list.Select(r => parser.Parse(r, warnings, questId)).ToList();
    }

    private static void LinkUnlocksAndTags(SortedDictionary<string, CatalogQuest> quests)
    {
        foreach (var q in quests.Values)
        {
            foreach (var prereq in q.Prerequisites)
            {
                if (quests.TryGetValue(prereq, out var parent)) parent.Unlocks.Add(q.Id);
            }
        }

        foreach (var q in quests.Values)
        {
            q.Unlocks.Sort(StringComparer.Ordinal);

            if (q.Prerequisites.Count == 0 && q.Unlocks.Count == 0)
            {
                q.Tags.Add("isolated");
                continue;
            }

            var neighbours = q.Prerequisites.Concat(q.Unlocks)
                .Select(id => quests.TryGetValue(id, out var n) ? n.TraderId : null)
                .ToList();
            if (neighbours.All(t => t == q.TraderId))
            {
                q.Tags.Add("traderInternal");
            }
        }
    }

    private static SortedDictionary<string, List<string>> BuildRewardIndex(SortedDictionary<string, CatalogQuest> quests)
    {
        var index = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var key in RewardIndexKeys) index[key] = [];

        foreach (var q in quests.Values)
        {
            var buckets = new HashSet<string>();
            foreach (var reward in q.Rewards.Success)
            {
                switch (reward)
                {
                    case ItemReward item: buckets.Add(item.Categories[0]); break;
                    case AssortUnlockReward au:
                        buckets.Add("assortUnlock");
                        if (au.Categories.Count > 0) buckets.Add(au.Categories[0]);
                        break;
                    case ProductionReward: buckets.Add("production"); break;
                    case TraderUnlockReward: buckets.Add("traderUnlock"); break;
                }
            }

            foreach (var b in buckets)
            {
                if (index.TryGetValue(b, out var list)) list.Add(q.Id);
            }
        }

        return index;
    }
}
