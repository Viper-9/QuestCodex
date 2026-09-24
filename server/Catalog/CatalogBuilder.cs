using QuestCodex.Catalog.Models;
using QuestCodex.Catalog.Prep;
using QuestCodex.Catalog.Requirements;
using QuestCodex.Catalog.Rewards;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

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

        if (input.VanillaQuestIds is null)
        {
            warnings.Add(new CatalogWarning(null, WarningCodes.VanillaSnapshotMissing, "Data/vanilla-quest-ids.json not loaded; isVanilla is false for all quests"));
        }
        else if (input.VanillaSnapshotSptVersion is not null && input.VanillaSnapshotSptVersion != input.SptVersion)
        {
            warnings.Add(new CatalogWarning(null, WarningCodes.VanillaSnapshotMismatch, $"snapshot {input.VanillaSnapshotSptVersion} vs server {input.SptVersion}"));
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
                quests[questId] = BuildQuest(questId, kv.Value, input, locale, rewardParser, prepParser, traders, warnings);
            }
            catch (Exception ex)
            {
                warnings.Add(new CatalogWarning(questId, WarningCodes.BuildFailed, $"{ex.GetType().Name}: {ex.Message}"));
            }
        }

        LinkUnlocksAndTags(quests);

        var rewardIndex = BuildRewardIndex(quests);

        return new Models.Catalog(input.SptVersion, input.ModVersion, now, input.Lang, traders, quests, rewardIndex, warnings);
    }

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
        SortedDictionary<string, CatalogTrader> traders,
        List<CatalogWarning> warnings)
    {
        var name = locale.Resolve($"{questId} name", questId, out var nameFellBack);
        var description = locale.Resolve($"{questId} description", questId, out var descFellBack);
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

        var objectives = (quest.Conditions.AvailableForFinish ?? [])
            .Select(c => BuildObjective(c, locale, warnings, questId) with { Prep = prepParser.Parse(c) })
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
            Requirements = requirements,
            Prerequisites = prerequisites,
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

    /// <summary>quest.location 은 맵 MongoId(로케일 "<id> Name") 또는 "any"/"marathon" 같은 비지도 값이다.</summary>
    private static string? ResolveLocation(string? location, LocaleResolver locale)
        => string.IsNullOrWhiteSpace(location) || location == "any" ? null : locale.TryResolve($"{location} Name");

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
