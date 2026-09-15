using QuestCodex.Web;

namespace QuestCodex.Tests;

public class ViteManifestTests
{
    // Vite 7 이 build.manifest=true, rollupOptions.input='src/main.tsx' 로 생성하는 형태.
    private const string SampleManifest = """
    {
      "src/main.tsx": {
        "file": "assets/main-BxK3d9Qe.js",
        "name": "main",
        "src": "src/main.tsx",
        "isEntry": true,
        "css": ["assets/main-C7fA1b2c.css"]
      },
      "_vendor-DeadBeef.js": {
        "file": "assets/vendor-DeadBeef.js",
        "name": "vendor"
      }
    }
    """;

    [Fact]
    public void Parse_picks_entry_chunk_and_prefixes_base_url()
    {
        var entry = ViteManifest.Parse(SampleManifest, "/questcodex/");

        Assert.Equal("/questcodex/assets/main-BxK3d9Qe.js", entry.Js);
        Assert.Equal(["/questcodex/assets/main-C7fA1b2c.css"], entry.Css);
    }

    [Fact]
    public void Parse_tolerates_entry_without_css()
    {
        const string noCss = """{ "src/main.tsx": { "file": "assets/main-1.js", "isEntry": true } }""";

        var entry = ViteManifest.Parse(noCss, "/questcodex/");

        Assert.Equal("/questcodex/assets/main-1.js", entry.Js);
        Assert.Empty(entry.Css);
    }

    [Fact]
    public void Parse_throws_when_no_entry_chunk()
    {
        const string noEntry = """{ "_vendor.js": { "file": "assets/vendor.js" } }""";

        var ex = Assert.Throws<InvalidOperationException>(() => ViteManifest.Parse(noEntry, "/questcodex/"));
        Assert.Contains("isEntry", ex.Message);
    }
}
