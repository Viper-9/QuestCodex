using Microsoft.AspNetCore.Mvc;
using QuestCodex.Services;
using QuestCodex.Web.Controllers;
// "Catalog" 는 QuestCodex.Catalog 네임스페이스와 이름이 겹쳐 using 만으로는 모호해지므로 별칭을 둔다.
using CatalogModel = QuestCodex.Catalog.Models.Catalog;

namespace QuestCodex.Tests.Web;

public class CatalogControllerTests
{
    private sealed class FakeSource(params string[] langs) : ICatalogSource
    {
        public List<string> Requested { get; } = [];
        public Func<string, CatalogModel> Factory { get; set; } = lang => new CatalogModel("4.1.5", "0.2.0", DateTimeOffset.UnixEpoch, lang, new(), new(), new(), []);
        public IReadOnlySet<string> SupportedLangs { get; } = langs.ToHashSet();
        public CatalogModel Get(string lang) { Requested.Add(lang); return Factory(lang); }
    }

    [Fact]
    public void Defaults_to_en()
    {
        var src = new FakeSource("en", "kr");
        var result = new CatalogController(src).Get(null);

        var json = Assert.IsType<JsonResult>(result);
        Assert.Equal("en", Assert.IsType<CatalogModel>(json.Value).Lang);
        Assert.Equal(["en"], src.Requested);
    }

    [Fact]
    public void Passes_requested_lang()
    {
        var src = new FakeSource("en", "kr");
        var json = Assert.IsType<JsonResult>(new CatalogController(src).Get("kr"));
        Assert.Equal("kr", Assert.IsType<CatalogModel>(json.Value).Lang);
    }

    [Fact]
    public void Unknown_lang_is_400_with_supported_list()
    {
        var src = new FakeSource("en", "kr");
        var result = new CatalogController(src).Get("xx");

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var body = Assert.IsType<CatalogController.ErrorBody>(bad.Value);
        Assert.Equal("unknownLang", body.Error);
        Assert.Equal(["en", "kr"], body.Supported!.Order());
        Assert.Empty(src.Requested);
    }

    [Fact]
    public void Build_failure_is_500()
    {
        var src = new FakeSource("en") { Factory = _ => throw new InvalidOperationException("boom") };
        var result = new CatalogController(src).Get("en");

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, obj.StatusCode);
        Assert.Equal("catalogBuildFailed", Assert.IsType<CatalogController.ErrorBody>(obj.Value).Error);
    }
}
