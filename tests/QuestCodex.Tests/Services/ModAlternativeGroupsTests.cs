using QuestCodex.Catalog.Models;
using QuestCodex.Services;

namespace QuestCodex.Tests.Services;

public class ModAlternativeGroupsTests
{
    private const string OneGroup = """
        { "groups": [ { "questId": "q1", "operator": "any", "conditionIds": ["c1", "c2"] } ] }
        """;

    private sealed class TempModsRoot : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("qc-alt-").FullName;

        public void AddFile(string modName, string fileName, string content)
        {
            var dir = Path.Combine(Root, modName, "db");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, fileName), content);
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    [Fact]
    public void Without_the_file_there_are_no_groups()
    {
        using var mods = new TempModsRoot();
        mods.AddFile("SomeOtherMod", "Config.json", "{}");

        var (groups, warnings) = ModAlternativeGroups.ScanRoot(mods.Root);

        Assert.Empty(groups);
        Assert.Empty(warnings);
    }

    [Fact]
    public void The_file_is_found_whatever_the_mod_folder_is_called()
    {
        using var mods = new TempModsRoot();
        mods.AddFile("RenamedBackport", "QuestAlternativeConditionGroups.json", OneGroup);

        var (groups, warnings) = ModAlternativeGroups.ScanRoot(mods.Root);

        var group = Assert.Single(groups);
        Assert.Equal("q1", group.QuestId);
        Assert.Equal(["c1", "c2"], group.ConditionIds);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Quest_content_off_means_the_mod_does_not_apply_its_groups()
    {
        using var mods = new TempModsRoot();
        mods.AddFile("QuestBackport", "QuestAlternativeConditionGroups.json", OneGroup);
        mods.AddFile("QuestBackport", "Config.json", """{ "questContentEnabled": false }""");

        var (groups, warnings) = ModAlternativeGroups.ScanRoot(mods.Root);

        Assert.Empty(groups);
        Assert.Empty(warnings);
    }

    [Theory]
    [InlineData("""{ "questContentEnabled": true }""")]
    [InlineData("""{ "disableSalesVolumeRequirement": true }""")] // 키가 없으면 QuestBackport 기본값은 켜짐
    public void Quest_content_on_or_unset_applies_the_groups(string config)
    {
        using var mods = new TempModsRoot();
        mods.AddFile("QuestBackport", "QuestAlternativeConditionGroups.json", OneGroup);
        mods.AddFile("QuestBackport", "Config.json", config);

        Assert.Single(ModAlternativeGroups.ScanRoot(mods.Root).Groups);
    }

    [Fact]
    public void Missing_config_counts_as_on()
    {
        using var mods = new TempModsRoot();
        mods.AddFile("QuestBackport", "QuestAlternativeConditionGroups.json", OneGroup);

        Assert.Single(ModAlternativeGroups.ScanRoot(mods.Root).Groups);
    }

    [Fact]
    public void Groups_with_another_operator_are_left_out()
    {
        using var mods = new TempModsRoot();
        mods.AddFile("QuestBackport", "QuestAlternativeConditionGroups.json",
            """{ "groups": [ { "questId": "q1", "operator": "all", "conditionIds": ["c1", "c2"] } ] }""");

        Assert.Empty(ModAlternativeGroups.ScanRoot(mods.Root).Groups);
    }

    [Theory]
    [InlineData("QuestAlternativeConditionGroups.json")]
    [InlineData("Config.json")]
    public void A_broken_file_warns_and_does_not_stop_other_mods(string brokenFile)
    {
        using var mods = new TempModsRoot();
        mods.AddFile("Broken", "QuestAlternativeConditionGroups.json", OneGroup);
        mods.AddFile("Broken", brokenFile, "{ not json");
        mods.AddFile("Fine", "QuestAlternativeConditionGroups.json", OneGroup);

        var (groups, warnings) = ModAlternativeGroups.ScanRoot(mods.Root);

        Assert.Single(groups);
        var warning = Assert.Single(warnings);
        Assert.Equal(WarningCodes.ModQuestScanFailed, warning.Code);
        Assert.Contains("Broken", warning.Detail);
    }
}
