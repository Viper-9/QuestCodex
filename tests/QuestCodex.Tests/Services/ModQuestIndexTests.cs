using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using QuestCodex.Catalog.Models;
using QuestCodex.Services;

namespace QuestCodex.Tests.Services;

public class ModQuestIndexTests
{
    private sealed class TempModsRoot : IDisposable
    {
        public string Root { get; }
        public string SelfDir { get; }

        public TempModsRoot()
        {
            Root = Directory.CreateTempSubdirectory("qc-mods-").FullName;
            SelfDir = Path.Combine(Root, "QuestCodex");
            Directory.CreateDirectory(SelfDir);
            File.WriteAllText(Path.Combine(SelfDir, "quests.json"), QuestDb("selfid000000000000000000"));
        }

        public void AddMod(string modName, string relativeFilePath, string json)
        {
            var full = Path.Combine(Root, modName, relativeFilePath);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, json);
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    /// <summary>
    /// 퀘스트 DB 픽스처. 값에 특징 필드(traderId/conditions/rewards)가 있어야 스캔이 퀘스트로 인정하므로,
    /// 빈 객체로는 더 이상 테스트할 수 없다.
    /// </summary>
    private static string QuestDb(params string[] questIds) =>
        "{" + string.Join(
            ",",
            questIds.Select(id => $"\"{id}\": {{ \"traderId\": \"54cb50c76803fa8b248b4571\", \"conditions\": {{}}, \"rewards\": {{}} }}")) + "}";

    [Fact]
    public void Maps_quest_ids_to_their_mod_folder()
    {
        using var mods = new TempModsRoot();
        mods.AddMod("acidphantasm-reffriendlyquests", "db/quests.json", QuestDb("aaa000000000000000000001"));
        mods.AddMod("ItemPropertyBackport", "db/quests.json", QuestDb("bbb000000000000000000002"));

        var (origins, warnings) = ModQuestIndex.ScanRoot(mods.Root, mods.SelfDir);

        Assert.Equal("acidphantasm-reffriendlyquests", origins["aaa000000000000000000001"]);
        Assert.Equal("ItemPropertyBackport", origins["bbb000000000000000000002"]);
        Assert.Empty(warnings);
    }

    /// <summary>
    /// 파일명 매칭을 버린 뒤의 핵심 회귀 테스트: 실제 모드(WTT-Armory 등)는 퀘스트를 트레이더별로 쪼개서
    /// quests.json 과 전혀 다른 이름으로 저장한다. 이름이 아니라 파일 내용(스키마)으로 잡혀야 한다.
    /// </summary>
    [Fact]
    public void Quest_db_is_found_regardless_of_file_name()
    {
        using var mods = new TempModsRoot();
        mods.AddMod("WTT-Artem", "db/CustomQuests/66bf757f27d0b097db0acea5/Quests/ArtemQuests.json", QuestDb("a1a000000000000000000001"));
        mods.AddMod("WTT-Armory", "db/CustomQuests/PRAPOR/Quests/PraporQuests.json", QuestDb("a2a000000000000000000002"));
        mods.AddMod("WTT-Armory", "db/CustomQuests/SKIER/Quests/SKIER.json", QuestDb("a3a000000000000000000003"));

        var (origins, warnings) = ModQuestIndex.ScanRoot(mods.Root, mods.SelfDir);

        Assert.Equal("WTT-Artem", origins["a1a000000000000000000001"]);
        Assert.Equal("WTT-Armory", origins["a2a000000000000000000002"]);
        Assert.Equal("WTT-Armory", origins["a3a000000000000000000003"]);
        Assert.Empty(warnings);
    }

