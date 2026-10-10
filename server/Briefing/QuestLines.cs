namespace QuestCodex.Briefing;

// 퀘스트 목록의 한 줄. 진행 수치가 없으면 Current, Target 이 null.
public sealed record QuestLine(string Summary, double? Current, double? Target);

public static class QuestLines
{
    // 웹 레이드 준비의 퀘스트 줄과 같은 규칙. 목표가 하나면 그 문장을, 여럿이면 "목표 N개" 를 쓴다.
    // 수치는 target 이 있는 목표만 더하고 current 는 target 을 넘지 않게 자른다.
    // 정렬용 진행률(CounterRatio)과 달리 target 이 1 인 목표도 센다. 웹 카운터 표시가 그렇게 되어 있다.
    public static QuestLine Of(IReadOnlyList<RaidEntry> entries, string lang)
    {
        double current = 0, target = 0;
        var any = false;
        foreach (var e in entries)
        {
            if (e.Progress is not { Target: { } t } p) continue;
            current += Math.Min(p.Current, t);
            target += t;
            any = true;
        }

        var summary = entries.Count == 1 ? BriefingText.ObjectiveText(entries[0].Objective) : BriefingText.ObjectiveCount(entries.Count, lang);
        return any ? new QuestLine(summary, current, target) : new QuestLine(summary, null, null);
    }
}
