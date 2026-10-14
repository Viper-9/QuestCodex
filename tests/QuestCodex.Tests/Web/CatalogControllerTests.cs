using Microsoft.AspNetCore.Mvc;
using QuestCodex.Services;
using QuestCodex.Web.Controllers;
using static QuestCodex.Tests.Fixtures;
// "Catalog" 는 QuestCodex.Catalog 네임스페이스와 이름이 겹쳐 using 만으로는 모호해지므로 별칭을 둔다.
using CatalogModel = QuestCodex.Catalog.Models.Catalog;

namespace QuestCodex.Tests.Web;

public class CatalogControllerTests
{
    private sealed class FakeSource(params string[] langs) : ICatalogSource
    {
        public List<string> Requested { get; } = [];
        public Func<string, CatalogModel> Factory { get; set; } = lang => new CatalogModel("4.1.5", "0.2.0", DateTimeOffset.UnixEpoch, lang, new(), new(), new(), [], [], new(), new(), new(), new(), new());
        public IReadOnlySet<string> SupportedLangs { get; } = langs.ToHashSet();
        public CatalogModel Get(string lang) { Requested.Add(lang); return Factory(lang); }
        public int Rebuilds { get; private set; }
        public void Rebuild() => Rebuilds++;
    }

    private static CatalogController Controller(FakeSource src) => new(src, new RecordingLogger<CatalogController>());

    [Fact]
    public void Defaults_to_en()
    {
        var src = new FakeSource("en", "kr");
        var result = Controller(src).Get(null);

        var json = Assert.IsType<JsonResult>(result);
        Assert.Equal("en", Assert.IsType<CatalogModel>(json.Value).Lang);
        Assert.Equal(["en"], src.Requested);
    }

    [Fact]
    public void Passes_requested_lang()
    {
        var src = new FakeSource("en", "kr");
        var json = Assert.IsType<JsonResult>(Controller(src).Get("kr"));
        Assert.Equal("kr", Assert.IsType<CatalogModel>(json.Value).Lang);
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
        var src = new FakeSource("en", "kr", "es", "es-mx");
        var json = Assert.IsType<JsonResult>(Controller(src).Get(requested));
        Assert.Equal(expected, Assert.IsType<CatalogModel>(json.Value).Lang);
        Assert.Equal([expected], src.Requested);
    }

    [Fact]
    public void Unknown_lang_is_400_only_when_en_is_missing_too()
    {
        var src = new FakeSource("kr");
        var result = Controller(src).Get("xx");

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var body = Assert.IsType<CatalogController.ErrorBody>(bad.Value);
        Assert.Equal("unknownLang", body.Error);
        Assert.Equal(["kr"], body.Supported!);
        Assert.Empty(src.Requested);
    }

    [Fact]
    public void Rebuild_resets_the_source_and_is_204()
    {
        var src = new FakeSource("en");
        var result = Controller(src).Rebuild();

        Assert.IsType<NoContentResult>(result);
        Assert.Equal(1, src.Rebuilds);
    }

    [Fact]
    public void Build_failure_is_500()
    {
        var src = new FakeSource("en") { Factory = _ => throw new InvalidOperationException("boom") };
        var result = Controller(src).Get("en");

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, obj.StatusCode);
        Assert.Equal("catalogBuildFailed", Assert.IsType<CatalogController.ErrorBody>(obj.Value).Error);
    }
}
