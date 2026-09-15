using Microsoft.AspNetCore.Mvc;
using SPTarkov.Server.Core.Utils;

namespace QuestCodex.Web.Controllers;

public sealed record PingResponse(string Mod, string Version, string Spt);

/// <summary>
/// 뼈대 검증용 엔드포인트. SPT가 IModBlazorMetadata 모드의 어셈블리를 MVC ApplicationPart 로
/// 등록하므로 [ApiController] 만 붙이면 라우팅된다.
/// </summary>
[ApiController]
[Route("questcodex/api")]
public class PingController : ControllerBase
{
    [HttpGet("ping")]
    public ActionResult<PingResponse> Ping() =>
        Ok(new PingResponse(
            Mod: "QuestCodex",
            Version: ModMetadata.AssemblyVersion,
            Spt: ProgramStatics.SPT_VERSION().ToString()));
}
