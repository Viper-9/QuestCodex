using System.Text.Json;
using QuestCodex.Briefing;
using QuestCodex.Catalog;
using QuestCodex.Catalog.Models;
using static QuestCodex.Tests.Briefing.B;
using CatalogModel = QuestCodex.Catalog.Models.Catalog;

namespace QuestCodex.Tests.Briefing;

public class QuestChainsTests
{
    private static QuestRequirement Done(string id) => new(id, ["Success"], 0, true);

    private static CatalogQuest Q(string id, params Requirement[] reqs) => Quest(id, [], requirements: reqs);

    private static IReadOnlySet<string> Of(string goal, params CatalogQuest[] quests) => QuestChains.PrerequisitesOf(B.Catalog(quests), goal);

    [Fact]
    public void Follows_a_chain_back_to_the_start()
        => Assert.Equal(["a", "b", "c"], Of("g", Q("g", Done("c")), Q("c", Done("b")), Q("b", Done("a")), Q("a")).Order());

    [Fact]
    public void Collects_every_branch_once()
        => Assert.Equal(["a", "b", "c", "root"],
            Of("g", Q("g", Done("a"), Done("b")), Q("a", Done("root")), Q("b", Done("root"), Done("c")), Q("c"), Q("root")).Order());

    [Fact]
    public void Stops_on_a_cycle()
        => Assert.Equal(["a", "b"], Of("g", Q("g", Done("a")), Q("a", Done("b")), Q("b", Done("a"))).Order());

    [Fact]
    public void Excludes_the_goal_even_when_a_cycle_leads_back_to_it()
        => Assert.Equal(["a"], Of("g", Q("g", Done("a")), Q("a", Done("g"))).Order());

    [Fact]
    public void Ignores_requirements_that_do_not_need_success()
        => Assert.Equal(["a"], Of("g",
            Q("g", Done("a"), new QuestRequirement("started", ["Started"], 0, true), new QuestRequirement("failed", ["Fail"], 0, true),
                new LevelRequirement(10, ">=")),
            Q("a"), Q("started"), Q("failed")).Order());

    [Fact]
    public void Accepts_success_among_other_statuses()
        => Assert.Equal(["a"], Of("g", Q("g", new QuestRequirement("a", ["Fail", "Success"], 0, true)), Q("a")).Order());

    [Fact]
    public void Skips_quests_missing_from_the_catalog()
        => Assert.Equal(["a"], Of("g", Q("g", Done("a"), Done("gone")), Q("a", Done("gone2"))).Order());

    [Fact]
    public void Missing_goal_has_no_prerequisites()
        => Assert.Empty(Of("g", Q("a")));

    // 바닐라 녹화 카탈로그(web/fixtures/vanilla, gitignore 대상)로 센 기준 숫자. 카파 251 은 웹 카파 트래커 바닐라와 같다.
    [Fact]
    public void Vanilla_fixture_counts()
    {
        var path = TestPaths.RepoRoot is { } root ? Path.Combine(root, "web", "fixtures", "vanilla", "catalog", "en.json") : null;
        if (path is null || !File.Exists(path)) return; // 녹화 없으면 건너뜀

        var catalog = JsonSerializer.Deserialize<CatalogModel>(File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var kappa = QuestChains.PrerequisitesOf(catalog, CatalogBuilder.CollectorId);
        var lightkeeper = QuestChains.PrerequisitesOf(catalog, QuestChains.InformationSourceId);
        Assert.Equal(251, kappa.Count);
        Assert.Equal(100, lightkeeper.Count);
        Assert.Equal(90, kappa.Intersect(lightkeeper).Count());
    }
}
