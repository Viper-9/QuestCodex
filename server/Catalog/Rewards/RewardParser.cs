using QuestCodex.Catalog.Models;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;

namespace QuestCodex.Catalog.Rewards;

/// <summary>
/// SPT Reward → CatalogReward. 함정: AssortmentUnlock 의 Target 은 미리보기용 합성 id 라 무시하고
/// Items[0].Template 을 쓴다. ProductionScheme 의 TraderId 는 상인이 아니라 HideoutAreas 정수다.
/// </summary>
public sealed class RewardParser(
    ItemCategorizer categorizer,
    LocaleResolver locale,
    IReadOnlyDictionary<MongoId, TemplateItem> items)
{
    /// <summary>SPT ImageRouter 가 서빙하는 핸드북 아이콘 경로. 실제 경로는 수동 체크리스트에서 실측 후 필요 시 수정.</summary>
    public static string IconUrl(string tpl) => $"/files/handbook/{tpl}.png";

    public CatalogReward Parse(Reward r, List<CatalogWarning> warnings, string questId)
    {
        try
        {
            return r.Type switch
            {
                RewardType.Item => ParseItem(r, warnings, questId),
                RewardType.AssortmentUnlock => ParseAssortUnlock(r, warnings, questId),
                RewardType.ProductionScheme => ParseProduction(r, warnings, questId),
                RewardType.TraderUnlock => new TraderUnlockReward(r.Target ?? ""),
                RewardType.TraderStanding => new TraderStandingReward(r.Target ?? "", r.Value ?? 0),
                RewardType.Experience => new ExperienceReward(r.Value ?? 0),
                RewardType.Skill => new SkillReward(r.Target ?? "", r.Value ?? 0),
                RewardType.Achievement => new AchievementReward(r.Target ?? ""),
                null => new OtherReward("unknown"),
                _ => new OtherReward(r.Type.Value.ToString()),
            };
        }
        catch (Exception ex)
        {
            warnings.Add(new CatalogWarning(questId, WarningCodes.UnparsedReward, $"{r.Type} {r.Id}: {ex.GetType().Name}: {ex.Message}"));
            return new OtherReward(r.Type?.ToString() ?? "unknown");
        }
    }

    private CatalogReward ParseItem(Reward r, List<CatalogWarning> warnings, string questId)
    {
        var tpl = FirstTpl(r);
        if (tpl is null)
        {
            warnings.Add(new CatalogWarning(questId, WarningCodes.EmptyRewardItems, $"Item reward {r.Id} has no items"));
            return new OtherReward("Item");
        }

        return new ItemReward(tpl, NameOf(tpl), IconUrl(tpl), r.Value ?? 1, categorizer.Categorize(tpl));
    }

    private CatalogReward ParseAssortUnlock(Reward r, List<CatalogWarning> warnings, string questId)
    {
        var tpl = FirstTpl(r);
        if (tpl is null)
        {
            warnings.Add(new CatalogWarning(questId, WarningCodes.EmptyRewardItems, $"AssortmentUnlock reward {r.Id} has no items"));
        }

        var traderId = r.TraderId?.String ?? r.TraderId?.Int?.ToString() ?? "";
        return new AssortUnlockReward(
            traderId,
            tpl,
            tpl is null ? null : NameOf(tpl),
            tpl is null ? null : IconUrl(tpl),
            r.LoyaltyLevel ?? 1,
            tpl is null ? [] : categorizer.Categorize(tpl));
    }

    private CatalogReward ParseProduction(Reward r, List<CatalogWarning> warnings, string questId)
    {
        var area = r.TraderId?.Int
                   ?? (int.TryParse(r.TraderId?.String, out var parsed) ? parsed : -1);
        var tpl = FirstTpl(r);
        if (tpl is null)
        {
            warnings.Add(new CatalogWarning(questId, WarningCodes.EmptyRewardItems, $"ProductionScheme reward {r.Id} has no items"));
        }

        return new ProductionReward(area, tpl, tpl is null ? null : NameOf(tpl), tpl is null ? null : IconUrl(tpl));
    }

    private static string? FirstTpl(Reward r)
        => r.Items is { Count: > 0 } list ? list[0].Template.ToString() : null;

    private string NameOf(string tpl)
    {
        var fromLocale = locale.TryResolve($"{tpl} Name");
        if (fromLocale is not null) return fromLocale;
        if (MongoId.IsValidMongoId(tpl) && items.TryGetValue(new MongoId(tpl), out var template) && !string.IsNullOrWhiteSpace(template.Name))
        {
            return template.Name;
        }

        return tpl;
    }
}
