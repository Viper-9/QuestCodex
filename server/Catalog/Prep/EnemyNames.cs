using QuestCodex.Catalog.Models;

namespace QuestCodex.Catalog.Prep;

/// <summary>
/// 처치·명중 목표가 인정하는 봇 역할의 표시 이름(카탈로그 언어). 찾는 순서:
/// ① 아래 표(이름에 BlackDiv 가 들어가면 블랙 디비전) → ② 게임 로케일 QuestCondition/Elimination/Kill/BotRole/{역할} → ③ ScavRole/{역할} → ④ 역할 키 그대로.
/// 게임 로케일에는 구형 보스 6명과 광신도 2종의 BotRole 만 있고, 로케일을 넣는 모드도 없어서(2026-11-07 확인) 표가 먼저다.
/// ②·③ 은 요청 언어 → en 순서(LocaleResolver). 개발용 역할(followerTest 등)과 bossStormtrooper 는 이름에서 뺀다. 이름에 BlackDiv 가 들어가면 "블랙 디비전". 같은 이름은 한 번만.
///
/// 표 규칙: 보스 이름은 웹 사전 map.boss.name.* 와 맞춘다. 다만 bossKnight 는 지도에서는 "군즈"지만 퀘스트는 나이트 한 명을 가리켜 "나이트".
/// 영문·러시아어는 퀘스트 문장의 표기를 따른다(나이트 일행은 러시아어도 라틴 표기, 경호원은 "Свита"). 이름 있는 경호원(Basmach, Gus)은 이름으로.
/// 한 보스의 경호원 하위 역할(돌격·정찰·저격 등)은 같은 이름으로 묶는다. en·kr·ru 말고는 en.
/// </summary>
public sealed class EnemyNames(LocaleResolver locale, string lang)
{
    // 이름에서 빼는 역할: 개발용(바닐라 enum 의 test·bossTest·followerTest)과 정체를 알 수 없는 bossStormtrooper(QuestBackport, 사용자 결정).
    private static readonly HashSet<string> Hidden = new(StringComparer.OrdinalIgnoreCase) { "test", "bossTest", "followerTest", "bossStormtrooper" };

    private static readonly (string En, string Kr, string Ru) BlackDivision = ("Black Division", "블랙 디비전", "Black Division");

