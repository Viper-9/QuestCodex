using System.Collections.Concurrent;
using SPTarkov.Common.Models.Logging;

namespace QuestCodex.Services;

// 요청 언어를 서버에 있는 게임 로케일 키로 맞춘다. 카탈로그·브리핑 API 가 같이 쓴다.
// 클라이언트 쪽 한글화 모드가 kr-en 같은 로케일을 따로 추가하면 게임 culture 가 그대로 넘어오는데,
// 서버에는 그 텍스트가 없으니 가장 가까운 언어로 대체한다.
public static class LangFallback
{
    public const string Default = "en";

    // 같은 대체 조합이 요청마다 반복되므로 서버 수명 동안 조합별로 한 번만 로그를 남긴다.
    private static readonly ConcurrentDictionary<string, byte> Logged = new(StringComparer.Ordinal);

    // 1) 그대로 있으면 그 키 2) 하이픈이 있으면 첫 하이픈 앞부분 3) en. 대소문자 무시, 앞뒤 공백 무시.
    // es-mx 처럼 하이픈이 든 정상 키는 1) 에서 걸리므로 무조건 자르지 않는다. en 마저 없으면 null.
    public static string? Pick(string? requested, IReadOnlySet<string> supported)
    {
        var lang = requested?.Trim() ?? "";
        if (lang.Length > 0)
        {
            if (Find(lang, supported) is { } exact) return exact;
            var dash = lang.IndexOf('-');
            if (dash > 0 && Find(lang[..dash], supported) is { } prefix) return prefix;
        }
        return Find(Default, supported);
    }

    // Pick 과 같고, 요청과 다른 언어로 대체했으면 로그를 남긴다. 빈 값 → en 은 기본값이라 남기지 않는다.
    public static string? Resolve<T>(string? requested, IReadOnlySet<string> supported, ISptLogger<T> logger)
    {
        var picked = Pick(requested, supported);
        var asked = requested?.Trim() ?? "";
        if (picked is not null && asked.Length > 0 && !string.Equals(asked, picked, StringComparison.OrdinalIgnoreCase)
            && Logged.TryAdd($"{asked.ToLowerInvariant()}>{picked}", 0))
        {
            logger.Info($"[QuestCodex] language '{asked}' is not available on the server, using '{picked}' instead");
        }
        return picked;
    }

    // 키 원래 표기를 돌려준다. 지원 목록은 스무 개 남짓이라 순회로 충분하다.
    private static string? Find(string lang, IReadOnlySet<string> supported)
    {
        if (supported.Contains(lang)) return lang;
        return supported.FirstOrDefault(s => string.Equals(s, lang, StringComparison.OrdinalIgnoreCase));
    }
}
