using System.Diagnostics;
using System.Text.Json;
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

        // 한글화 모드가 붙인 kr-en 같은 culture 도 그대로 넘어오므로 가까운 언어로 대체한다.
        // 실제로 쓴 언어는 응답의 lang 에 들어간다.
        var resolved = LangFallback.Resolve(lang, catalogs.SupportedLangs, logger);
        if (resolved is null)
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
            // 단계별 소요 시간(16 catalog-warmup 스펙 §6). 직렬화도 재려고 JsonResult 대신 여기서 바이트로 만든다.
            var t0 = Stopwatch.GetTimestamp();
            // 진행 상태는 텍스트를 쓰지 않으므로 요청 언어 카탈로그 하나로 둘 다 만든다.
            var catalog = catalogs.Get(resolved);
            var t1 = Stopwatch.GetTimestamp();
            var progress = ProgressBuilder.Build(catalog, lookup.Pmc!, profile, lookup.IsActive);
            var t2 = Stopwatch.GetTimestamp();
            var body = BriefingBuilder.Build(catalog, progress, mapIndex.Index, ModMetadata.AssemblyVersion, DateTimeOffset.UtcNow);
            var t3 = Stopwatch.GetTimestamp();
            var bytes = JsonSerializer.SerializeToUtf8Bytes(body, QuestCodexJson.Options);
            var t4 = Stopwatch.GetTimestamp();
            LogTimings(resolved, t0, t1, t2, t3, t4);
            return File(bytes, "application/json; charset=utf-8");
        }
        catch (Exception ex)
        {
            // 원인은 서버 로그에만 남기고 응답에는 넣지 않는다.
            logger.Error($"[QuestCodex] briefing build failed for {profile}/{resolved}", ex);
            return StatusCode(500, new ErrorBody("briefingBuildFailed"));
        }
    }

    /// <summary>이 합계 이상이면 Info, 아니면 Debug. 기본 설정에서 Debug 는 로그 파일에 남지 않아 사용자 로그엔 느린 요청만 보인다.</summary>
    internal const double SlowMs = 500;

    private void LogTimings(string lang, long t0, long t1, long t2, long t3, long t4)
    {
        static double Ms(long from, long to) => Stopwatch.GetElapsedTime(from, to).TotalMilliseconds;
        var message = $"[QuestCodex] briefing {lang}: catalog {Ms(t0, t1):F0} ms, progress {Ms(t1, t2):F0} ms, build {Ms(t2, t3):F0} ms, serialize {Ms(t3, t4):F0} ms";
        if (Ms(t0, t4) >= SlowMs) logger.Info(message);
        else logger.Debug(message);
    }
}
