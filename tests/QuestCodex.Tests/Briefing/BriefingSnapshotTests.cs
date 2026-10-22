using System.Text.Encodings.Web;
using System.Text.Json;
using QuestCodex.Briefing;
using QuestCodex.Briefing.Models;
using QuestCodex.Catalog.Models;
using QuestCodex.Progress.Models;
using QuestCodex.Web;
using CatalogModel = QuestCodex.Catalog.Models.Catalog;

namespace QuestCodex.Tests.Briefing;

// 바닐라 녹화 데이터(web/fixtures/vanilla, gitignore 대상)로 브리핑 응답을 만들어 스냅샷과 비교한다.
// 녹화가 없으면 VanillaSmokeTests 처럼 그냥 넘어간다. 스냅샷이 없으면 새로 만들고 통과시킨다.
// QUESTCODEX_UPDATE_SNAPSHOTS=1 이면 스냅샷을 다시 쓰는데, 응답 형식을 일부러 바꿨을 때만 쓰고 그 전에 diff 를 꼭 확인한다.
public class BriefingSnapshotTests
{
    private static readonly string? FixtureDir = TestPaths.RepoRoot is { } root ? Path.Combine(root, "web", "fixtures", "vanilla") : null;

    private static readonly JsonSerializerOptions ReadJson = new(JsonSerializerDefaults.Web);

    // 응답과 같은 옵션에 들여쓰기와 한글 그대로 출력만 더해서 사람이 읽기 쉽게 한다.
    private static readonly JsonSerializerOptions SnapshotJson = new(QuestCodexJson.Options)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static BriefingResponse? Load(string profileId, string lang)
    {
        if (FixtureDir is null) return null;
        var catalogPath = Path.Combine(FixtureDir, "catalog", $"{lang}.json");
        var progressPath = Path.Combine(FixtureDir, "progress", $"{profileId}.json");
        if (!File.Exists(catalogPath) || !File.Exists(progressPath)) return null;

        var catalog = JsonSerializer.Deserialize<CatalogModel>(File.ReadAllText(catalogPath), ReadJson)!;
        // 역직렬화된 SortedDictionary 는 문화권 기준으로 정렬돼서 서버처럼 ordinal 로 다시 만든다. 순서가 결과에 영향을 준다.
        catalog = catalog with
        {
            Quests = new SortedDictionary<string, CatalogQuest>(catalog.Quests, StringComparer.Ordinal),
            Exits = new SortedDictionary<string, List<MapExit>>(catalog.Exits, StringComparer.Ordinal),
            LockedDoors = new SortedDictionary<string, List<LockedDoor>>(catalog.LockedDoors, StringComparer.Ordinal),
            Traders = new SortedDictionary<string, CatalogTrader>(catalog.Traders, StringComparer.Ordinal),
        };
        var progress = JsonSerializer.Deserialize<ProfileProgress>(File.ReadAllText(progressPath), ReadJson)!;
        var index = MapIndex.Parse(File.ReadAllText(TestPaths.MapIndexJson));
        return BriefingBuilder.Build(catalog, progress, index, "test", DateTimeOffset.UnixEpoch);
    }

    [Theory]
    [InlineData("6a8328d2c1ae327dd00378cf", "kr")]
    [InlineData("6a8328d2c1ae327dd00378cf", "en")]
    [InlineData("6ac3d8a02594d2788836a657", "kr")]
    public void Vanilla_fixture_matches_snapshot(string profileId, string lang)
    {
        var briefing = Load(profileId, lang);
        if (briefing is null) return; // 녹화 없으면 건너뜀

        var json = JsonSerializer.Serialize(briefing, SnapshotJson).ReplaceLineEndings("\n") + "\n";
        var path = Path.Combine(FixtureDir!, "briefing", $"{profileId}.{lang}.json");
        if (!File.Exists(path) || Environment.GetEnvironmentVariable("QUESTCODEX_UPDATE_SNAPSHOTS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, json);
            return;
        }
        Assert.Equal(File.ReadAllText(path).ReplaceLineEndings("\n"), json);
    }

    // 스냅샷과 따로, 실제 데이터에서 항상 지켜져야 하는 것들을 확인한다. 스냅샷을 다시 써도 이건 통과해야 한다.
    [Fact]
    public void Vanilla_fixture_keeps_contract_invariants()
    {
        var r = Load("6a8328d2c1ae327dd00378cf", "kr");
        if (r is null) return; // 녹화 없으면 건너뜀

        Assert.Equal(1, r.SchemaVersion);
        Assert.Equal(RaidPlan.MapOrder, r.Maps.Take(RaidPlan.MapOrder.Count).Select(m => m.Key));
        Assert.Contains("factory4_night", r.Maps.Single(m => m.Key == "factory4_day").LocationIds);
        Assert.Contains("sandbox_high", r.Maps.Single(m => m.Key == "sandbox").LocationIds);
        Assert.Equal("세관", r.Maps.Single(m => m.Key == "bigmap").Name);
        Assert.Contains(r.Maps, m => m.Quests.Count > 0);
        foreach (var m in r.Maps)
        {
            Assert.Contains(m.Key, m.LocationIds);
            Assert.Equal(m.HasMap, m.MapFolder is not null);
            var markers = m.Quests.Where(q => q.Marker is not null).Select(q => q.Marker!.Value).ToList();
            Assert.Equal(Enumerable.Range(1, markers.Count), markers);           // 1부터 빈틈없이, 목록 순서대로
            Assert.All(m.Quests.Where(q => q.Marker is not null), q => Assert.Contains(q.Objectives, o => o.Locations.Count > 0));
            Assert.All(m.Quests.SelectMany(q => q.Objectives).SelectMany(o => o.Locations), l => Assert.Equal(m.Key, l.Map));
            Assert.All(m.Gear.SelectMany(g => g.Rows), row => Assert.Equal(row.Names.Count, row.Tpls.Count));
            Assert.Equal(m.Count, m.Quests.Sum(q => q.Objectives.Count));
        }
        Assert.All(r.Anywhere.Quests, q => Assert.Null(q.Marker));

        // 맨 위 목록은 바닐라 기준 전체 집합이고, 퀘스트마다 붙은 bool 과 어긋나지 않는다.
        Assert.Equal(251, r.KappaQuests.Count);
        Assert.Equal(100, r.LightkeeperQuests.Count);
        Assert.Equal(r.KappaQuests.Order(StringComparer.Ordinal), r.KappaQuests);
        Assert.Equal(r.LightkeeperQuests.Order(StringComparer.Ordinal), r.LightkeeperQuests);
        foreach (var q in r.Maps.SelectMany(m => m.Quests).Concat(r.Anywhere.Quests))
        {
            Assert.Equal(r.KappaQuests.Contains(q.QuestId), q.Kappa);
            Assert.Equal(r.LightkeeperQuests.Contains(q.QuestId), q.Lightkeeper);
        }
        var size = JsonSerializer.Serialize(r, QuestCodexJson.Options).Length;
        Assert.True(size < 1_000_000, $"briefing is {size} chars");             // 응답은 수백 KB 안쪽이어야 한다
    }
}
