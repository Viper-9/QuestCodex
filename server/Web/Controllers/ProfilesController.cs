using Microsoft.AspNetCore.Mvc;
using QuestCodex.Progress;
using QuestCodex.Services;

namespace QuestCodex.Web.Controllers;

[ApiController]
[Route("questcodex/api")]
public class ProfilesController(IProfileSource profiles, ICatalogSource catalogs) : ControllerBase
{
    public sealed record ErrorBody(string Error);

    [HttpGet("profiles")]
    public IActionResult List() => new JsonResult(profiles.List(), QuestCodexJson.Options);

    [HttpGet("profiles/{id}/progress")]
    public IActionResult Progress(string id)
    {
        var lookup = profiles.Find(id);
        switch (lookup.State)
        {
            case ProfileLookupState.NotFound:
                return NotFound(new ErrorBody("profileNotFound"));
            case ProfileLookupState.NoCharacter:
                return Conflict(new ErrorBody("noCharacter"));
        }

        try
        {
            // 상태 계산은 텍스트를 쓰지 않으므로 언어 무관 — en 카탈로그 하나로 충분하다.
            var progress = ProgressBuilder.Build(catalogs.Get("en"), lookup.Pmc!, id, lookup.IsActive);
            return new JsonResult(progress, QuestCodexJson.Options);
        }
        catch (Exception)
        {
            return StatusCode(500, new ErrorBody("progressBuildFailed"));
        }
    }
}
