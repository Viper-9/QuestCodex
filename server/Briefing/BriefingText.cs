using System.Globalization;
using QuestCodex.Catalog.Models;

namespace QuestCodex.Briefing;

// 브리핑 응답에 서버가 직접 넣는 문구.
// 웹은 i18n 사전(web/src/i18n/*.json)으로 만들지만 클라이언트는 받은 문구를 그대로 그리기 때문에 여기 사본을 둔다.
// 웹 사전의 map.name.*, raid.objCount 를 고치면 여기도 같이 고친다. en·kr·ru 말고는 en 으로 나간다.
public static class BriefingText
{
    private static readonly Dictionary<string, Dictionary<string, string>> MapNames = new(StringComparer.Ordinal)
    {
        ["en"] = new(StringComparer.Ordinal)
        {
            ["bigmap"] = "Customs", ["factory4_day"] = "Factory", ["sandbox"] = "Ground Zero", ["interchange"] = "Interchange",
            ["interchange-manimal"] = "Interchange (Expanded)", ["laboratory"] = "The Lab", ["labyrinth"] = "The Labyrinth",
            ["lighthouse"] = "Lighthouse", ["rezervbase"] = "Reserve", ["shoreline"] = "Shoreline",
            ["tarkovstreets"] = "Streets of Tarkov", ["woods"] = "Woods", ["icebreaker"] = "Icebreaker",
        },
        ["kr"] = new(StringComparer.Ordinal)
        {
            ["bigmap"] = "세관", ["factory4_day"] = "공장", ["sandbox"] = "그라운드 제로", ["interchange"] = "인터체인지",
            ["interchange-manimal"] = "확장된 인터체인지", ["laboratory"] = "연구소", ["labyrinth"] = "The Labyrinth",
            ["lighthouse"] = "등대", ["rezervbase"] = "리저브", ["shoreline"] = "해안가",
            ["tarkovstreets"] = "타르코프 시내", ["woods"] = "삼림", ["icebreaker"] = "아이스브레이커",
        },
        ["ru"] = new(StringComparer.Ordinal)
        {
            ["bigmap"] = "Таможня", ["factory4_day"] = "Завод", ["sandbox"] = "Эпицентр", ["interchange"] = "Развязка",
            ["interchange-manimal"] = "Развязка (расширенная)", ["laboratory"] = "Лаборатория", ["labyrinth"] = "Лабиринт",
            ["lighthouse"] = "Маяк", ["rezervbase"] = "Резерв", ["shoreline"] = "Берег",
            ["tarkovstreets"] = "Улицы Таркова", ["woods"] = "Лес", ["icebreaker"] = "Ледокол",
        },
    };

    private static Dictionary<string, string> NamesFor(string lang) => MapNames.GetValueOrDefault(lang) ?? MapNames["en"];

    // 탭 이름. 맵 교체 모드가 깔려 있으면 변형 이름("확장된 인터체인지")을 쓰고, 번역이 없는 모드 맵은 키를 그대로 쓴다.
    public static string MapName(string key, IReadOnlyDictionary<string, string> variants, string lang)
    {
        var names = NamesFor(lang);
        if (variants.TryGetValue(key, out var v) && names.TryGetValue($"{key}-{v}", out var variantName)) return variantName;
        return names.GetValueOrDefault(key, key);
    }

    // 웹 사전의 raid.objCount
    public static string ObjectiveCount(int n, string lang) => lang switch
    {
        "kr" => $"목표 {n}개",
        "ru" => $"{n} целей",
        _ => $"{n} objectives",
    };

    // 웹 wiki/format.ts formatObjective 와 같은 결과. 모드가 번역을 빠뜨려 문장이 비어 있으면 조건 종류와 대상 이름으로 채운다.
    public static string ObjectiveText(Objective o)
    {
        var text = o.Text;
        if (text == "") text = o.TargetName is null ? o.ConditionType : $"{o.ConditionType}: {o.TargetName}";
        if (o.TargetCount is { } n) text += $" ×{FormatNumber(n)}";
        return text;
    }

    // 웹 formatInt 처럼 천 단위 쉼표를 넣는다. 서버 문화권과 상관없이 en-US 형식.
    public static string FormatNumber(double n) => n.ToString("#,0.###", CultureInfo.InvariantCulture);
}
