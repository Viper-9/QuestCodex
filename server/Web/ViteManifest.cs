using System.Text.Json;
using SPTarkov.DI.Annotations;

namespace QuestCodex.Web;

/// <summary>Vite 엔트리 청크의 JS/CSS 절대 URL.</summary>
public sealed record ViteEntry(string Js, IReadOnlyList<string> Css);

/// <summary>
/// wwwroot/.vite/manifest.json 을 읽어 Razor 호스트 페이지가 삽입할 스크립트/스타일 URL을 제공한다.
/// 서버 프로세스 내부에서 typed 값이 필요하므로 디스크에서 직접 읽는다 (HTTP로 노출되는지 여부와 무관).
/// 실측 결과 SPT 4.1.5의 정적 파일 미들웨어는 점 접두어 폴더를 차단하지 않으므로,
/// wwwroot/ 아래 무엇을 두든 공개 서빙된다고 가정할 것 — 민감한 파일을 두지 말 것.
/// </summary>
[Injectable(InjectionType.Singleton)]
public class ViteManifest
{
    public const string BaseUrl = "/questcodex/";

    private readonly Lazy<ViteEntry> _entry = new(LoadFromModFolder);

    public ViteEntry Entry => _entry.Value;

    public static ViteEntry Parse(string manifestJson, string baseUrl)
    {
        using var doc = JsonDocument.Parse(manifestJson);
        foreach (var chunk in doc.RootElement.EnumerateObject())
        {
            if (!chunk.Value.TryGetProperty("isEntry", out var isEntry) || !isEntry.GetBoolean())
            {
                continue;
            }

            var js = baseUrl + chunk.Value.GetProperty("file").GetString();
            var css = chunk.Value.TryGetProperty("css", out var cssArray)
                ? cssArray.EnumerateArray().Select(c => baseUrl + c.GetString()).ToList()
                : [];
            return new ViteEntry(js, css);
        }

        throw new InvalidOperationException("Vite manifest has no chunk with isEntry: true");
    }

    private static ViteEntry LoadFromModFolder()
    {
        var modDir = Path.GetDirectoryName(typeof(ViteManifest).Assembly.Location)
                     ?? throw new InvalidOperationException("Cannot resolve QuestCodex mod folder");
        var manifestPath = Path.Combine(modDir, "wwwroot", ".vite", "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException(
                $"QuestCodex web build not found at {manifestPath}. Run tools/package.ps1 (npm build → wwwroot copy) first.",
                manifestPath);
        }

        return Parse(File.ReadAllText(manifestPath), BaseUrl);
    }
}
