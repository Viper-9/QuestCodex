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
}
