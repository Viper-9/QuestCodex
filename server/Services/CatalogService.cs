using System.Collections.Concurrent;
using QuestCodex.Catalog;
using QuestCodex.Catalog.Locations;
using QuestCodex.Catalog.Loot;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Image;
using SPTarkov.Server.Core.Services.Locales;
using SPTarkov.Server.Core.Utils;

namespace QuestCodex.Services;

/// <summary>
/// SPT 테이블을 첫 요청 시점(모든 모드의 IOnLoad 완료 후)에 읽어 언어별로 한 번만 빌드하고 서버 수명 동안 캐시한다.
/// 비싼 루트 데이터는 디스크에도 캐시한다(<see cref="LootDataCache"/>). <see cref="Rebuild"/> 가 둘 다 새로 만들게 한다.
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
    IReadOnlyList<SptMod> loadedMods,
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

    // 언어와 무관한 데이터는 서버 수명당 한 번만 만든다(다시 만들기 버튼이 세대를 통째로 바꾼다). looseLoot·staticLoot 는
    // SPT 가 LazyLoad 로 들고 있어서 .Value 를 읽을 때마다 파일을 다시 역직렬화한다(맵당 수 MB, 14맵에 10초 가까이) —
    // 그래서 여기서 한 번 뽑고, 그 결과는 디스크에도 남겨 다음 서버 시작 때 재사용한다(13 catalog-cache 스펙).
    private Generation? _generation;

    private Generation Current
    {
        get
        {
            var g = Volatile.Read(ref _generation);
            if (g is not null) return g;
            Interlocked.CompareExchange(ref _generation, NewGeneration(readDiskCache: true), null);
            return Volatile.Read(ref _generation)!;
        }
    }

    /// <summary>
    /// 한 세대 = 언어별 카탈로그 + 그 재료. 다시 만들기 중에 진행 중이던 빌드는 옛 세대를 끝까지 쓰고, 새 요청은 새 세대를 쓴다.
    /// </summary>
    private sealed record Generation(
        ConcurrentDictionary<string, Lazy<QuestCodex.Catalog.Models.Catalog>> Catalogs,
        Lazy<LocationData> Locations,
        Lazy<CachedLootData> Loot,
        Lazy<(IReadOnlyDictionary<string, HandbookCategoryInput> Categories, IReadOnlyDictionary<string, string> ItemParents)> Handbook);

    private Generation NewGeneration(bool readDiskCache) => new(
        new ConcurrentDictionary<string, Lazy<QuestCodex.Catalog.Models.Catalog>>(StringComparer.Ordinal),
        new Lazy<LocationData>(() => LoadLocationData(questZoneSnapshot, modQuestZoneIndex, locationTable), LazyThreadSafetyMode.ExecutionAndPublication),
        new Lazy<CachedLootData>(() => LoadLootData(readDiskCache), LazyThreadSafetyMode.ExecutionAndPublication),
        new(LoadHandbook, LazyThreadSafetyMode.ExecutionAndPublication));

    private sealed record LocationData(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<QuestCodex.Catalog.Models.MapPoint>>> Zones,
        IReadOnlyDictionary<string, string> LocationKeys,
        bool SnapshotMissing,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<QuestCodex.Catalog.Models.MapArea>>> Areas,
        IReadOnlyDictionary<string, QuestCodex.Catalog.Models.LocationExits> Exits);

    /// <summary>디스크 캐시 파일. 모드 폴더 안이라 모드를 지우면 같이 사라진다.</summary>
    private static readonly string CachePath = Path.Combine(
        Path.GetDirectoryName(typeof(CatalogService).Assembly.Location) ?? ".", "cache", "loot-data.json");

    public QuestCodex.Catalog.Models.Catalog Get(string lang)
    {
        var generation = Current;
        var lazy = generation.Catalogs.GetOrAdd(lang, l => new Lazy<QuestCodex.Catalog.Models.Catalog>(() => Build(l, generation), LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            return lazy.Value;
        }
        catch
        {
            // Lazy 는 예외도 캐시하므로 다음 요청이 재시도할 수 있게 항목을 제거한다.
            generation.Catalogs.TryRemove(lang, out _);
            throw;
        }
    }

    /// <summary>
    /// 메모리 캐시를 통째로 버린다. 새 세대는 디스크 캐시를 읽지 않고 새로 만들어 덮어쓴다 — 키가 같아도(모드 설정만 바꾼 경우)
    /// 새 값이 되도록. 실제 빌드는 다음 Get 이 한다.
    /// </summary>
    public void Rebuild()
    {
        Volatile.Write(ref _generation, NewGeneration(readDiskCache: false));
        logger.Info("[QuestCodex] catalog data reset, it will be rebuilt on the next request");
    }

    private QuestCodex.Catalog.Models.Catalog Build(string lang, Generation generation)
    {
        var started = DateTimeOffset.UtcNow;
        var locale = localeService.GetLocaleDb(lang);
        var fallback = lang == FallbackLang ? locale : localeService.GetLocaleDb(FallbackLang);
        var locations = generation.Locations.Value;
        var loot = generation.Loot.Value;
        var handbook = generation.Handbook.Value;

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
            QuestItemSpawns: loot.QuestItemSpawns,
            LocationKeys: locations.LocationKeys,
            QuestZoneSnapshotMissing: locations.SnapshotMissing,
            LockedDoors: questZoneSnapshot.Doors,
            HandbookCategories: handbook.Categories,
            HandbookItemParents: handbook.ItemParents,
            QuestZoneAreas: locations.Areas,
            MapVariants: mapVariants.Active,
            ExitPositions: questZoneSnapshot.Exits,
            LocationExits: locations.Exits,
            LootSources: loot.LootSources);

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

    /// <summary>핸드북 카테고리·아이템 부모. 모드가 추가한 아이템도 첫 요청 시점이면 들어와 있다.</summary>
    private (IReadOnlyDictionary<string, HandbookCategoryInput> Categories, IReadOnlyDictionary<string, string> ItemParents) LoadHandbook()
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
    }

    /// <summary>
    /// 퀘스트 아이템 강제 스폰 + 루트 출처. 키(SPT·QuestCodex 버전, 로드된 모드 목록, 루트 배율)가 같은 디스크 캐시가 있으면
    /// 그것을 쓰고, 없으면 looseLoot·staticLoot 를 읽어 만든 뒤 남긴다. 캐시 읽기·쓰기 실패는 캐시 없이 동작한다.
    /// </summary>
    private CachedLootData LoadLootData(bool readDiskCache)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var key = LootDataCache.Key(new LootDataCache.KeyInput(
            ProgramStatics.SPT_VERSION().ToString(),
            ModMetadata.AssemblyVersion,
            loadedMods.Select(m => (m.ModMetadata.ModGuid ?? "", m.ModMetadata.Version?.ToString() ?? "")),
            locationConfig.StaticLootMultiplier,
            locationConfig.LooseLootMultiplier));

        if (readDiskCache && LootDataCache.TryRead(CachePath, key) is { } cached)
        {
            logger.Info($"[QuestCodex] loot data loaded from cache in {watch.ElapsedMilliseconds} ms");
            return cached;
        }

        var data = new CachedLootData(LoadQuestItemSpawns(locationTable, logger), LoadLootSources(locationTable, botTable, locationConfig, logger));
        try
        {
            LootDataCache.Write(CachePath, key, data);
        }
        catch (Exception ex)
        {
            logger.Warning($"[QuestCodex] loot data cache not saved: {ex.Message}");
        }

        logger.Info($"[QuestCodex] loot data built in {watch.ElapsedMilliseconds} ms (cached for the next server start)");
        return data;
    }

    /// <summary>각 맵 looseLoot 의 강제 스폰. 지연 열거라 맵 하나의 looseLoot 만 메모리에 두고 다음 맵으로 넘어간다.</summary>
    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<QuestCodex.Catalog.Models.MapPoint>>> LoadQuestItemSpawns(
        LocationTable locationTable, ISptLogger<CatalogService> logger)
    {
        var maps = locationTable.GetDictionary().Values
            .Where(l => l?.Base is not null && !string.IsNullOrWhiteSpace(l.Base.Id))
            .Select(l => (Map: l.Base.Id.ToLowerInvariant(), Location: l));
        return LooseLootSpawns.Forced(maps.Select(m => (m.Map, ReadLooseLoot(m.Map, m.Location))));

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
    /// 존 = 동봉 스냅샷 + 모드 CustomQuestZones(같은 맵·ID 면 둘 다 점으로 남김), 탈출구 = 각 맵 base.
    /// 맵 키는 LocationBase.Id 소문자(= locations 폴더 이름, 예: Sandbox_high → sandbox_high).
    /// </summary>
    private static LocationData LoadLocationData(
        QuestZoneSnapshot questZoneSnapshot, ModQuestZoneIndex modQuestZoneIndex, LocationTable locationTable)
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

        var exits = new Dictionary<string, QuestCodex.Catalog.Models.LocationExits>(StringComparer.Ordinal);
        foreach (var (map, location) in maps)
        {
            if (ReadExits(location) is { } e) exits[map] = e;
        }

        return new LocationData(zones, keys, questZoneSnapshot.Zones is null, areas, exits);

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
