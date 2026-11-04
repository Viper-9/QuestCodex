using System.Text.Json;
using QuestCodex.Catalog;
using QuestCodex.Catalog.Models;
using SPTarkov.DI.Annotations;

namespace QuestCodex.Services;

/// <summary>
/// user/mods/*/db/QuestAlternativeConditionGroups.json 을 읽어 "이 중 하나만 하면 되는" 목표 묶음을 모은다(QuestBackport 가 쓰는 형식).
/// 폴더 이름은 사용자가 바꿀 수 있어 파일 이름으로 찾는다. 파일이 없으면 묶음도 없어서 카탈로그는 묶음을 전혀 쓰지 않는다.
/// 같은 폴더 db/Config.json 의 questContentEnabled 가 false 면 그 모드 서버도 묶음을 적용하지 않으므로 건너뛴다.
/// 바닐라 조건 ID 는 그대로라 카탈로그 대조로는 이 경우를 가릴 수 없다. 키나 파일이 없으면 그 모드 기본값대로 켜진 것으로 본다.
/// </summary>
[Injectable(InjectionType.Singleton)]
public class ModAlternativeGroups
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly Lazy<(IReadOnlyList<AlternativeGroup> Groups, IReadOnlyList<CatalogWarning> Warnings)> _data = new(Scan);

    public IReadOnlyList<AlternativeGroup> Groups => _data.Value.Groups;
    public IReadOnlyList<CatalogWarning> Warnings => _data.Value.Warnings;

    private static (IReadOnlyList<AlternativeGroup>, IReadOnlyList<CatalogWarning>) Scan()
    {
        var modDir = Path.GetDirectoryName(typeof(ModAlternativeGroups).Assembly.Location);
        var modsRoot = modDir is null ? null : Directory.GetParent(modDir)?.FullName;
        return modsRoot is null || !Directory.Exists(modsRoot) ? ([], []) : ScanRoot(modsRoot);
    }

    /// <summary>순수 로직만 분리 — 테스트가 실제 어셈블리 경로 대신 임시 디렉터리로 호출한다.</summary>
    public static (IReadOnlyList<AlternativeGroup> Groups, IReadOnlyList<CatalogWarning> Warnings) ScanRoot(string modsRoot)
    {
        var groups = new List<AlternativeGroup>();
        var warnings = new List<CatalogWarning>();

        foreach (var modDir in Directory.GetDirectories(modsRoot))
        {
            var file = Path.Combine(modDir, "db", "QuestAlternativeConditionGroups.json");
            if (!File.Exists(file)) continue;

            try
            {
                if (!QuestContentEnabled(Path.Combine(modDir, "db", "Config.json"))) continue;

                var parsed = JsonSerializer.Deserialize<GroupsFile>(File.ReadAllText(file), Json);
                foreach (var g in parsed?.Groups ?? [])
                {
                    if (g.Operator is null || g.Operator.Equals("any", StringComparison.OrdinalIgnoreCase))
                    {
                        groups.Add(new AlternativeGroup(g.QuestId, g.ConditionIds));
                    }
                }
            }
            catch (Exception ex)
            {
                warnings.Add(new CatalogWarning(null, WarningCodes.ModQuestScanFailed, $"{Path.GetFileName(modDir)}: {ex.GetType().Name}: {ex.Message}"));
            }
        }

        return (groups, warnings);
    }

    private static bool QuestContentEnabled(string configFile)
    {
        if (!File.Exists(configFile)) return true;
        using var config = JsonDocument.Parse(File.ReadAllText(configFile));
        return !config.RootElement.TryGetProperty("questContentEnabled", out var enabled) || enabled.ValueKind != JsonValueKind.False;
    }

    private sealed record GroupsFile(List<GroupEntry>? Groups);

    private sealed record GroupEntry(string QuestId, string? Operator, List<string> ConditionIds);
}
