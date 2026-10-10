using System.Text.Json;

namespace QuestCodex.Briefing;

// maps/index.json 의 한 항목. Key 는 지도 폴더 이름, InternalNames 는 이 지도를 쓰는 로케이션 ID,
// Variants 는 변형 ID 별 폴더.
public sealed record MapIndexEntry(string Key, IReadOnlyList<string> InternalNames, IReadOnlyDictionary<string, string>? Variants = null);

// 웹이 지도 폴더를 고를 때 쓰는 maps/index.json. 브리핑 응답의 mapFolder, hasMap 도 이걸 보고 정한다.
public sealed record MapIndex(IReadOnlyList<MapIndexEntry> Maps)
{
    public static readonly MapIndex Empty = new(Array.Empty<MapIndexEntry>());

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static MapIndex Parse(string json) => Deserialize(json);

    // {} 처럼 "maps" 가 없는 JSON 은 Maps 가 null 로 들어와 FolderFor 에서 NRE 가 나니 빈 index 로 바꾼다.
    // 깨진 JSON 은 여기서 잡지 않고 Load 에서 잡는다.
    private static MapIndex Deserialize(string json)
    {
        var parsed = JsonSerializer.Deserialize<MapIndex>(json, Json);
        return parsed?.Maps is null ? Empty : parsed;
    }

    // 파일이 없거나 깨졌거나 못 읽으면 빈 index 를 돌려준다. 지도 하나 때문에 브리핑 전체가 실패하면 안 된다.
    public static MapIndex Load(string path)
    {
        try
        {
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : Empty;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return Empty;
        }
    }

    // 웹 mapProjection.ts applyMapVariants 와 같다. 서버에 켜진 변형(catalog.MapVariants)이 있으면 Key 만 변형 폴더로 바꾼다.
    public MapIndex WithVariants(IReadOnlyDictionary<string, string> active)
    {
        if (active.Count == 0) return this;
        return new MapIndex(Maps.Select(d =>
        {
            var variant = d.InternalNames.Select(n => active.GetValueOrDefault(n.ToLowerInvariant())).FirstOrDefault(v => v is not null);
            return variant is not null && d.Variants is not null && d.Variants.TryGetValue(variant, out var folder) ? d with { Key = folder } : d;
        }).ToList());
    }

    // 로케이션 ID 로 지도 폴더를 찾는다. 대소문자는 무시. 웹 mapProjection.ts mapKeyFor 와 같다.
    public string? FolderFor(string map)
        => Maps.FirstOrDefault(d => d.InternalNames.Any(n => string.Equals(n, map, StringComparison.OrdinalIgnoreCase)))?.Key;
}
