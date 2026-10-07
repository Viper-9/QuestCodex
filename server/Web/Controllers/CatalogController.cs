using Microsoft.AspNetCore.Mvc;
using QuestCodex.Services;

namespace QuestCodex.Web.Controllers;

[ApiController]
[Route("questcodex/api")]
public class CatalogController(ICatalogSource catalogs) : ControllerBase
{
    public sealed record ErrorBody(string Error, IReadOnlyList<string>? Supported = null);

    [HttpGet("catalog")]
    public IActionResult Get([FromQuery] string? lang)
    {
        var requested = string.IsNullOrWhiteSpace(lang) ? "en" : lang;
        if (!catalogs.SupportedLangs.Contains(requested))
        {
            return BadRequest(new ErrorBody("unknownLang", catalogs.SupportedLangs.Order(StringComparer.Ordinal).ToList()));
        }

        try
        {
            return new JsonResult(catalogs.Get(requested), QuestCodexJson.Options);
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
