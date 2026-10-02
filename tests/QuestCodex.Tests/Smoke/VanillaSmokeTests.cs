using System.Text.Json;
using QuestCodex.Catalog;
using QuestCodex.Catalog.Locations;
using QuestCodex.Catalog.Models;
using QuestCodex.Services;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Json;
using Path = System.IO.Path;

namespace QuestCodex.Tests.Smoke;

/// <summary>
/// 실제 SPT_Data 로 전체 바닐라 카탈로그를 빌드한다. QUESTCODEX_SPT_DATA(기본 F:\SPT4.1.2\SPT_Runtime\SPT_Data\database)가 없으면 건너뛴다.
///
/// 역직렬화 노트(스펙 §6.2): System.Text.Json 기본 옵션으로는 SPTarkov.Server.Core.Models.Common.MongoId 를
/// 역직렬화할 수 없다 — MongoId 구조체 자체에는 [JsonConverter] 어트리뷰트가 없고(ilspycmd 확인 완료),
/// 대신 컨버터(StringToMongoIdConverter 등 9종)는 SptJsonConverterRegistrator.GetJsonConverters() 가 생산해
/// SPTarkov.Server.Core.Utils.JsonUtil 생성자에 주입되는 방식이다. SptJsonConverterRegistrator 는 (DI 컨테이너 없이도)
/// 매개변수 없는 public 생성자를 가지므로, DI 없이 `new JsonUtil([new SptJsonConverterRegistrator()])` 로
/// 동일한 JsonSerializerOptions 를 손으로 구성할 수 있다 — 브리프 폴백 절차의 2단계에 해당.
/// </summary>
public class VanillaSmokeTests
{
    private static readonly string DataDir =
        Environment.GetEnvironmentVariable("QUESTCODEX_SPT_DATA") ?? @"F:\SPT4.1.2\SPT_Runtime\SPT_Data\database";

    private static readonly JsonUtil SptJson = new([new SptJsonConverterRegistrator()]);

    [Fact]
    public void Full_vanilla_catalog_builds_without_failures()
    {
        var questsPath = Path.Combine(DataDir, "templates", "quests.json");
        if (!File.Exists(questsPath))
        {
            return; // skip: no local SPT install
        }

        var quests = Deserialize<Dictionary<MongoId, Quest>>(questsPath);
        var items = Deserialize<Dictionary<MongoId, TemplateItem>>(Path.Combine(DataDir, "templates", "items.json"));
        var en = Deserialize<Dictionary<string, string>>(Path.Combine(DataDir, "locales", "global", "en.json"));
        var traders = Directory.GetDirectories(Path.Combine(DataDir, "traders"))
            .Select(dir => Deserialize<TraderBase>(Path.Combine(dir, "base.json")))
            .ToDictionary(t => t.Id);

        var input = new CatalogInput("en", "4.1.5", "test", quests, traders, items, en, en,
            new HashSet<MongoId>(), new HashSet<MongoId>(), quests.Keys.Select(k => k.ToString()).ToHashSet(), "4.1.5");

        var catalog = CatalogBuilder.Build(input, DateTimeOffset.UtcNow);

        Assert.True(catalog.Quests.Count > 300, $"only {catalog.Quests.Count} quests");
        Assert.DoesNotContain(catalog.Warnings, w => w.Code == WarningCodes.BuildFailed);

        var danglingPrereq = catalog.Warnings.Where(w => w.Code == WarningCodes.DanglingPrereq).ToList();
        Assert.True(danglingPrereq.Count == 0,
            $"{danglingPrereq.Count} dangling prereqs: {string.Join(", ", danglingPrereq.Select(w => $"{w.QuestId}: {w.Detail}"))}");

        var missingLocale = catalog.Warnings.Count(w => w.Code == WarningCodes.MissingLocale);
        Assert.True(missingLocale < catalog.Quests.Count * 0.05, $"{missingLocale} quests missing locale");
        Assert.All(catalog.Quests.Values, q => Assert.True(q.IsVanilla));
        Assert.Contains(catalog.RewardIndex["weapon"], _ => true);

        // 준비물: 바닐라 퍼니셔 파트 4 는 12게이지 산탄총 목록과 등대 제한을 가진다
        var punisher4 = catalog.Quests["59ca264786f77445a80ed044"];
        Assert.Equal("Lighthouse", punisher4.Location);
        Assert.True(punisher4.Objectives[0].Prep?.Weapons.Count >= 10);
        Assert.Contains(punisher4.Objectives, o => o.Prep?.Item is { Action: "handover", FoundInRaid: true });
    }

