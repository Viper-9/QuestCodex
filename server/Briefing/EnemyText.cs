namespace QuestCodex.Briefing;

// 처치·명중 목표의 진영(Target) 이름. 역할 이름은 카탈로그가 정한다(Catalog/Prep/EnemyNames.cs).
// en·kr·ru 말고는 en, 표에 없는 진영(모드)은 키를 그대로 쓴다.
public static class EnemyText
{
    private static readonly Dictionary<string, (string En, string Kr, string Ru)> Targets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Savage"] = ("Scavs", "스캐브", "Дикие"),
        ["AnyPmc"] = ("PMC", "PMC", "ЧВК"),
        ["Usec"] = ("USEC", "USEC", "USEC"),
        ["Bear"] = ("BEAR", "BEAR", "BEAR"),
        ["Any"] = ("Any enemy", "모든 적", "Любой противник"),
    };

    public static string TargetName(string target, string lang)
    {
        if (!Targets.TryGetValue(target, out var n)) return target;
        return lang switch { "kr" => n.Kr, "ru" => n.Ru, _ => n.En };
    }
}
