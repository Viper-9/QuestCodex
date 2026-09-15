using System.Reflection;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Web;

namespace QuestCodex;

/// <summary>
/// SPT가 리플렉션으로 찾는 모드 메타데이터. IModBlazorMetadata 구현으로 wwwroot/ 정적 서빙,
/// MVC 컨트롤러, Razor 페이지 등록, 랜딩 페이지 모드 카드가 활성화된다.
/// </summary>
public record ModMetadata : IModMetadata, IModBlazorMetadata
{
    /// <summary>Directory.Build.props 의 &lt;Version&gt; 이 어셈블리 InformationalVersion 으로 들어온다.</summary>
    public static string AssemblyVersion { get; } =
        typeof(ModMetadata).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "0.0.0";

    public string ModGuid { get; init; } = "com.viper.questcodex";
    public string Name { get; init; } = "QuestCodex";
    public string Author { get; init; } = "Viper-9";
    public List<string>? Contributors { get; init; }
    public SemanticVersioning.Version Version { get; init; } = new(AssemblyVersion);
    public SemanticVersioning.Range SptVersion { get; init; } = new("~4.1.2");
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public string? Url { get; init; } = "https://github.com/Viper-9/QuestCodex";
    public string License { get; init; } = "CC-BY-NC-ND-4.0";
    public bool HasPrepatcher { get; init; } = false;

    // IModBlazorMetadata — wwwroot/ 가 http://host/questcodex/... 로 서빙된다.
    public string? WWWRootUrl { get; init; } = "questcodex";
    public string? HomePage { get; init; } = "/questcodex";
    public string? HomePageDescription { get; init; } =
        "Quest wiki and progress tracker for every quest loaded on this server.";
}
