using QuestCodex.Catalog.Loot;
using QuestCodex.Catalog.Models;
using QuestCodex.Services;

namespace QuestCodex.Tests.Services;

/// <summary>루트 데이터 디스크 캐시(13 catalog-cache 스펙 §3): 키 계산, 같은 키만 읽기, 망가진 파일은 없는 셈.</summary>
public sealed class LootDataCacheTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "qc-lootcache-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "cache", "loot-data.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static LootDataCache.KeyInput Input(params (string Guid, string Version)[] mods) =>
        new("4.1.5", "1.5.0", mods, new Dictionary<string, double> { ["bigmap"] = 1.0 }, new Dictionary<string, double> { ["bigmap"] = 2.0 });

    private static CachedLootData Sample() => new(
        new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<MapPoint>>>
        {
            ["bigmap"] = new Dictionary<string, IReadOnlyList<MapPoint>> { ["tplA"] = [new MapPoint(1.5, 2, -3.25)] },
        },
        new Dictionary<string, RawLootSource>
        {
            ["tplB"] = new([("containerX", 0.25), ("containerY", 0.1)], ["scav", "boss"]),
        });

    [Fact]
    public void Key_ignores_mod_order_but_changes_with_versions_and_multipliers()
    {
        var a = LootDataCache.Key(Input(("a.mod", "1.0.0"), ("b.mod", "2.0.0")));
        Assert.Equal(a, LootDataCache.Key(Input(("b.mod", "2.0.0"), ("a.mod", "1.0.0"))));
        Assert.NotEqual(a, LootDataCache.Key(Input(("a.mod", "1.0.1"), ("b.mod", "2.0.0"))));
        Assert.NotEqual(a, LootDataCache.Key(Input(("a.mod", "1.0.0"))));
        Assert.NotEqual(a, LootDataCache.Key(Input(("a.mod", "1.0.0"), ("b.mod", "2.0.0")) with { SptVersion = "4.1.6" }));
        Assert.NotEqual(a, LootDataCache.Key(Input(("a.mod", "1.0.0"), ("b.mod", "2.0.0")) with { ModVersion = "1.6.0" }));
        Assert.NotEqual(a, LootDataCache.Key(Input(("a.mod", "1.0.0"), ("b.mod", "2.0.0")) with
        {
            StaticLootMultiplier = new Dictionary<string, double> { ["bigmap"] = 1.5 },
        }));
    }

    [Fact]
    public void Round_trips_data_for_the_same_key()
    {
        LootDataCache.Write(FilePath, "k1", Sample());
        var read = LootDataCache.TryRead(FilePath, "k1");

        Assert.NotNull(read);
        Assert.Equal(new MapPoint(1.5, 2, -3.25), Assert.Single(read.QuestItemSpawns["bigmap"]["tplA"]));
        var source = read.LootSources["tplB"];
        Assert.Equal([("containerX", 0.25), ("containerY", 0.1)], source.Containers);
        Assert.Equal(["scav", "boss"], source.Bots);
    }

    [Fact]
    public void Different_key_or_missing_file_is_a_miss()
    {
        Assert.Null(LootDataCache.TryRead(FilePath, "k1"));
        LootDataCache.Write(FilePath, "k1", Sample());
        Assert.Null(LootDataCache.TryRead(FilePath, "k2"));
    }

    [Fact]
    public void Corrupt_file_is_a_miss()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, "{ not json");
        Assert.Null(LootDataCache.TryRead(FilePath, "k1"));
    }

    [Fact]
    public void Write_overwrites_and_delete_removes()
    {
        LootDataCache.Write(FilePath, "k1", Sample());
        LootDataCache.Write(FilePath, "k2", Sample());
        Assert.Null(LootDataCache.TryRead(FilePath, "k1"));
        Assert.NotNull(LootDataCache.TryRead(FilePath, "k2"));

        LootDataCache.Delete(FilePath);
        Assert.False(File.Exists(FilePath));
        LootDataCache.Delete(FilePath); // 없어도 예외 없음
    }
}
