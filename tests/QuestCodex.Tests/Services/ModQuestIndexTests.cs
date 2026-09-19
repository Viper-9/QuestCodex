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
            File.WriteAllText(Path.Combine(SelfDir, "quests.json"), """{ "selfid000000000000000000": {} }""");
        }

        public void AddMod(string modName, string relativeFilePath, string json)
        {
            var full = Path.Combine(Root, modName, relativeFilePath);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, json);
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    [Fact]
    public void Maps_quest_ids_to_their_mod_folder()
    {
        using var mods = new TempModsRoot();
        mods.AddMod("acidphantasm-reffriendlyquests", "db/quests.json", """{ "aaa000000000000000000001": {} }""");
        mods.AddMod("ItemPropertyBackport", "db/quests.json", """{ "bbb000000000000000000002": {} }""");

        var (origins, warnings) = ModQuestIndex.ScanRoot(mods.Root, mods.SelfDir);

        Assert.Equal("acidphantasm-reffriendlyquests", origins["aaa000000000000000000001"]);
        Assert.Equal("ItemPropertyBackport", origins["bbb000000000000000000002"]);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Self_mod_folder_is_excluded()
    {
        using var mods = new TempModsRoot();

        var (origins, _) = ModQuestIndex.ScanRoot(mods.Root, mods.SelfDir);

        Assert.DoesNotContain("selfid000000000000000000", origins.Keys);
    }

    [Fact]
    public void Broken_json_in_one_mod_does_not_stop_the_scan()
    {
        using var mods = new TempModsRoot();
        mods.AddMod("BrokenMod", "quests.json", "{ not json");
        mods.AddMod("GoodMod", "quests.json", """{ "ccc000000000000000000003": {} }""");

        var (origins, warnings) = ModQuestIndex.ScanRoot(mods.Root, mods.SelfDir);

        Assert.Equal("GoodMod", origins["ccc000000000000000000003"]);
        Assert.Single(warnings, w => w.Code == WarningCodes.ModQuestScanFailed && w.Detail.StartsWith("BrokenMod"));
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
        mods.AddMod("DeniedMod", "db/quests.json", """{ "eee000000000000000000005": {} }""");
        mods.AddMod("GoodMod", "quests.json", """{ "fff000000000000000000006": {} }""");

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
        mods.AddMod("ZetaMod", "quests.json", """{ "ddd000000000000000000004": {} }""");
        mods.AddMod("AlphaMod", "quests.json", """{ "ddd000000000000000000004": {} }""");

        var (origins, warnings) = ModQuestIndex.ScanRoot(mods.Root, mods.SelfDir);

        Assert.Equal("AlphaMod", origins["ddd000000000000000000004"]);
        var w = Assert.Single(warnings, w => w.Code == WarningCodes.ModQuestIdCollision);
        Assert.Equal("ddd000000000000000000004", w.QuestId);
        Assert.Contains("AlphaMod", w.Detail);
        Assert.Contains("ZetaMod", w.Detail);
    }

    /// <summary>Finding 3: 24자리 hex 가 아닌 최상위 키(예: 설정 파일의 "enabled")는 퀘스트 ID로 취급하지 않는다.</summary>
    [Fact]
    public void Non_hex_top_level_keys_are_ignored()
    {
        using var mods = new TempModsRoot();
        mods.AddMod("ConfigLookalike", "quests.json", """{ "enabled": true, "notAQuestId": {} }""");
        mods.AddMod("GoodMod", "quests.json", """{ "ccc000000000000000000003": {} }""");

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
        mods.AddMod("AlphaMod", "quests.json", """{ "ddd000000000000000000004": {} }""");
        mods.AddMod("ZetaMod", "quests.json", """{ "DDD000000000000000000004": {} }""");

        var (origins, warnings) = ModQuestIndex.ScanRoot(mods.Root, mods.SelfDir);

        Assert.Single(origins);
        var w = Assert.Single(warnings, w => w.Code == WarningCodes.ModQuestIdCollision);
        Assert.Contains("AlphaMod", w.Detail);
        Assert.Contains("ZetaMod", w.Detail);
    }
}