    /// <summary>
    /// 스키마 검사가 필요한 이유: 아이템 템플릿 ID도 퀘스트 ID와 똑같은 24자리 hex 라서, 키 형식만 보면
    /// 아이템 매핑·봇 설정 파일이 전부 퀘스트로 딸려 들어온다(실측 기준 허수 키 29,618개).
    /// </summary>
    [Fact]
    public void Hex_keyed_files_without_quest_shape_are_ignored()
    {
        using var mods = new TempModsRoot();
        mods.AddMod("ItemMod", "db/items/mapping.json", """
            { "5448bd6b4bdc2dfc2f8b4569": { "_id": "5448bd6b4bdc2dfc2f8b4569", "_parent": "5448bc234bdc2d3c308b4569" } }
            """);
        mods.AddMod("GoodMod", "db/quests.json", QuestDb("ccc000000000000000000003"));

        var (origins, warnings) = ModQuestIndex.ScanRoot(mods.Root, mods.SelfDir);

        Assert.Equal("GoodMod", origins["ccc000000000000000000003"]);
        Assert.DoesNotContain("5448bd6b4bdc2dfc2f8b4569", origins.Keys);
        Assert.Empty(warnings);
    }

    /// <summary>판정은 파일 단위다 — 항목 하나라도 퀘스트 모양이 아니면 그 파일 전체를 버린다.</summary>
    [Fact]
    public void A_single_non_quest_entry_disqualifies_the_whole_file()
    {
        using var mods = new TempModsRoot();
        mods.AddMod("MixedMod", "db/mixed.json", """
            {
              "b1b000000000000000000001": { "traderId": "54cb50c76803fa8b248b4571" },
              "b2b000000000000000000002": { "_parent": "5448bc234bdc2d3c308b4569" }
            }
            """);

        var (origins, warnings) = ModQuestIndex.ScanRoot(mods.Root, mods.SelfDir);

        Assert.Empty(origins);
        Assert.Empty(warnings);
    }

