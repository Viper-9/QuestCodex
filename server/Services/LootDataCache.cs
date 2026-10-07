using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using QuestCodex.Catalog.Loot;
using QuestCodex.Catalog.Models;
using PointTable = System.Collections.Generic.IReadOnlyDictionary<string, System.Collections.Generic.IReadOnlyDictionary<string, System.Collections.Generic.IReadOnlyList<QuestCodex.Catalog.Models.MapPoint>>>;

namespace QuestCodex.Services;

/// <summary>looseLoot·staticLoot 를 읽어야 나오는 비싼 두 가지. 언어와 무관하다.</summary>
public sealed record CachedLootData(PointTable QuestItemSpawns, IReadOnlyDictionary<string, RawLootSource> LootSources);

/// <summary>
/// 루트 데이터 디스크 캐시(13 catalog-cache 스펙 §3). 키와 데이터를 한 파일에 담고, 키가 다르거나 파일이 망가졌으면
/// 없는 셈 친다. 모드 폴더 파일은 보지 않는다 — 시작마다 파일을 다시 쓰는 모드가 많아 키가 매번 달라지기 때문이다.
/// </summary>
public static class LootDataCache
{
    /// <summary>파일 구조나 추출 로직이 바뀌면 올린다(QuestCodex 버전도 키에 들어가지만 개발 중 빌드는 버전이 같다).</summary>
    public const int Format = 1;

    public sealed record KeyInput(
        string SptVersion,
        string ModVersion,
        IEnumerable<(string Guid, string Version)> Mods,
        IReadOnlyDictionary<string, double>? StaticLootMultiplier,
        IReadOnlyDictionary<string, double>? LooseLootMultiplier);

    public static string Key(KeyInput input)
    {
        var sb = new StringBuilder();
        sb.Append("format=").Append(Format).Append('\n');
        sb.Append("spt=").Append(input.SptVersion).Append('\n');
        sb.Append("questcodex=").Append(input.ModVersion).Append('\n');
        foreach (var (guid, version) in input.Mods.OrderBy(m => m.Guid, StringComparer.OrdinalIgnoreCase).ThenBy(m => m.Version, StringComparer.Ordinal))
        {
            sb.Append("mod=").Append(guid.ToLowerInvariant()).Append('@').Append(version).Append('\n');
        }

        AppendMultipliers("static", input.StaticLootMultiplier);
        AppendMultipliers("loose", input.LooseLootMultiplier);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();

        void AppendMultipliers(string name, IReadOnlyDictionary<string, double>? values)
        {
            foreach (var (map, value) in (values ?? new Dictionary<string, double>()).OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                sb.Append(name).Append(':').Append(map).Append('=').Append(value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
            }
        }
    }

    /// <summary>같은 형식·키면 데이터, 아니면(파일 없음·키 다름·읽기 실패) null.</summary>
    public static CachedLootData? TryRead(string path, string key)
    {
        if (!File.Exists(path)) return null;
        CacheFile? file;
        try
        {
            using var stream = File.OpenRead(path);
            file = JsonSerializer.Deserialize<CacheFile>(stream, JsonOptions);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }

        if (file is null || file.Format != Format || file.Key != key || file.QuestItemSpawns is null || file.LootSources is null) return null;

        PointTable spawns = file.QuestItemSpawns.ToDictionary(
            m => m.Key,
            m => (IReadOnlyDictionary<string, IReadOnlyList<MapPoint>>)m.Value.ToDictionary(
                t => t.Key, t => (IReadOnlyList<MapPoint>)t.Value, StringComparer.Ordinal),
            StringComparer.Ordinal);
        var sources = file.LootSources.ToDictionary(
            s => s.Key,
            s => new RawLootSource(s.Value.Containers.Select(c => (c.Tpl, c.Chance)).ToList(), s.Value.Bots),
            StringComparer.Ordinal);
        return new CachedLootData(spawns, sources);
    }

    /// <summary>임시 파일에 쓴 뒤 교체한다 — 쓰다가 서버가 꺼져도 반쪽 파일이 남지 않게.</summary>
    public static void Write(string path, string key, CachedLootData data)
    {
        var file = new CacheFile(
            Format,
            key,
            DateTimeOffset.UtcNow,
            data.QuestItemSpawns.ToDictionary(m => m.Key, m => m.Value.ToDictionary(t => t.Key, t => t.Value.ToList(), StringComparer.Ordinal), StringComparer.Ordinal),
            data.LootSources.ToDictionary(
                s => s.Key,
                s => new SourceEntry(s.Value.Containers.Select(c => new ContainerEntry(c.ContainerTpl, c.Chance)).ToList(), s.Value.Bots.ToList()),
                StringComparer.Ordinal));

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        using (var stream = File.Create(temp)) JsonSerializer.Serialize(stream, file, JsonOptions);
        File.Move(temp, path, overwrite: true);
    }

    public static void Delete(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record CacheFile(
        int Format,
        string Key,
        DateTimeOffset CreatedAt,
        Dictionary<string, Dictionary<string, List<MapPoint>>>? QuestItemSpawns,
        Dictionary<string, SourceEntry>? LootSources);

    private sealed record SourceEntry(List<ContainerEntry> Containers, List<string> Bots);

    private sealed record ContainerEntry(string Tpl, double Chance);
}
