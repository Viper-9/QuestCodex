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

        // iconUrl 은 항상 null: SPT 는 임의 아이템 tpl 의 아이콘 PNG 를 서빙하지 않는다(실서버 실측,
        // 2026-09-16 스펙 §3.2-10 갱신) — 바닐라·모드 아이템 모두 아이콘은 클라이언트 Unity 에셋 번들
        // 안에만 존재한다. 프론트는 categories 로 카테고리 아이콘을 대신 표시한다.
        return new ItemReward(tpl, NameOf(tpl), null, r.Value ?? 1, categorizer.Categorize(tpl));
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
            null,
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

        return new ProductionReward(area, tpl, tpl is null ? null : NameOf(tpl), null);
    }

    private static string? FirstTpl(Reward r)
        => r.Items is { Count: > 0 } list ? list[0].Template.ToString() : null;

    public string NameOf(string tpl)
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
