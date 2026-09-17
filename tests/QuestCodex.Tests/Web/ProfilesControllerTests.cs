using Microsoft.AspNetCore.Mvc;
using QuestCodex.Progress.Models;
using QuestCodex.Services;
using QuestCodex.Web.Controllers;
using SPTarkov.Server.Core.Models.Eft.Common;
using static QuestCodex.Tests.Fixtures;
// "Catalog" 는 QuestCodex.Catalog 네임스페이스와 이름이 겹쳐 using 만으로는 모호해지므로 별칭을 둔다.
using CatalogModel = QuestCodex.Catalog.Models.Catalog;

namespace QuestCodex.Tests.Web;

public class ProfilesControllerTests
{
    private sealed class FakeProfiles : IProfileSource
    {
        public List<ProfileSummary> Summaries { get; } = [];
        public Dictionary<string, ProfileLookup> Lookups { get; } = new();
        public IReadOnlyList<ProfileSummary> List() => Summaries;
        public ProfileLookup Find(string id) => Lookups.GetValueOrDefault(id, new ProfileLookup(ProfileLookupState.NotFound, null, false));
    }

    private sealed class FakeCatalog : ICatalogSource
    {
        public IReadOnlySet<string> SupportedLangs { get; } = new HashSet<string> { "en" };
        public CatalogModel Get(string lang) => new("4.1.5", "0.2.0", DateTimeOffset.UnixEpoch, lang, new(), new(), new(), []);
    }

    [Fact]
    public void List_returns_summaries_as_json()
    {
        var profiles = new FakeProfiles();
        profiles.Summaries.Add(new ProfileSummary("p1", "nick", 12, "Usec", true, true, null));

        var json = Assert.IsType<JsonResult>(new ProfilesController(profiles, new FakeCatalog()).List());

        Assert.Equal("p1", Assert.Single(Assert.IsAssignableFrom<IEnumerable<ProfileSummary>>(json.Value)).Id);
    }

    [Fact]
    public void Progress_404_when_missing()
    {
        var result = new ProfilesController(new FakeProfiles(), new FakeCatalog()).Progress("nope");
        var nf = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal("profileNotFound", Assert.IsType<ProfilesController.ErrorBody>(nf.Value).Error);
    }

    [Fact]
    public void Progress_409_when_no_character()
    {
        var profiles = new FakeProfiles();
        profiles.Lookups["p1"] = new ProfileLookup(ProfileLookupState.NoCharacter, null, false);

        var result = new ProfilesController(profiles, new FakeCatalog()).Progress("p1");

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal("noCharacter", Assert.IsType<ProfilesController.ErrorBody>(conflict.Value).Error);
    }

    [Fact]
    public void Progress_200_builds_from_en_catalog()
    {
        var profiles = new FakeProfiles();
        profiles.Lookups["p1"] = new ProfileLookup(ProfileLookupState.Ready, Pmc(level: 7), true);

        var json = Assert.IsType<JsonResult>(new ProfilesController(profiles, new FakeCatalog()).Progress("p1"));

        var body = Assert.IsType<ProfileProgress>(json.Value);
        Assert.Equal("p1", body.ProfileId);
        Assert.Equal(7, body.Level);
        Assert.True(body.IsActive);
    }

    [Fact]
    public void Progress_500_when_builder_throws()
    {
        var profiles = new FakeProfiles();
        profiles.Lookups["p1"] = new ProfileLookup(ProfileLookupState.Ready, null, true);   // Ready 인데 Pmc null → ArgumentNullException

        var result = new ProfilesController(profiles, new FakeCatalog()).Progress("p1");

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, obj.StatusCode);
        Assert.Equal("progressBuildFailed", Assert.IsType<ProfilesController.ErrorBody>(obj.Value).Error);
    }
}