    /// <summary>빈 JSON 객체는 퀘스트 DB가 아니다(모드 폴더엔 빈 JSON 스텁이 흔하다).</summary>
    [Fact]
    public void Empty_json_object_is_not_a_quest_db()
    {
        using var mods = new TempModsRoot();
        mods.AddMod("EmptyMod", "db/quests.json", "{}");

        var (origins, warnings) = ModQuestIndex.ScanRoot(mods.Root, mods.SelfDir);

        Assert.Empty(origins);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Self_mod_folder_is_excluded()
    {
        using var mods = new TempModsRoot();

        var (origins, _) = ModQuestIndex.ScanRoot(mods.Root, mods.SelfDir);

        Assert.DoesNotContain("selfid000000000000000000", origins.Keys);
    }

    /// <summary>
    /// 파싱 실패는 경고 없이 조용히 건너뛴다: 이제 모드 폴더의 모든 *.json 을 읽기 때문에, 퀘스트와 무관한
    /// 파일의 파싱 실패까지 경고로 남기면 warnings 배열이 폭발한다. 중요한 성질은 그대로다 — 깨진 파일
    /// 하나가 나머지 스캔을 중단시키지 않는다.
    /// </summary>
    [Fact]
    public void Broken_json_in_one_mod_does_not_stop_the_scan()
    {
        using var mods = new TempModsRoot();
        mods.AddMod("BrokenMod", "quests.json", "{ not json");
        mods.AddMod("GoodMod", "quests.json", QuestDb("ccc000000000000000000003"));

        var (origins, warnings) = ModQuestIndex.ScanRoot(mods.Root, mods.SelfDir);

        Assert.Equal("GoodMod", origins["ccc000000000000000000003"]);
        Assert.Empty(warnings);
    }

    /// <summary>
    /// Finding 1 회귀 테스트: 하위 디렉터리 열거(Directory.EnumerateFiles) 자체가 예외를 던지는 경우
    /// (권한 거부 등) ScanRoot 전체가 죽지 않고, 이웃 모드 폴더는 정상적으로 스캔되어야 한다.
    /// ACL 로 실제 UnauthorizedAccessException 을 유발한다 — Windows 전용, 이 프로젝트는 SPT 서버(Windows)
    /// 대상이라 다른 플랫폼 스킵 처리는 하지 않는다.
    /// </summary>
    [Fact]
    [SupportedOSPlatform("windows")]
    public void Inaccessible_subfolder_does_not_stop_the_scan()
    {
        using var mods = new TempModsRoot();
        mods.AddMod("DeniedMod", "db/quests.json", QuestDb("eee000000000000000000005"));
        mods.AddMod("GoodMod", "quests.json", QuestDb("fff000000000000000000006"));

        var deniedSub = Path.Combine(mods.Root, "DeniedMod", "db");
        var dirInfo = new DirectoryInfo(deniedSub);
        var identity = WindowsIdentity.GetCurrent()!.User!;
        var denyRule = new FileSystemAccessRule(
            identity,
            FileSystemRights.ListDirectory | FileSystemRights.Read | FileSystemRights.ReadAndExecute | FileSystemRights.Traverse,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Deny);

        var security = dirInfo.GetAccessControl();
        security.AddAccessRule(denyRule);
        dirInfo.SetAccessControl(security);

        try
        {
            var (origins, warnings) = ModQuestIndex.ScanRoot(mods.Root, mods.SelfDir);

            // 접근 거부된 폴더의 퀘스트는 잡히지 않지만(EnumerationOptions.IgnoreInaccessible 이 조용히 건너뜀),
            // 스캔 자체는 죽지 않고 이웃 GoodMod 는 정상적으로 반영되어야 한다.
            Assert.Equal("GoodMod", origins["fff000000000000000000006"]);
            Assert.DoesNotContain("eee000000000000000000005", origins.Keys);
        }
        finally
        {
            // 정리: TempModsRoot.Dispose() 의 Directory.Delete 가 실패하지 않도록 deny 규칙을 먼저 제거한다.
            var cleanupSecurity = dirInfo.GetAccessControl();
            cleanupSecurity.RemoveAccessRule(denyRule);
            dirInfo.SetAccessControl(cleanupSecurity);
        }
    }

    [Fact]
    public void Colliding_quest_id_keeps_the_alphabetically_first_mod_and_warns()
    {
        using var mods = new TempModsRoot();
        mods.AddMod("ZetaMod", "quests.json", QuestDb("ddd000000000000000000004"));
        mods.AddMod("AlphaMod", "quests.json", QuestDb("ddd000000000000000000004"));

        var (origins, warnings) = ModQuestIndex.ScanRoot(mods.Root, mods.SelfDir);

        Assert.Equal("AlphaMod", origins["ddd000000000000000000004"]);
        var w = Assert.Single(warnings, w => w.Code == WarningCodes.ModQuestIdCollision);
        Assert.Equal("ddd000000000000000000004", w.QuestId);
        Assert.Contains("AlphaMod", w.Detail);
        Assert.Contains("ZetaMod", w.Detail);
    }

    /// <summary>Finding 3: 24자리 hex 가 아닌 최상위 키(예: 설정 파일의 "enabled")가 하나라도 있으면 퀘스트 DB로 보지 않는다.</summary>
    [Fact]
    public void Non_hex_top_level_keys_are_ignored()
    {
        using var mods = new TempModsRoot();
        mods.AddMod("ConfigLookalike", "quests.json", """{ "enabled": true, "notAQuestId": {} }""");
        mods.AddMod("GoodMod", "quests.json", QuestDb("ccc000000000000000000003"));

        var (origins, warnings) = ModQuestIndex.ScanRoot(mods.Root, mods.SelfDir);

        Assert.Equal("GoodMod", origins["ccc000000000000000000003"]);
        Assert.DoesNotContain("enabled", origins.Keys);
        Assert.DoesNotContain("notAQuestId", origins.Keys);
        Assert.Empty(warnings);
    }

    /// <summary>Finding 3: 대소문자만 다른 동일 퀘스트 ID는 같은 ID로 취급되어 충돌로 잡혀야 한다.</summary>
    [Fact]
    public void Quest_id_matching_is_case_insensitive()
    {
        using var mods = new TempModsRoot();
        mods.AddMod("AlphaMod", "quests.json", QuestDb("ddd000000000000000000004"));
        mods.AddMod("ZetaMod", "quests.json", QuestDb("DDD000000000000000000004"));

        var (origins, warnings) = ModQuestIndex.ScanRoot(mods.Root, mods.SelfDir);

        Assert.Single(origins);
        var w = Assert.Single(warnings, w => w.Code == WarningCodes.ModQuestIdCollision);
        Assert.Contains("AlphaMod", w.Detail);
        Assert.Contains("ZetaMod", w.Detail);
    }
}
