using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Servers.Http;

namespace QuestCodex.Services;

/// <summary>
/// 레이드 종료(/client/match/local/end)를 감지해 ProfileUpdated 를 발행한다.
///
/// 감지 수단은 IHttpListener 다. Router.OnAfterAction 은 쓸 수 없다 — StaticRouter 파생 타입이
/// [Injectable(InjectionType.Transient)] 로 등록돼 있어서 IEnumerable&lt;StaticRouter&gt; 를 주입받으면
/// 실제 요청을 처리하는 인스턴스가 아니라 그 자리에서 새로 만들어진 사본을 받는다. 구독은 성공하지만
/// 이벤트는 영원히 오지 않는다 (SPT 4.1.5 실측).
///
/// IHttpListener 는 조건이 정반대다. HttpServer 가 [Injectable(InjectionType.Singleton)] 이라
/// IEnumerable&lt;IHttpListener&gt; 를 부팅 때 한 번만 해석해 고정으로 들고 있고, SPTarkov.DI 의
/// HandleSingletonRegistration 이 인스턴스를 캐시하므로 여기 들어가는 객체는 Blazor 페이지가
/// 주입받는 객체와 동일하다.
///
/// 격리 규칙: (1) CanHandle 은 예외를 절대 HttpServer 로 전파하지 않는다 — FirstOrDefault 의 술어라서
/// 던지면 서버의 모든 요청이 죽는다. (2) 항상 false 를 반환해 처리는 SPT 에 넘기고 관찰만 한다.
/// (3) 발행은 Task.Run 으로 넘겨 호출 스레드를 즉시 반환한다.
/// </summary>
// TypePriority 는 반드시 SptHttpListener(int.MaxValue) 보다 작아야 한다. DependencyInjectionHandler.InjectAll
// 이 TypePriority 오름차순으로 등록하고 IEnumerable 열거 순서가 곧 등록 순서이므로, 이 값이 커지면
// SptHttpListener 가 먼저 true 를 반환해 우리 CanHandle 이 호출되지 않는다.
[Injectable(InjectionType.Singleton, 1000)]
public class RaidEndNotifier : IHttpListener
{
    public const string RaidEndUrl = "/client/match/local/end";

    /// <summary>SPT 가 세션 식별에 쓰는 쿠키 이름 (HttpServer.HandleRequestAsync 와 동일).</summary>
    private const string SessionCookie = "PHPSESSID";

    private static readonly TimeSpan DebounceWindow = TimeSpan.FromSeconds(1);

    private readonly ISptLogger<RaidEndNotifier> _logger;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, long> _lastRaised = new(StringComparer.Ordinal);

    /// <summary>CanHandle 실패 로그를 1회로 제한하는 플래그. 0 = 아직 안 찍음.</summary>
    private int _observeFailureLogged;

    public event Action<string>? ProfileUpdated;

    /// <summary>SPT DI 용.</summary>
    public RaidEndNotifier(ISptLogger<RaidEndNotifier> logger)
        : this(logger, null)
    {
    }

    /// <summary>
    /// 테스트용. TimeProvider 를 주입한다.
    /// internal 인 이유: ASP.NET Core 는 TimeProvider 를 기본 등록하므로 public 이면 DI 가 두 생성자 중
    /// 어느 것을 쓸지 모호해진다. 테스트는 csproj 의 InternalsVisibleTo(QuestCodex.Tests) 로 접근한다.
    /// </summary>
    internal RaidEndNotifier(ISptLogger<RaidEndNotifier> logger, TimeProvider? time)
    {
        _logger = logger;
        _time = time ?? TimeProvider.System;

        // 스펙 §5 는 기동 로그를 Debug 로 규정하지만 이 한 줄만 Info 다. SPT 기본 로그 레벨이
        // Information 이라 Debug 는 보이지 않는데, 이 기능의 지배적 실패 모드가 "예외 없이 조용히
        // 아무 일도 안 일어남"(=리스너 체인에 합류하지 못함)이라 그 진단이 기본 설치에서 보여야 한다.
        // 프로세스당 1줄이라 노이즈는 없다.
        _logger.Info($"[QuestCodex] raid-end listener active (priority 1000), watching {RaidEndUrl}");
    }

