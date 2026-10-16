using System.Text.Json;

namespace QuestCodex.Services;

/// <summary>
/// 서버 시작 때 미리 만들 언어 기록(16 catalog-warmup 스펙 §4). 요청받은 언어를 최대 <see cref="Max"/>개 남긴다.
/// 이미 있는 언어는 순서를 바꾸지 않는다 — 웹(en)과 Briefing(kr)이 번갈아 요청해도 파일을 매번 다시 쓰지 않게.
/// </summary>
public static class WarmLangs
{
    public const int Max = 2;

    /// <summary>새 언어면 맨 앞에 넣고 오래된 것을 잘라낸 목록, 이미 있으면 null(변화 없음).</summary>
    public static IReadOnlyList<string>? Add(IReadOnlyList<string> current, string lang)
    {
        if (current.Contains(lang, StringComparer.Ordinal)) return null;
        return [lang, .. current.Take(Max - 1)];
    }

    /// <summary>파일이 없거나 읽을 수 없으면 빈 목록(= 기록 없음).</summary>
    public static IReadOnlyList<string> Read(string path)
    {
        try
        {
            if (!File.Exists(path)) return [];
            using var stream = File.OpenRead(path);
            var file = JsonSerializer.Deserialize<WarmLangsFile>(stream, JsonOptions);
            return file?.Langs?.Where(l => !string.IsNullOrWhiteSpace(l)).Take(Max).ToList() ?? [];
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>임시 파일에 쓴 뒤 교체한다. 실패는 호출자가 처리한다.</summary>
    public static void Write(string path, IReadOnlyList<string> langs)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        using (var stream = File.Create(temp)) JsonSerializer.Serialize(stream, new WarmLangsFile(langs.ToList()), JsonOptions);
        File.Move(temp, path, overwrite: true);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record WarmLangsFile(List<string>? Langs);
}
