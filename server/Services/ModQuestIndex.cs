using System.Text.Json;
using System.Text.RegularExpressions;
using QuestCodex.Catalog.Models;
using SPTarkov.DI.Annotations;

namespace QuestCodex.Services;

/// <summary>
/// user/mods/ 를 스캔해 questId → 모드 폴더명 맵을 만든다. 바닐라 판정(VanillaSnapshot)과는 완전히 별개이며,
/// IsVanilla=false 인 퀘스트에만 이 맵을 적용한다(CatalogBuilder). CustomQuestService.CreateQuest() 로 주입된
/// 퀘스트는 파일 흔적이 없어 여기 잡히지 않는다 — 알려진 한계, 해결 시도하지 않는다.
/// </summary>
[Injectable(InjectionType.Singleton)]
public class ModQuestIndex
{
    private readonly Lazy<(IReadOnlyDictionary<string, string> QuestOrigins, IReadOnlyList<CatalogWarning> Warnings)> _data = new(Scan);

    public IReadOnlyDictionary<string, string> QuestOrigins => _data.Value.QuestOrigins;
    public IReadOnlyList<CatalogWarning> Warnings => _data.Value.Warnings;

    /// <summary>퀘스트 ID는 MongoID 24자리 hex — 이 형식이 아닌 최상위 키는 quests.json 이름만 같은 비-퀘스트 파일로 간주하고 무시한다.</summary>
    private static readonly Regex QuestIdPattern = new("^[0-9a-fA-F]{24}$", RegexOptions.Compiled);

    private static (IReadOnlyDictionary<string, string>, IReadOnlyList<CatalogWarning>) Scan()
    {
        var modDir = Path.GetDirectoryName(typeof(ModQuestIndex).Assembly.Location);
        if (modDir is null) return (new Dictionary<string, string>(), []);

        var modsRoot = Directory.GetParent(modDir)?.FullName;
        if (modsRoot is null || !Directory.Exists(modsRoot)) return (new Dictionary<string, string>(), []);

        return ScanRoot(modsRoot, modDir);
    }

    /// <summary>순수 로직만 분리 — 테스트가 실제 어셈블리 경로 대신 임시 디렉터리로 호출한다.</summary>
    public static (IReadOnlyDictionary<string, string> QuestOrigins, IReadOnlyList<CatalogWarning> Warnings) ScanRoot(string modsRoot, string selfDir)
    {
        var origins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<CatalogWarning>();
        var selfFull = Path.GetFullPath(selfDir);
        var enumOptions = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };

        foreach (var modDir in Directory.GetDirectories(modsRoot))
        {
            if (string.Equals(Path.GetFullPath(modDir), selfFull, StringComparison.OrdinalIgnoreCase)) continue;

            var modName = Path.GetFileName(modDir);

            try
            {
                foreach (var file in Directory.EnumerateFiles(modDir, "quests.json", enumOptions))
                {
                    Dictionary<string, JsonElement>? parsed;
                    try
                    {
                        parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(file));
                    }
                    catch (Exception ex)
                    {
                        warnings.Add(new CatalogWarning(null, WarningCodes.ModQuestScanFailed, $"{modName}: {ex.GetType().Name}: {ex.Message}"));
                        continue;
                    }

                    if (parsed is null) continue;

                    foreach (var questId in parsed.Keys)
                    {
                        if (!QuestIdPattern.IsMatch(questId)) continue;

                        if (!origins.TryGetValue(questId, out var existing))
                        {
                            origins[questId] = modName;
                        }
                        else if (existing != modName)
                        {
                            var winner = string.CompareOrdinal(existing, modName) <= 0 ? existing : modName;
                            var loser = winner == existing ? modName : existing;
                            origins[questId] = winner;
                            warnings.Add(new CatalogWarning(questId, WarningCodes.ModQuestIdCollision, $"claimed by '{winner}' and '{loser}', kept '{winner}'"));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                warnings.Add(new CatalogWarning(null, WarningCodes.ModQuestScanFailed, $"{modName}: {ex.GetType().Name}: {ex.Message}"));
            }
        }

        return (origins, warnings);
    }
}
