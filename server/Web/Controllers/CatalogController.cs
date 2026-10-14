using Microsoft.AspNetCore.Mvc;
using QuestCodex.Services;
using SPTarkov.Common.Models.Logging;

namespace QuestCodex.Web.Controllers;

[ApiController]
[Route("questcodex/api")]
public class CatalogController(ICatalogSource catalogs, ISptLogger<CatalogController> logger) : ControllerBase
{
    public sealed record ErrorBody(string Error, IReadOnlyList<string>? Supported = null);

    [HttpGet("catalog")]
    public IActionResult Get([FromQuery] string? lang)
    {
        // 모르는 언어는 가까운 언어로 대체한다. 실제로 쓴 언어는 응답의 lang 에 그대로 들어간다.
        var resolved = LangFallback.Resolve(lang, catalogs.SupportedLangs, logger);
        if (resolved is null)
        {
            return BadRequest(new ErrorBody("unknownLang", catalogs.SupportedLangs.Order(StringComparer.Ordinal).ToList()));
        }

        try
        {
            return new JsonResult(catalogs.Get(resolved), QuestCodexJson.Options);
        }
        catch (Exception)
        {
            return StatusCode(500, new ErrorBody("catalogBuildFailed"));
        }
    }

    /// <summary>"데이터 다시 만들기" 버튼. 캐시만 버리고 바로 돌아온다 — 웹이 이어서 GET catalog 로 새로 받는다.</summary>
    [HttpPost("catalog/rebuild")]
    public IActionResult Rebuild()
    {
        catalogs.Rebuild();
        return NoContent();
    }
}
