using System.Collections.Concurrent;
using QuestCodex.Catalog;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Locales;
using SPTarkov.Server.Core.Utils;

namespace QuestCodex.Services;

/// <summary>
/// SPT 테이블을 첫 요청 시점(모든 모드의 IOnLoad 완료 후)에 읽어 언어별로 한 번만 빌드하고 서버 수명 동안 캐시한다.
/// </summary>
[Injectable(InjectionType.Singleton)]
public class CatalogService(
    TemplateTable templateTable,
    TradersTable tradersTable,
    QuestConfig questConfig,
    LocaleService localeService,
    LocaleTable localeTable,
    VanillaSnapshot vanilla,
    ModQuestIndex modQuestIndex,
    ISptLogger<CatalogService> logger) : ICatalogSource
{
    private const string FallbackLang = "en";

    // "Catalog" 는 QuestCodex.Catalog 네임스페이스와 이름이 겹쳐 using 만으로는 모호해지므로 전체 이름을 쓴다.
    private readonly ConcurrentDictionary<string, Lazy<QuestCodex.Catalog.Models.Catalog>> _cache = new(StringComparer.Ordinal);

    // 지원 언어 = 게임 텍스트 로케일(database/locales/global: en, kr, jp, ge …)의 키. LocaleService.GetServerSupportedLocales()
    // 는 서버 UI 로케일(ko, ja, de …) 목록이라 키 체계가 다르고, GetLocaleDb 는 모르는 키를 조용히 en 으로 폴백하므로
    // 그 목록으로 검증하면 "ko 는 200 인데 영어" 가 된다. 첫 요청 시점(모든 모드 로드 후)에 한 번 스냅샷.
    private readonly Lazy<IReadOnlySet<string>> _supportedLangs = new(
        () => localeTable.Global.Keys.ToHashSet(StringComparer.Ordinal),
        LazyThreadSafetyMode.ExecutionAndPublication);

    public IReadOnlySet<string> SupportedLangs => _supportedLangs.Value;

    public QuestCodex.Catalog.Models.Catalog Get(string lang)
    {
        var lazy = _cache.GetOrAdd(lang, l => new Lazy<QuestCodex.Catalog.Models.Catalog>(() => Build(l), LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            return lazy.Value;
        }
        catch
        {
            // Lazy 는 예외도 캐시하므로 다음 요청이 재시도할 수 있게 항목을 제거한다.
            _cache.TryRemove(lang, out _);
            throw;
        }
    }

    private QuestCodex.Catalog.Models.Catalog Build(string lang)
    {
        var started = DateTimeOffset.UtcNow;
        var locale = localeService.GetLocaleDb(lang);
        var fallback = lang == FallbackLang ? locale : localeService.GetLocaleDb(FallbackLang);

        var input = new CatalogInput(
            Lang: lang,
            SptVersion: ProgramStatics.SPT_VERSION().ToString(),
            ModVersion: ModMetadata.AssemblyVersion,
            Quests: templateTable.Quests,
            Traders: tradersTable.ToDictionary(kv => kv.Key, kv => kv.Value.Base),
            Items: templateTable.Items,
            Locale: locale,
            FallbackLocale: fallback,
            BearOnly: questConfig.BearOnlyQuests,
            UsecOnly: questConfig.UsecOnlyQuests,
            VanillaQuestIds: vanilla.QuestIds,
            VanillaSnapshotSptVersion: vanilla.SptVersion,
            ModQuestOrigins: modQuestIndex.QuestOrigins,
            ModQuestScanWarnings: modQuestIndex.Warnings);

        var catalog = CatalogBuilder.Build(input, started);

        var elapsed = DateTimeOffset.UtcNow - started;
        logger.Debug($"[QuestCodex] catalog '{lang}' built: {catalog.Quests.Count} quests, {catalog.Traders.Count} traders, {catalog.Warnings.Count} warnings in {elapsed.TotalMilliseconds:F0} ms");
        if (catalog.Warnings.Count > 0)
        {
            var byCode = catalog.Warnings.GroupBy(w => w.Code).Select(g => $"{g.Key}={g.Count()}");
            logger.Warning($"[QuestCodex] catalog '{lang}' warnings: {string.Join(", ", byCode)}");
        }

        return catalog;
    }
}
