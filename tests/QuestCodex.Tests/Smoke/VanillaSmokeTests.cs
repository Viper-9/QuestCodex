using System.Text.Json;
using QuestCodex.Catalog;
using QuestCodex.Catalog.Models;
using SPTarkov.Server.Core.Models.Common;
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