    /// <summary>
    /// 위치정보(스펙 §6·§7): 동봉 존 스냅샷 + 실제 looseLoot + 로케이션 base.json 으로 빌드해서 대표 퀘스트의 좌표를 확인한다.
    /// 존이 덤프에 없는 11개(스냅샷 README 의 남은 존)는 questZoneNotFound 로 남으므로 경고 수는 검사하지 않는다.
    /// </summary>
    [Fact]
    public void Vanilla_catalog_has_quest_locations()
    {
        var questsPath = Path.Combine(DataDir, "templates", "quests.json");
        if (!File.Exists(questsPath))
        {
            return; // skip: no local SPT install
        }

        var quests = Deserialize<Dictionary<MongoId, Quest>>(questsPath);
        var items = Deserialize<Dictionary<MongoId, TemplateItem>>(Path.Combine(DataDir, "templates", "items.json"));
        var en = Deserialize<Dictionary<string, string>>(Path.Combine(DataDir, "locales", "global", "en.json"));

        var keys = new Dictionary<string, string>(StringComparer.Ordinal);
        var loots = new List<(string, LooseLoot?)>();
        foreach (var dir in Directory.GetDirectories(Path.Combine(DataDir, "locations")))
        {
            var basePath = Path.Combine(dir, "base.json");
            if (!File.Exists(basePath)) continue;
            using var doc = JsonDocument.Parse(File.ReadAllText(basePath));
            if (!doc.RootElement.TryGetProperty("Id", out var id) || !doc.RootElement.TryGetProperty("_Id", out var mongoId)) continue;
            var map = id.GetString()!.ToLowerInvariant();
            keys[mongoId.GetString()!] = map;
            var lootPath = Path.Combine(dir, "looseLoot.json");
            if (File.Exists(lootPath)) loots.Add((map, Deserialize<LooseLoot>(lootPath)));
        }

        var snapshot = QuestZoneSnapshot.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "quest-zones.json")));
        var input = new CatalogInput("en", "4.1.5", "test", quests, new Dictionary<MongoId, TraderBase>(), items, en, en,
            new HashSet<MongoId>(), new HashSet<MongoId>(), null, null,
            QuestZones: snapshot.Zones, QuestItemSpawns: LooseLootSpawns.Forced(loots), LocationKeys: keys, LockedDoors: snapshot.Doors,
            QuestZoneAreas: snapshot.Areas);

        var catalog = CatalogBuilder.Build(input, DateTimeOffset.UtcNow);

        // BP 연료 확보(Customs): 목표 4개 모두 bigmap 좌표, Reserve 의 fuel4 복사본은 규칙 1 로 빠진다
        var bpDepot = catalog.Quests["59c124d686f774189b3c843f"].Objectives;
        Assert.Equal(4, bpDepot.Count);
        Assert.All(bpDepot, o => Assert.Equal("bigmap", Assert.Single(o.Locations).Map));

        // 광신도 - 파트 2(any): 규칙 2 로 세 맵
        var cult2Maps = catalog.Quests["5a27ba1c86f77461ea5a3c56"].Objectives.SelectMany(o => o.Locations).Select(l => l.Map).ToHashSet();
        Assert.Equal(new HashSet<string> { "bigmap", "woods", "shoreline" }, cult2Maps);

        // Beneath The Streets(marathon): Labs
        var beneath = catalog.Quests["66aba85403e0ee3101042877"].Objectives.SelectMany(o => o.Locations).ToList();
        Assert.Contains(beneath, l => l.Map == "laboratory");

        // 보급 계획: FindItem 퀘스트 아이템 → looseLoot 강제 스폰
        var supplyPlans = catalog.Quests["596a0e1686f7741ddf17dbee"].Objectives.First(o => o.ConditionType == "FindItem");
        Assert.NotEmpty(supplyPlans.Locations);

        // 못 찾은 존은 tools/zone-dump/dumps/README.md 의 "남은 존" 목록과 같아야 한다. 새로 빠지는 존이 생기면(스냅샷 누락,
        // SPT 업데이트로 ID 변경) 여기서 드러난다. 목록을 줄였다면 README 와 이 기대값을 함께 고친다.
        var notFound = catalog.Warnings.Where(w => w.Code == WarningCodes.QuestZoneNotFound)
            .Select(w => w.Detail.Split('\'')[1]).ToHashSet();
        string[] remaining =
        [
            "bunker2", "Check_cinema", "Labs_transits", "1", // 미궁 7개는 2026-10-01 재덤프로 확보
        ];
        Assert.Equal(remaining.ToHashSet(), notFound);

        // 잠긴 문(06 스펙): 모든 문의 열쇠가 실제 아이템 이름으로 풀린다(tpl 그대로 남은 것 0개)
        var doors = catalog.LockedDoors.Values.SelectMany(d => d).ToList();
        Assert.Equal(34, catalog.LockedDoors["bigmap"].Count);
        Assert.All(doors, d => Assert.NotEqual(d.KeyTpl, d.KeyName));
        Assert.Contains(catalog.LockedDoors["laboratory"], d => d.Kind == "keycard");

        // 구역 영역(08 스펙): 구역 처치와 신호탄 목표는 영역을, 설치 목표(BP 연료 확보)는 영역 없이 점만
        var logging = catalog.Quests["67503219527c9a38e80496ae"].Objectives.SelectMany(o => o.Locations).ToList();
        Assert.Contains(logging, l => l.Map == "woods" && l.Areas.Any(a => a.SizeX == 500 && a.SizeZ == 1600));
        var payback = catalog.Quests["63966fd9ea19ac7ed845db30"].Objectives.SelectMany(o => o.Locations).ToList();
        Assert.Contains(payback, l => l.Areas.Count > 0);
        Assert.All(bpDepot.SelectMany(o => o.Locations), l => Assert.Empty(l.Areas));

        // 호텔 안뜰 신호탄(Huntsman Administrator): 감지 상자 중심 10.3m 가 아니라 바닥 + 1m 로 층을 정한다
        var huntsmanFlare = catalog.Quests["639136df4b15ca31f76bc31f"].Objectives
            .Single(o => o.ConditionId == "63aaccae87413d64ae079631").Locations.SelectMany(l => l.Areas).Single();
        Assert.True(huntsmanFlare.MaxY < 2, $"flare anchored at {huntsmanFlare.MinY}..{huntsmanFlare.MaxY}");
    }

    /// <summary>
    /// 탈출구(10 스펙 §4): 실제 DB(allExtracts·secretExits·base.transits) + 동봉 스냅샷(tarkov.dev, 등대는 덤프). 모든 탈출구가
    /// 좌표를 가져야 한다. 경고가 생기면 SPT 업데이트로 키가 바뀌었거나 스냅샷 데이터가 빠진 것이다.
    /// </summary>
    [Fact]
    public void Vanilla_exits_have_positions()
    {
        var questsPath = Path.Combine(DataDir, "templates", "quests.json");
        if (!File.Exists(questsPath))
        {
            return; // skip: no local SPT install
        }

        var en = Deserialize<Dictionary<string, string>>(Path.Combine(DataDir, "locales", "global", "en.json"));
        var db = new Dictionary<string, LocationExits>(StringComparer.Ordinal);
        foreach (var dir in Directory.GetDirectories(Path.Combine(DataDir, "locations")))
        {
            var extractsPath = Path.Combine(dir, "allExtracts.json");
            if (!File.Exists(extractsPath)) continue;
            using var extracts = JsonDocument.Parse(File.ReadAllText(extractsPath));
            using var baseJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "base.json")));
            var rows = extracts.RootElement.EnumerateArray().Select(e => new LocationExitRow(
                e.GetProperty("Name").GetString()!, e.GetProperty("Side").GetString()!, e.GetProperty("PassageRequirement").GetString()!,
                e.GetProperty("Count").GetInt32(), e.GetProperty("Id").GetString(), e.GetProperty("RequirementTip").GetString(),
                e.GetProperty("Chance").GetDouble())).ToList();
            if (baseJson.RootElement.TryGetProperty("secretExits", out var secrets))
            {
                foreach (var s in secrets.EnumerateArray())
                {
                    rows.Add(new LocationExitRow(s.GetProperty("Name").GetString()!, "Pmc", "Secret", 0, null, null, null));
                    rows.Add(new LocationExitRow(s.GetProperty("Name").GetString()!, "Scav", "Secret", 0, null, null, null));
                }
            }

            var transits = baseJson.RootElement.TryGetProperty("transits", out var t)
                ? t.EnumerateArray().Select(x => new LocationTransitRow(
                    x.GetProperty("id").GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture),
                    x.GetProperty("active").GetBoolean(), x.GetProperty("location").GetString()!)).ToList()
                : [];
            db[baseJson.RootElement.GetProperty("Id").GetString()!.ToLowerInvariant()] = new LocationExits(rows, transits);
        }

        var snapshot = QuestZoneSnapshot.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "quest-zones.json")));
        var warnings = new List<CatalogWarning>();
        var exits = ExitBuilder.Build(snapshot.Exits, db, new LocaleResolver(en, en), tpl => tpl, warnings);

        Assert.Empty(warnings.Select(w => w.Detail));
        // 등대는 덤프 좌표: 라이브에서 없어진 택시 V-Ex 가 있고, 이름은 build-snapshot.js 의 EXIT_NAMES
        Assert.Contains(exits["lighthouse"], e => e is { Key: " V-Ex_light", Name: "Road to Military Base V-Ex", Kind: "pmc", Chance: 50 });
        // 인터체인지: 공용 EMERCOM, PMC 전용 발전소(5,000 RUB, 50%), 협동은 공용, 환승 2개(세관·시내)
        var interchange = exits["interchange"];
        Assert.Contains(interchange, e => e is { Key: "SE Exfil", Kind: "shared", Name: "Emercom Checkpoint" });
        Assert.Contains(interchange, e => e is { Key: "PP Exfil", Kind: "pmc", Requirement: "Bring 5,000 RUB", Chance: 50 });
        Assert.Contains(interchange, e => e is { Key: "Interchange Cooperation", Kind: "shared", RequirementKind: "coop" });
        Assert.Equal(["bigmap", "tarkovstreets"], interchange.Where(e => e.Kind == "transit").Select(e => e.Target));
        Assert.Contains(exits["bigmap"], e => e.Kind == "scav");
        // 진영별로 키가 다른 같은 탈출구는 공용 하나로(해안선 Road to Customs)
        Assert.Single(exits["shoreline"], e => e.Name == "Road to Customs" && e.Kind == "shared");
        // 비밀 탈출구(base.secretExits): 삼림 "Railway Bridge to Tarkov"는 공용 + secret
        Assert.Contains(exits["woods"], e => e is { Key: "woods_secret_minefield", Kind: "shared", RequirementKind: "secret", Name: "Railway Bridge to Tarkov" });
    }

    private static T Deserialize<T>(string path)
    {
        try
        {
            return SptJson.Deserialize<T>(File.ReadAllText(path))
                   ?? throw new InvalidOperationException($"null from {path}");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"deserialize {Path.GetFileName(path)} failed: {ex.Message}", ex);
        }
    }
}
