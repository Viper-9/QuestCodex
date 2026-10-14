using System.Text.RegularExpressions;

namespace QuestCodex.Briefing;

// 데이터에 맵 정보가 없는 목표는 문장에 나온 맵 이름으로 맵을 짐작한다. 웹 derive.ts 의 MAP_TEXT 와 같은 패턴.
internal static class MapText
{
    // ECMAScript 옵션을 켜야 \b 가 JS 처럼 영문 기준 단어 경계가 된다. 끄면 "Customs에서" 를 못 찾는다.
    private static readonly (string Key, Regex Re)[] Patterns =
    [
        ("bigmap", Re(@"\bCustoms\b|세관")),
        ("factory4_day", Re(@"\bFactory\b|공장")),
        ("woods", Re(@"\bWoods\b|삼림")),
        ("shoreline", Re(@"\bShoreline\b|해안선|해안가")),
        ("interchange", Re(@"\bInterchange\b|인터체인지")),
        ("rezervbase", Re(@"\bReserve\b|리저브")),
        ("lighthouse", Re(@"\bLighthouse\b|등대")),
        ("tarkovstreets", Re(@"\bStreets of Tarkov\b|타르코프 시내")),
        ("sandbox", Re(@"\bGround Zero\b|그라운드 제로")),
        ("laboratory", Re(@"\bThe Lab\b|\bLaboratory\b|연구소")),
        ("labyrinth", Re(@"\bLabyrinth\b")),
    ];

    private static Regex Re(string pattern) => new(pattern, RegexOptions.ECMAScript);

    public static IReadOnlyList<string> Find(string text)
        => Patterns.Where(x => x.Re.IsMatch(text)).Select(x => x.Key).ToList();
}
