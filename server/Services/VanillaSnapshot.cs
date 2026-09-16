using System.Text.Json;
using SPTarkov.DI.Annotations;

namespace QuestCodex.Services;

/// <summary>
/// 모드에 동봉한 바닐라 퀘스트 ID 스냅샷. 없으면 QuestIds == null 이고 카탈로그가 경고를 낸다 (기동 실패 아님).
/// </summary>
[Injectable(InjectionType.Singleton)]
public class VanillaSnapshot
{
    private readonly Lazy<(string SptVersion, IReadOnlySet<string> QuestIds)?> _data = new(LoadFromModFolder);

    public string? SptVersion => _data.Value?.SptVersion;
    public IReadOnlySet<string>? QuestIds => _data.Value?.QuestIds;

    public static (string SptVersion, IReadOnlySet<string> QuestIds) Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var version = doc.RootElement.GetProperty("sptVersion").GetString()
                      ?? throw new InvalidOperationException("sptVersion is null");
        var ids = doc.RootElement.GetProperty("questIds").EnumerateArray()
            .Select(e => e.GetString() ?? throw new InvalidOperationException("questIds contains null"))
            .ToHashSet(StringComparer.Ordinal);
        return (version, ids);
    }

    public static (string SptVersion, IReadOnlySet<string> QuestIds)? LoadFromModFolder()
    {
        var modDir = Path.GetDirectoryName(typeof(VanillaSnapshot).Assembly.Location);
        if (modDir is null) return null;
        var path = Path.Combine(modDir, "Data", "vanilla-quest-ids.json");
        return File.Exists(path) ? Parse(File.ReadAllText(path)) : null;
    }
}
