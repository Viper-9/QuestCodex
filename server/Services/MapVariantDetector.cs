using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Spt.Mod;

namespace QuestCodex.Services;

/// <summary>
/// 맵을 통째로 바꾸는 모드(09 스펙)가 로드됐는지 본다. 의존성으로 두지 않으므로 DLL 을 참조하지 않고
/// SPT 가 로드한 모드 목록의 GUID 만 대조한다. 결과는 map 키 → 변형 ID 이고, 모드가 없으면 빈 표다.
/// 모드 설치·제거는 서버 재시작이 필요하므로 기동 때 한 번만 판정한다.
/// </summary>
[Injectable(InjectionType.Singleton)]
public class MapVariantDetector
{
    /// <summary>알려진 맵 교체 모드: GUID → (map 키, 변형 ID). 변형 ID 는 스냅샷 variants 와 웹 maps/index.json 의 키다.</summary>
    private static readonly (string Guid, string Map, string Variant)[] Known =
    [
        ("com.manimal.interchange", "interchange", "manimal"),
    ];

    public IReadOnlyDictionary<string, string> Active { get; }

    public MapVariantDetector(IReadOnlyList<SptMod> loadedMods, ISptLogger<MapVariantDetector> logger)
    {
        Active = Detect(loadedMods.Select(m => m.ModMetadata.ModGuid));
        foreach (var (map, variant) in Active) logger.Info($"[QuestCodex] map variant '{variant}' active for {map}");
    }

    public static IReadOnlyDictionary<string, string> Detect(IEnumerable<string?> loadedGuids)
    {
        var guids = loadedGuids.OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Known.Where(k => guids.Contains(k.Guid)).ToDictionary(k => k.Map, k => k.Variant, StringComparer.Ordinal);
    }
}
