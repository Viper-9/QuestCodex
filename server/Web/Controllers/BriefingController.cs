using Microsoft.AspNetCore.Mvc;
using QuestCodex.Briefing;
using QuestCodex.Progress;
using QuestCodex.Services;
using SPTarkov.Common.Models.Logging;

namespace QuestCodex.Web.Controllers;

// 인게임 모드 Briefing 이 받아 가는 레이드 준비 데이터.
// 클라이언트는 SPT RequestHandler 로 이 주소를 그대로 부를 수 있다(0단계에서 실제로 확인함).
// 레이드 중에는 다시 요청하지 않기 때문에 모든 맵을 한 응답에 담는다.
[ApiController]
[Route("questcodex/api")]
public class BriefingController(IProfileSource profiles, ICatalogSource catalogs, IMapIndexSource mapIndex,
    ISptLogger<BriefingController> logger) : ControllerBase
{
    public sealed record ErrorBody(string Error, IReadOnlyList<string>? Supported = null);

    [HttpGet("briefing")]
    public IActionResult Get([FromQuery] string? profile, [FromQuery] string? lang)
    {
        if (string.IsNullOrWhiteSpace(profile)) return BadRequest(new ErrorBody("missingProfile"));

        var requested = string.IsNullOrWhiteSpace(lang) ? "en" : lang;
        if (!catalogs.SupportedLangs.Contains(requested))
        {
            return BadRequest(new ErrorBody("unknownLang", catalogs.SupportedLangs.Order(StringComparer.Ordinal).ToList()));
        }

        var lookup = profiles.Find(profile);
        switch (lookup.State)
        {
            case ProfileLookupState.NotFound:
                return NotFound(new ErrorBody("profileNotFound"));
            case ProfileLookupState.NoCharacter:
                return Conflict(new ErrorBody("noCharacter"));
        }

        try
        {
            // 진행 상태는 텍스트를 쓰지 않으므로 요청 언어 카탈로그 하나로 둘 다 만든다.
            var catalog = catalogs.Get(requested);
            var progress = ProgressBuilder.Build(catalog, lookup.Pmc!, profile, lookup.IsActive);
            var body = BriefingBuilder.Build(catalog, progress, mapIndex.Index, ModMetadata.AssemblyVersion, DateTimeOffset.UtcNow);
            return new JsonResult(body, QuestCodexJson.Options);
        }
        catch (Exception ex)
        {
            // 원인은 서버 로그에만 남기고 응답에는 넣지 않는다.
            logger.Error($"[QuestCodex] briefing build failed for {profile}/{requested}", ex);
            return StatusCode(500, new ErrorBody("briefingBuildFailed"));
        }
    }
}
