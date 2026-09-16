namespace QuestCodex.Catalog;

/// <summary>요청 언어 → en → 원문 순서로 텍스트를 찾는다. 폴백 여부를 호출자에게 알린다.</summary>
public sealed class LocaleResolver(
    IReadOnlyDictionary<string, string> locale,
    IReadOnlyDictionary<string, string> fallback)
{
    public string Resolve(string key, string fallbackText, out bool fellBack)
    {
        if (locale.TryGetValue(key, out var text) && !string.IsNullOrWhiteSpace(text))
        {
            fellBack = false;
            return text;
        }

        fellBack = true;
        if (fallback.TryGetValue(key, out var fallbackValue) && !string.IsNullOrWhiteSpace(fallbackValue))
        {
            return fallbackValue;
        }

        return fallbackText;
    }

    /// <summary>키가 어느 로케일에도 없을 때 null. 폴백 텍스트가 없는 선택 필드용.</summary>
    public string? TryResolve(string key)
    {
        if (locale.TryGetValue(key, out var text) && !string.IsNullOrWhiteSpace(text)) return text;
        if (fallback.TryGetValue(key, out var fb) && !string.IsNullOrWhiteSpace(fb)) return fb;
        return null;
    }
}
