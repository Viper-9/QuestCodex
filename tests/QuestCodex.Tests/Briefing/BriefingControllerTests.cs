using Microsoft.AspNetCore.Mvc;
using QuestCodex.Briefing;
using QuestCodex.Briefing.Models;
using QuestCodex.Progress.Models;
using QuestCodex.Services;
using QuestCodex.Web.Controllers;
using SPTarkov.Server.Core.Models.Enums;
using static QuestCodex.Tests.Fixtures;
using CatalogModel = QuestCodex.Catalog.Models.Catalog;

namespace QuestCodex.Tests.Briefing;

public class BriefingControllerTests
{
    private sealed class FakeProfiles : IProfileSource
    {
        public Dictionary<string, ProfileLookup> Lookups { get; } = new();
        public IReadOnlyList<ProfileSummary> List() => [];
        public ProfileLookup Find(string id) => Lookups.GetValueOrDefault(id, new ProfileLookup(ProfileLookupState.NotFound, null, false));
    }

    private sealed class FakeCatalog : ICatalogSource
    {
        public IReadOnlySet<string> SupportedLangs { get; init; } = new HashSet<string> { "en", "kr", "es", "es-mx" };
        public List<string> Requested { get; } = [];
        public bool Throw { get; init; }
        public CatalogModel Get(string lang)
        {
            if (Throw) throw new InvalidOperationException("boom");
            Requested.Add(lang);
            var q = B.Quest(Id(1).ToString(), [B.Obj("c", "CounterCreator", B.Prep(mapKeys: ["woods"]), 5, text: "Kill")]);
            return B.Catalog([q], lang);
        }
        public void Rebuild() { }
    }

    private sealed class FakeIndex : IMapIndexSource
    {
        public MapIndex Index => MapIndex.Empty;
    }

    private static BriefingController Controller(FakeProfiles? p = null, FakeCatalog? c = null, RecordingLogger<BriefingController>? log = null)
        => new(p ?? new FakeProfiles(), c ?? new FakeCatalog(), new FakeIndex(), log ?? new RecordingLogger<BriefingController>());

    private static FakeProfiles Ready()
    {
        var pmc = Pmc();
        pmc.Quests!.Add(ProfileQuest(Id(1), QuestStatusEnum.Started));
        var p = new FakeProfiles();
        p.Lookups["p1"] = new ProfileLookup(ProfileLookupState.Ready, pmc, true);
        return p;
    }

    private static string Error(IActionResult r) => Assert.IsType<BriefingController.ErrorBody>(((ObjectResult)r).Value).Error;

    [Fact]
    public void Missing_profile_is_400()
    {
        var r = Controller().Get(null, "kr");
        Assert.IsType<BadRequestObjectResult>(r);
        Assert.Equal("missingProfile", Error(r));
    }

    [Theory]
    [InlineData("kr", "kr")]
    [InlineData("KR", "kr")]
    [InlineData("es-mx", "es-mx")]
    [InlineData("kr-en", "kr")]
    [InlineData("es-xx", "es")]
    [InlineData("xx-yy", "en")]
    [InlineData("", "en")]
    public void Unknown_lang_falls_back_to_the_closest_supported_one(string requested, string expected)
    {
        var catalogs = new FakeCatalog();
        var body = Assert.IsType<BriefingResponse>(Assert.IsType<JsonResult>(Controller(Ready(), catalogs).Get("p1", requested)).Value);
        Assert.Equal((1, expected), (body.SchemaVersion, body.Lang));
        Assert.Equal([expected], catalogs.Requested);
    }

    [Fact]
    public void Unknown_lang_is_400_only_when_en_is_missing_too()
    {
        var catalogs = new FakeCatalog { SupportedLangs = new HashSet<string> { "kr" } };
        var r = Assert.IsType<BadRequestObjectResult>(Controller(Ready(), catalogs).Get("p1", "xx"));
        var body = Assert.IsType<BriefingController.ErrorBody>(r.Value);
        Assert.Equal(("unknownLang", "kr"), (body.Error, string.Join(",", body.Supported!)));
        Assert.Empty(catalogs.Requested);
    }

    [Fact]
    public void Unknown_profile_is_404_and_no_character_is_409()
    {
        Assert.Equal("profileNotFound", Error(Assert.IsType<NotFoundObjectResult>(Controller().Get("nope", "kr"))));
        var p = new FakeProfiles();
        p.Lookups["p1"] = new ProfileLookup(ProfileLookupState.NoCharacter, null, false);
        Assert.Equal("noCharacter", Error(Assert.IsType<ConflictObjectResult>(Controller(p).Get("p1", "kr"))));
    }

    [Fact]
    public void Ok_builds_briefing_from_requested_language()
    {
        var catalogs = new FakeCatalog();
        var json = Assert.IsType<JsonResult>(Controller(Ready(), catalogs).Get("p1", "kr"));
        var body = Assert.IsType<BriefingResponse>(json.Value);
        Assert.Equal((1, "kr", ModMetadata.AssemblyVersion), (body.SchemaVersion, body.Lang, body.QuestCodexVersion));
        Assert.Equal(["kr"], catalogs.Requested);
        Assert.Equal("Kill ×5", Assert.Single(body.Maps.Single(m => m.Key == "woods").Quests).Summary);
    }

    [Fact]
    public void Lang_defaults_to_en()
    {
        var body = Assert.IsType<BriefingResponse>(Assert.IsType<JsonResult>(Controller(Ready()).Get("p1", null)).Value);
        Assert.Equal("en", body.Lang);
    }

    [Fact]
    public void Build_failure_is_500()
    {
        var log = new RecordingLogger<BriefingController>();
        var r = Assert.IsType<ObjectResult>(Controller(Ready(), new FakeCatalog { Throw = true }, log).Get("p1", "kr"));
        Assert.Equal(500, r.StatusCode);
        Assert.Equal("briefingBuildFailed", Error(r));
        var entry = Assert.Single(log.Entries);
        Assert.Equal("Error", entry.Level);
        Assert.Contains("p1/kr", entry.Message);
    }
}
