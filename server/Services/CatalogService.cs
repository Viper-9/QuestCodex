using System.Collections.Concurrent;
using QuestCodex.Catalog;
using QuestCodex.Catalog.Locations;
using QuestCodex.Catalog.Loot;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Image;
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
    LocationTable locationTable,
    BotTable botTable,
    QuestConfig questConfig,
    LocationConfig locationConfig,
    LocaleService localeService,
    LocaleTable localeTable,
    VanillaSnapshot vanilla,
    ModQuestIndex modQuestIndex,
    QuestZoneSnapshot questZoneSnapshot,
    ModQuestZoneIndex modQuestZoneIndex,
    MapVariantDetector mapVariants,
    ImageRouterService imageRouterService,
    FileUtil fileUtil,
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

    // 위치 데이터는 언어와 무관하므로 서버 수명당 한 번만 만든다. looseLoot 는 SPT 가 LazyLoad 로 들고 있어서
    // .Value 를 읽을 때마다 파일을 다시 역직렬화한다(맵당 수 MB) — 그래서 여기서 한 번 뽑아 캐시한다.
    private readonly Lazy<LocationData> _locationData = new(
        () => LoadLocationData(questZoneSnapshot, modQuestZoneIndex, locationTable, logger),
        LazyThreadSafetyMode.ExecutionAndPublication);

    private sealed record LocationData(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<QuestCodex.Catalog.Models.MapPoint>>> Zones,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<QuestCodex.Catalog.Models.MapPoint>>> QuestItemSpawns,
        IReadOnlyDictionary<string, string> LocationKeys,
        bool SnapshotMissing,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<QuestCodex.Catalog.Models.MapArea>>> Areas,
        IReadOnlyDictionary<string, QuestCodex.Catalog.Models.LocationExits> Exits);

    /// <summary>
    /// 아이템 출처(컨테이너 루트 테이블 + 봇 인벤토리). 언어와 무관해 한 번만. staticLoot·staticContainers 도 LazyLoad 라
    /// 맵당 .Value 를 한 번만 읽는다.
    /// </summary>
    private readonly Lazy<IReadOnlyDictionary<string, RawLootSource>> _lootSources = new(
        () => LoadLootSources(locationTable, botTable, locationConfig, logger),
        LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>핸드북 카테고리·아이템 부모. 언어와 무관해 한 번만. 모드가 추가한 아이템도 첫 요청 시점이면 들어와 있다.</summary>
    private readonly Lazy<(IReadOnlyDictionary<string, HandbookCategoryInput> Categories, IReadOnlyDictionary<string, string> ItemParents)> _handbook = new(() =>
    {
        var categories = new Dictionary<string, HandbookCategoryInput>(StringComparer.Ordinal);
        var parents = new Dictionary<string, string>(StringComparer.Ordinal);
        var handbook = templateTable.Handbook;
        foreach (var c in handbook?.Categories ?? [])
        {
            if (c is null) continue;
            var parent = c.ParentId?.ToString();
            categories[c.Id.ToString()] = new HandbookCategoryInput(string.IsNullOrEmpty(parent) ? null : parent, c.Icon);
        }

        foreach (var i in handbook?.Items ?? [])
        {
            if (i is null) continue;
            parents[i.Id.ToString()] = i.ParentId.ToString();
        }

        return (categories, parents);
    }, LazyThreadSafetyMode.ExecutionAndPublication);

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
        var locations = _locationData.Value;

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
            ModQuestScanWarnings: modQuestIndex.Warnings,
            AvatarIsServable: IsAvatarServable,
            QuestZones: locations.Zones,
            QuestItemSpawns: locations.QuestItemSpawns,
            LocationKeys: locations.LocationKeys,
            QuestZoneSnapshotMissing: locations.SnapshotMissing,
            LockedDoors: questZoneSnapshot.Doors,
            HandbookCategories: _handbook.Value.Categories,
            HandbookItemParents: _handbook.Value.ItemParents,
            QuestZoneAreas: locations.Areas,
            MapVariants: mapVariants.Active,
            ExitPositions: questZoneSnapshot.Exits,
            LocationExits: locations.Exits,
            LootSources: _lootSources.Value);

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

    /// <summary>각 맵 staticLoot(컨테이너 종류 → 루트 테이블)·staticContainers(놓인 컨테이너) + 봇 인벤토리 → LootSourceIndex.</summary>
    private static IReadOnlyDictionary<string, RawLootSource> LoadLootSources(
        LocationTable locationTable, BotTable botTable, LocationConfig locationConfig, ISptLogger<CatalogService> logger)
    {
        var maps = new List<LootMapInput>();
        foreach (var location in locationTable.GetDictionary().Values)
        {
            if (location?.Base is null || string.IsNullOrWhiteSpace(location.Base.Id)) continue;
            var map = location.Base.Id.ToLowerInvariant();
            try
            {
                var loot = location.StaticLoot?.Value;
                var placedList = location.StaticContainers?.Value?.StaticContainers;
                if (loot is null || placedList is null) continue;
                var placed = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var c in placedList)
                {
                    var tpl = c?.Template?.Items?.FirstOrDefault()?.Template.ToString();
                    if (!string.IsNullOrEmpty(tpl)) placed[tpl] = placed.GetValueOrDefault(tpl) + 1;
                }

                var tables = new Dictionary<string, StaticLootTable>(StringComparer.Ordinal);
                foreach (var (container, details) in loot)
                {
                    if (details is null) continue;
                    tables[container.ToString()] = new StaticLootTable(
                        (details.ItemCountDistribution ?? []).Where(d => d is not null).Select(d => (d.Count ?? 0, (double)(d.RelativeProbability ?? 0))).ToList(),
                        (details.ItemDistribution ?? []).Where(d => d is not null).Select(d => (d.Tpl.ToString(), (double)(d.RelativeProbability ?? 0))).ToList());
                }

                var multiplier = locationConfig.StaticLootMultiplier?.GetValueOrDefault(map, 1.0) ?? 1.0;
                maps.Add(new LootMapInput(map, tables, placed, multiplier));
            }
            catch (Exception ex)
            {
                logger.Warning($"[QuestCodex] static loot of '{map}' unreadable, loot sources skipped: {ex.Message}");
            }
        }

        // 봇이 떨어뜨리는 것: 가방·주머니·조끼·특수 루트 + 장비 전 슬롯(근접무기 등). 보안 컨테이너는 떨어지지 않아 뺀다.
        var bots = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
        foreach (var (type, bot) in botTable.Types ?? [])
        {
            var inv = bot?.BotInventory;
            if (inv is null) continue;
            var tpls = new HashSet<string>(StringComparer.Ordinal);
            void AddPool(Dictionary<SPTarkov.Server.Core.Models.Common.MongoId, double>? pool)
            {
                foreach (var (tpl, weight) in pool ?? []) if (weight > 0) tpls.Add(tpl.ToString());
            }

            AddPool(inv.Items?.Backpack);
            AddPool(inv.Items?.Pockets);
            AddPool(inv.Items?.TacticalVest);
            AddPool(inv.Items?.SpecialLoot);
            if (inv.Equipment is not null) foreach (var slot in inv.Equipment.Values) AddPool(slot);
            bots[type] = tpls;
        }

        return LootSourceIndex.Build(maps, bots);
    }

    /// <summary>
    /// 존 = 동봉 스냅샷 + 모드 CustomQuestZones(같은 맵·ID 면 둘 다 점으로 남김). 퀘스트 아이템 = 각 맵 looseLoot 의
    /// 강제 스폰. 맵 키는 LocationBase.Id 소문자(= locations 폴더 이름, 예: Sandbox_high → sandbox_high).
    /// </summary>
    private static LocationData LoadLocationData(
        QuestZoneSnapshot questZoneSnapshot, ModQuestZoneIndex modQuestZoneIndex, LocationTable locationTable, ISptLogger<CatalogService> logger)
    {
        var zones = new PointTableBuilder().AddAll(questZoneSnapshot.Zones).AddAll(modQuestZoneIndex.Zones).Build();
        var areas = new AreaTableBuilder().AddAll(questZoneSnapshot.Areas).AddAll(modQuestZoneIndex.Areas).Build();

        var maps = locationTable.GetDictionary().Values
            .Where(l => l?.Base is not null && !string.IsNullOrWhiteSpace(l.Base.Id))
            .Select(l => (Map: l.Base.Id.ToLowerInvariant(), Location: l))
            .ToList();
        // 인덱서로 넣는다: hideout·develop 처럼 _Id 가 비어 있거나 겹치는 로케이션이 있어도 예외가 나지 않게.
        var keys = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (map, location) in maps) keys[location.Base.IdField.ToString()] = map;

        // 지연 열거: 맵 하나의 looseLoot 만 메모리에 두고 다음 맵으로 넘어간다.
        var spawns = LooseLootSpawns.Forced(maps.Select(m => (m.Map, ReadLooseLoot(m.Map, m.Location))));
        var exits = new Dictionary<string, QuestCodex.Catalog.Models.LocationExits>(StringComparer.Ordinal);
        foreach (var (map, location) in maps)
        {
            if (ReadExits(location) is { } e) exits[map] = e;
        }

        return new LocationData(zones, spawns, keys, questZoneSnapshot.Zones is null, areas, exits);

        // allExtracts 는 PMC·스캐브·협동 탈출구를 진영(Side)과 함께 담는다(모드가 바꾼 목록 포함). 없으면 base.exits(PMC 만).
        // 비밀 탈출구는 base.secretExits 에 따로 있다.
        static QuestCodex.Catalog.Models.LocationExits? ReadExits(SPTarkov.Server.Core.Models.Eft.Common.Location location)
        {
            IEnumerable<SPTarkov.Server.Core.Models.Eft.Common.Exit>? all = location.AllExtracts;
            if (all is null || !all.Any()) all = location.Base.Exits;
            var rows = (all ?? [])
                .Where(e => e is not null && !string.IsNullOrEmpty(e.Name))
                .Select(e => new QuestCodex.Catalog.Models.LocationExitRow(
                    e.Name!, string.IsNullOrEmpty(e.Side) ? "Pmc" : e.Side, e.PassageRequirement.ToString(), e.Count ?? 0,
                    string.IsNullOrEmpty(e.Id) ? null : e.Id, e.RequirementTip, e.Chance))
                .ToList();
            // 비밀 탈출구(base.secretExits)는 allExtracts 에 없다. 진영은 EligibleFor* 로, 조건은 합성 값 "Secret" 으로 싣는다.
            foreach (var s in location.Base.SecretExits ?? [])
            {
                if (s is null || string.IsNullOrEmpty(s.Name)) continue;
                if (s.EligibleForPMC ?? true) rows.Add(new QuestCodex.Catalog.Models.LocationExitRow(s.Name, "Pmc", "Secret", 0, null, null, null));
                if (s.EligibleForScav ?? true) rows.Add(new QuestCodex.Catalog.Models.LocationExitRow(s.Name, "Scav", "Secret", 0, null, null, null));
            }

            var transits = (location.Base.Transits ?? [])
                .Where(t => t?.Id is not null)
                .Select(t => new QuestCodex.Catalog.Models.LocationTransitRow(t.Id!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), t.IsActive ?? false, t.Location ?? ""))
                .ToList();
            return rows.Count == 0 && transits.Count == 0 ? null : new QuestCodex.Catalog.Models.LocationExits(rows, transits);
        }

        SPTarkov.Server.Core.Models.Eft.Common.LooseLoot? ReadLooseLoot(string map, SPTarkov.Server.Core.Models.Eft.Common.Location location)
        {
            try
            {
                return location.LooseLoot?.Value;
            }
            catch (Exception ex)
            {
                logger.Warning($"[QuestCodex] looseLoot of '{map}' unreadable, quest item locations skipped: {ex.Message}");
                return null;
            }
        }
    }

    /// <summary>
    /// 이미지 URL 을 SPT 가 실제로 서빙하는지. 키 규칙(확장자 제거 → URL 디코드 → 소문자)은 ImageRouter.CanHandle 과
    /// 같아야 하므로 SPT 의 FileUtil 을 그대로 쓴다. 라우트는 각 모드의 IOnLoad 에서 등록되고 카탈로그는 첫 요청
    /// 시점(모든 모드 로드 후)에 빌드되므로, 이 시점이면 모드 상인 아바타도 이미 등록돼 있다.
    /// </summary>
    private bool IsAvatarServable(string avatarUrl)
    {
        var key = Uri.UnescapeDataString(fileUtil.StripExtension(avatarUrl, keepPath: true)).ToLowerInvariant();
        return imageRouterService.ExistsByKey(key);
    }
}