    /// <summary>
    /// 모든 요청이 여기를 지나간다. 레이드 종료면 응답 완료 콜백만 걸어두고 항상 false 를 반환한다.
    /// OnCompleted 시점을 쓰는 이유: CanHandle 은 SPT 가 레이드 종료를 처리하기 <em>전</em>에 불리므로
    /// 여기서 바로 발행하면 아직 갱신되지 않은 프로필을 읽게 된다.
    /// </summary>
    public bool CanHandle(HttpContext context)
    {
        try
        {
            if (!string.Equals(context.Request.Path.Value, RaidEndUrl, StringComparison.Ordinal))
            {
                return false;
            }

            // MongoId 검증까지 하는 이유: (1) _lastRaised 의 키 공간을 유효 프로필 id 로 제한해
            // 임의 쿠키로 딕셔너리를 무한히 키우지 못하게 하고, (2) ProfileUpdated 가 언제나 형식이
            // 유효한 id 만 실어 나른다는 계약을 만든다. null/빈 문자열도 여기서 함께 걸러진다.
            if (!context.Request.Cookies.TryGetValue(SessionCookie, out var profileId)
                || !MongoId.IsValidMongoId(profileId))
            {
                return false;
            }

            context.Response.OnCompleted(() =>
            {
                RaiseDebounced(profileId);
                return Task.CompletedTask;
            });
        }
        catch (Exception ex)
        {
            // 술어에서 던지면 HttpServer 의 FirstOrDefault 가 터져 서버 전체 요청이 죽는다.
            // 이 메서드는 모든 요청에서 호출되므로, 구조적 실패라면 요청당 한 줄씩 롤링 로그를
            // 태워 다른 진단을 밀어낸다. 첫 1회만 남긴다.
            if (Interlocked.Exchange(ref _observeFailureLogged, 1) == 0)
            {
                _logger.Warning($"[QuestCodex] raid-end observe failed (further occurrences suppressed): {ex.Message}", ex);
            }
        }

        return false;
    }

    /// <summary>
    /// 도달 불가 — <see cref="CanHandle"/> 이 항상 false 를 반환하므로 HttpServer 가 이 리스너를
    /// 고르지 않는다. 그럼에도 도달했다면 그 불변식이 깨진 것이고, 해당 요청은 SPT 가 처리하지
    /// 못한 채 빈 200 으로 증발한다(레이드 종료라면 게임 진행 손실). 조용히 삼키지 않고 남긴다.
    /// </summary>
    public Task HandleAsync(MongoId sessionId, HttpContext context, CancellationToken cancellationToken = default)
    {
        _logger.Error($"[QuestCodex] BUG: HandleAsync reached for {context.Request.Path.Value} — the request was swallowed instead of being handled by SPT");
        return Task.CompletedTask;
    }

    private void RaiseDebounced(string profileId)
    {
        try
        {
            var now = _time.GetTimestamp();

            // 디바운스는 best-effort 다. 이 읽기-비교-쓰기 3단계는 원자적이지 않으므로 같은 프로필의
            // OnCompleted 콜백이 동시에 돌면 둘 다 통과할 수 있다. 중복 발행의 실제 대가는 브라우저가
            // REST 를 한 번 더 부르는 것뿐이고, 같은 프로필의 레이드가 1초 안에 두 번 끝나는 상황은
            // 존재하지 않으므로 락을 도입하지 않는다.
            var last = _lastRaised.GetOrAdd(profileId, long.MinValue);
            if (last != long.MinValue && _time.GetElapsedTime(last, now) < DebounceWindow)
            {
                _logger.Debug($"[QuestCodex] raid end for {profileId} suppressed by debounce");
                return;
            }

            _lastRaised[profileId] = now;
            _ = Task.Run(() => Raise(profileId));
        }
        catch (Exception ex)
        {
            _logger.Warning($"[QuestCodex] raid-end dispatch failed: {ex.Message}", ex);
        }
    }

    private void Raise(string profileId)
    {
        try
        {
            // 구독자 수까지 남기는 이유: 0 이면 "서버는 정상인데 브라우저가 안 갱신됨"이 즉시 구분된다
            // (탭이 안 열려 있거나 Index.razor 의 구독이 해제된 경우).
            _logger.Debug($"[QuestCodex] raid end -> ProfileUpdated({profileId}), {ProfileUpdated?.GetInvocationList().Length ?? 0} subscriber(s)");
            ProfileUpdated?.Invoke(profileId);
        }
        catch (Exception ex)
        {
            _logger.Warning($"[QuestCodex] ProfileUpdated subscriber failed: {ex.Message}", ex);
        }
    }
}
