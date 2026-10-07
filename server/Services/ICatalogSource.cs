namespace QuestCodex.Services;

/// <summary>컨트롤러가 의존하는 카탈로그 공급자. 테스트에서 가짜로 바꾼다.</summary>
public interface ICatalogSource
{
    IReadOnlySet<string> SupportedLangs { get; }

    /// <summary>언어별 카탈로그. 빌드 실패 시 예외를 던지며, 실패한 언어는 캐시에 남지 않는다.</summary>
    // "Catalog" 는 QuestCodex.Catalog 네임스페이스와 이름이 겹쳐 using 만으로는 모호해지므로 전체 이름을 쓴다.
    QuestCodex.Catalog.Models.Catalog Get(string lang);

    /// <summary>메모리·디스크 캐시를 버린다. 다음 Get 이 처음부터 다시 만든다(13 catalog-cache 스펙 §4).</summary>
    void Rebuild();
}
