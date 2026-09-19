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
}
