using System.Text.Json;
using System.Text.RegularExpressions;
using QuestCodex.Catalog.Models;
using SPTarkov.DI.Annotations;

namespace QuestCodex.Services;

/// <summary>
/// user/mods/ 를 스캔해 questId → 모드 폴더명 맵을 만든다. 바닐라 판정(VanillaSnapshot)과는 완전히 별개이며,
/// IsVanilla=false 인 퀘스트에만 이 맵을 적용한다(CatalogBuilder).
/// 모드마다 퀘스트 파일의 이름·경로가 제각각이라(quests.json / ArtemQuests.json / SKIER.json …) 파일명으로는
/// 찾지 않고, 모드 폴더 아래 모든 *.json 을 파싱해 "퀘스트 DB 모양"(<see cref="LooksLikeQuestDb"/>)인 파일만
/// 골라낸다.
/// 알려진 한계: CustomQuestService.CreateQuest() 로 C# 코드에서 주입된 퀘스트는 파일 흔적이 없어 여기 잡히지
/// 않는다(SPT core 에 데이터 출처를 추적하는 공개 API가 없음 — 디컴파일로 확인). 해결 시도하지 않는다.
/// </summary>
[Injectable(InjectionType.Singleton)]
public class ModQuestIndex
{
    private readonly Lazy<(IReadOnlyDictionary<string, string> QuestOrigins, IReadOnlyList<CatalogWarning> Warnings)> _data = new(Scan);

    public IReadOnlyDictionary<string, string> QuestOrigins => _data.Value.QuestOrigins;
    public IReadOnlyList<CatalogWarning> Warnings => _data.Value.Warnings;

    /// <summary>퀘스트 ID는 MongoID 24자리 hex. 최상위 키가 하나라도 이 형식이 아니면 그 파일은 퀘스트 DB가 아니다.</summary>
    private static readonly Regex QuestIdPattern = new("^[0-9a-fA-F]{24}$", RegexOptions.Compiled);

    /// <summary>
    /// 퀘스트 객체의 특징 필드. 아이템 ID도 같은 24자리 hex 라서 키 형식만으로는 아이템 매핑·봇 설정 파일과
    /// 구분할 수 없다 — 값이 이 중 하나라도 갖고 있어야 퀘스트로 인정한다.
    /// </summary>
    private static readonly string[] QuestShapeProperties = ["conditions", "traderId", "rewards"];

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
                foreach (var file in Directory.EnumerateFiles(modDir, "*.json", enumOptions))
                {
                    Dictionary<string, JsonElement>? parsed;
                    try
                    {
                        parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(file));
                    }
                    catch
                    {
                        // 퀘스트와 무관한 JSON 수백 개까지 같이 읽는 구조라, 개별 파일의 읽기·파싱 실패는 경고
                        // 없이 건너뛴다(설정·번역 파일이 배열이거나 JSON5 주석을 쓰는 것만으로도 여기 들어온다).
                        // 스캔 자체가 통째로 실패하는 경우만 아래 바깥 catch 에서 ModQuestScanFailed 로 남긴다.
                        continue;
                    }

                    if (parsed is null || !LooksLikeQuestDb(parsed)) continue;

                    foreach (var questId in parsed.Keys)
                    {
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

    /// <summary>
    /// 퀘스트 DB 판정: 최상위 키가 하나 이상이고, 전부 24자리 hex 이며, 각 값이 퀘스트 객체의 특징 필드를 가진
    /// JSON 객체여야 한다. 하나라도 어긋나면 그 파일 전체를 퀘스트 DB가 아닌 것으로 본다.
    /// </summary>
    private static bool LooksLikeQuestDb(Dictionary<string, JsonElement> parsed)
    {
        if (parsed.Count == 0) return false;

        foreach (var (key, value) in parsed)
        {
            if (!QuestIdPattern.IsMatch(key)) return false;
            if (value.ValueKind != JsonValueKind.Object) return false;

            var looksLikeQuest = false;
            foreach (var property in QuestShapeProperties)
            {
                if (value.TryGetProperty(property, out _))
                {
                    looksLikeQuest = true;
                    break;
                }
            }

            if (!looksLikeQuest) return false;
        }

        return true;
    }
}
