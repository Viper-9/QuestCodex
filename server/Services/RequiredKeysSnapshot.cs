using System.Text.Json;
using SPTarkov.DI.Annotations;

namespace QuestCodex.Services;

/// <summary>
/// 모드에 동봉한 목표별 필요 열쇠 스냅샷(Data/required-keys.json, tools/gen-required-keys.mjs 로 tarkov.dev 에서 뽑는다).
/// 조건 ID → 열쇠 묶음 목록. 묶음은 모두 필요하고 묶음 안에서는 하나만 있으면 된다.
/// 퀘스트 데이터에는 이 정보가 없어서 따로 싣는다. 없으면 Objectives 가 null 이고 목표의 RequiredKeys 가 빈다(기동 실패 아님).
/// </summary>
[Injectable(InjectionType.Singleton)]
public class RequiredKeysSnapshot
{
    private readonly Lazy<IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyList<string>>>?> _data = new(LoadFromModFolder);

    public IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyList<string>>>? Objectives => _data.Value;

    public static IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyList<string>>> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var result = new Dictionary<string, IReadOnlyList<IReadOnlyList<string>>>(StringComparer.Ordinal);
        foreach (var o in doc.RootElement.GetProperty("objectives").EnumerateObject())
        {
            var groups = o.Value.EnumerateArray()
                .Select(g => (IReadOnlyList<string>)g.EnumerateArray().Select(t => t.GetString()).OfType<string>().ToList())
                .Where(g => g.Count > 0)
                .ToList();
            if (groups.Count > 0) result[o.Name] = groups;
        }
        return result;
    }

    public static IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyList<string>>>? LoadFromModFolder()
    {
        var modDir = Path.GetDirectoryName(typeof(RequiredKeysSnapshot).Assembly.Location);
        if (modDir is null) return null;
        var path = Path.Combine(modDir, "Data", "required-keys.json");
        return File.Exists(path) ? Parse(File.ReadAllText(path)) : null;
    }
}
