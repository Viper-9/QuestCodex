using QuestCodex.Briefing;
using SPTarkov.DI.Annotations;

namespace QuestCodex.Services;

// 컨트롤러가 지도 index 를 받아 가는 곳. 테스트에서는 가짜로 바꿔 끼운다.
public interface IMapIndexSource
{
    MapIndex Index { get; }
}

// 모드 폴더의 wwwroot/maps/index.json(웹 빌드 때 복사됨)을 처음 쓸 때 한 번만 읽는다.
// 웹을 빌드하지 않고 배포해서 파일이 없으면 빈 index 를 쓴다. 브리핑은 그대로 나가고 모든 맵이 hasMap=false 가 된다.
[Injectable(InjectionType.Singleton)]
public class MapIndexSource : IMapIndexSource
{
    private readonly Lazy<MapIndex> _index = new(LoadFromModFolder);

    public MapIndex Index => _index.Value;

    public static MapIndex LoadFromModFolder()
    {
        var modDir = Path.GetDirectoryName(typeof(MapIndexSource).Assembly.Location);
        if (modDir is null) return MapIndex.Empty;
        var path = Path.Combine(modDir, "wwwroot", "maps", "index.json");
        return MapIndex.Load(path);
    }
}