    private static readonly Dictionary<string, (string En, string Kr, string Ru)> Roles = new(StringComparer.OrdinalIgnoreCase)
    {
        // 보스
        ["bossBully"] = ("Reshala", "르샬라", "Решала"),
        ["bossKilla"] = ("Killa", "킬라", "Килла"),
        ["bossGluhar"] = ("Glukhar", "글루하", "Глухарь"),
        ["bossKojaniy"] = ("Shturman", "슈트르만", "Штурман"),
        ["bossSanitar"] = ("Sanitar", "세니타", "Санитар"),
        ["bossTagilla"] = ("Tagilla", "타길라", "Тагилла"),
        ["bossKnight"] = ("Knight", "나이트", "Knight"),
        ["followerBigPipe"] = ("Big Pipe", "빅 파이프", "Big Pipe"),
        ["followerBirdEye"] = ("Birdeye", "버드아이", "Birdeye"),
        ["bossBoar"] = ("Kaban", "카반", "Кабан"),
        ["bossBoarSniper"] = ("Kaban's sniper", "카반 저격수", "Снайпер Кабана"),
        ["bossKolontay"] = ("Kollontay", "콜론타이", "Коллонтай"),
        ["bossZryachiy"] = ("Zryachiy", "즈랴치", "Зрячий"),
        ["bossPartisan"] = ("Partisan", "파르티잔", "Партизан"),
        ["bossTagillaAgro"] = ("Tagilla (Labyrinth)", "타길라(미궁)", "Тагилла (Лабиринт)"),
        ["bossKillaAgro"] = ("Killa (Labyrinth)", "킬라(미궁)", "Килла (Лабиринт)"),
        ["tagillaHelperAgro"] = ("Tagilla's helper", "타길라 부하", "Помощник Тагиллы"),
        ["gifter"] = ("Santa", "산타", "Санта"),

        // 경호원
        ["followerBully"] = ("Reshala's guard", "르샬라 경호원", "Свита Решалы"),
        ["followerGluharAssault"] = ("Glukhar's guard", "글루하 경호원", "Свита Глухаря"),
        ["followerGluharScout"] = ("Glukhar's guard", "글루하 경호원", "Свита Глухаря"),
        ["followerGluharSecurity"] = ("Glukhar's guard", "글루하 경호원", "Свита Глухаря"),
        ["followerGluharSnipe"] = ("Glukhar's guard", "글루하 경호원", "Свита Глухаря"),
        ["followerKojaniy"] = ("Shturman's guard", "슈트르만 경호원", "Свита Штурмана"),
        ["followerSanitar"] = ("Sanitar's guard", "세니타 경호원", "Свита Санитара"),
        ["followerTagilla"] = ("Tagilla's guard", "타길라 경호원", "Свита Тагиллы"),
        ["followerBoar"] = ("Kaban's guard", "카반 경호원", "Свита Кабана"),
        ["followerBoarClose1"] = ("Basmach", "바스마치", "Басмач"),
        ["followerBoarClose2"] = ("Gus", "구스", "Гусь"),
        ["followerKolontayAssault"] = ("Kollontay's guard", "콜론타이 경호원", "Свита Коллонтая"),
        ["followerKolontaySecurity"] = ("Kollontay's guard", "콜론타이 경호원", "Свита Коллонтая"),
        ["followerZryachiy"] = ("Zryachiy's guard", "즈랴치 경호원", "Свита Зрячего"),
        ["followerStormtrooper"] = ("Stormtrooper", "스톰트루퍼", "Штурмовик"),

        // 그 밖의 AI
        ["assault"] = ("Scav", "스캐브", "Дикий"),
        ["assaultGroup"] = ("Scav", "스캐브", "Дикий"),
        ["savage"] = ("Scav", "스캐브", "Дикий"),
        ["marksman"] = ("Sniper Scav", "스나이퍼 스캐브", "Дикий-снайпер"),
        ["pmcBot"] = ("Raider", "레이더", "Рейдер"),
        ["exUsec"] = ("Rogue", "로그", "Отступник"),
        ["arenaFighterEvent"] = ("Bloodhound", "블러드하운드", "Погоня"),
        ["peacemaker"] = ("Peacemaker", "피스메이커", "Миротворец"),
        ["crazyAssaultEvent"] = ("Crazy Scav", "미친 스캐브", "Безумный дикий"),
        ["sectantPriest"] = ("Cultist priest", "광신도 사제", "Сектант Жрец"),
        ["sectantWarrior"] = ("Cultist warrior", "광신도 전사", "Сектант Воин"),
        ["cursedAssault"] = ("Cultist warrior", "광신도 전사", "Сектант Воин"),
        ["sectantPrizrak"] = ("Ghost", "고스트", "Призрак"),
        ["sectantPredvestnik"] = ("Harbinger", "하빈저", "Предвестник"),
        ["sectantOni"] = ("Oni", "오니", "Они"),
        ["infectedAssault"] = ("Infected", "감염자", "Заражённый"),
        ["infectedCivil"] = ("Infected", "감염자", "Заражённый"),
        ["infectedLaborant"] = ("Infected", "감염자", "Заражённый"),
        ["infectedPmc"] = ("Infected", "감염자", "Заражённый"),
        ["infectedTagilla"] = ("Infected Tagilla", "감염된 타길라", "Заражённый Тагилла"),

        // 모드 봇 — 역할 이름 로케일을 넣는 모드가 없어(2026-11-07 확인) 표에 둔다. 사용자 결정(2026-11-07):
        // 웨지 계열은 웨지·웨지 경호원으로 묶는다. bossWedge 는 Black Division 모드(아이스브레이커 스폰), bossWedgeLab·followerWedgeLab 은
        // QuestBackport 가 라이브 퀘스트에서 옮겨 온 역할로 이 클라이언트에는 없어 카운트되지 않지만 같은 이름으로 보여 준다.
        // 이름에 BlackDiv 가 들어간 역할은 표가 아니라 NameOf 의 규칙으로 "블랙 디비전".
        ["bossWedge"] = ("Wedge", "웨지", "Wedge"),
        ["bossWedgeLab"] = ("Wedge", "웨지", "Wedge"),
        ["followerWedgeLab"] = ("Wedge's guard", "웨지 경호원", "Свита Wedge"),
    };

    public IReadOnlyList<string> Of(IEnumerable<string> roles)
        => roles.Where(r => !Hidden.Contains(r)).Select(NameOf).Distinct(StringComparer.Ordinal).ToList();

    private string NameOf(string role)
    {
        if (Roles.TryGetValue(role, out var n)) return Pick(n);
        // Black Division 모드(blackDivAssault·Ib·Breacher·Support·Lead)와 QuestBackport(bossBullyBlackDiv·pmcBotBlackDiv·blackDivision 등) 모두
        if (role.Contains("BlackDiv", StringComparison.OrdinalIgnoreCase)) return Pick(BlackDivision);
        return locale.TryResolve($"QuestCondition/Elimination/Kill/BotRole/{role}")
            ?? locale.TryResolve($"ScavRole/{role}")
            ?? locale.TryResolve($"ScavRole/{char.ToUpperInvariant(role[0])}{role[1..]}")
            ?? role;
    }

    private string Pick((string En, string Kr, string Ru) n) => lang switch { "kr" => n.Kr, "ru" => n.Ru, _ => n.En };
}
