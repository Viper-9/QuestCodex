using System.Text.RegularExpressions;
using QuestCodex.Web;

namespace QuestCodex.Tests.Web;

public class ViteBundleExportTests
{
    private static readonly string WwwRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "server", "wwwroot"));

    [Fact]
    public void Entry_bundle_exports_mount_unmount_and_notify_or_skips_without_build()
    {
        var manifestPath = Path.Combine(WwwRoot, ".vite", "manifest.json");
        if (!File.Exists(manifestPath))
        {
            return; // skip: xunit 2.9 에는 동적 Skip 이 없다. 빌드 없는 환경에서는 조용히 통과.
        }

        var entry = ViteManifest.Parse(File.ReadAllText(manifestPath), "/questcodex/");
        var jsPath = Path.Combine(WwwRoot, entry.Js.Replace("/questcodex/", "").Replace('/', Path.DirectorySeparatorChar));
        var tail = File.ReadAllText(jsPath)[^400..];

        // Vite 는 export{a as mount,b as unmount,c as notify} 형태로 끝낸다 (preserveEntrySignatures: 'strict')
        var m = Regex.Match(tail, @"export\{([^}]*)\}");
        Assert.True(m.Success, "no export statement at end of bundle — preserveEntrySignatures lost?");
        foreach (var name in new[] { "mount", "unmount", "notify" })
        {
            Assert.Matches($@"\bas {name}\b", m.Groups[1].Value);
        }
    }
}
