using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Time.Testing;
using QuestCodex.Services;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Servers.Http;
using static QuestCodex.Tests.Fixtures;

namespace QuestCodex.Tests.Services;

public class RaidEndNotifierTests
{
    /// <summary>
    /// 기본 HttpResponseFeature 의 OnCompleted 는 no-op 이라 콜백을 되돌려 실행할 수 없다.
    /// 등록된 콜백을 모아뒀다가 테스트가 직접 "응답 완료"를 흉내낼 수 있게 한다.
    ///
    /// 실물과 다른 점 두 가지 (현재는 무해하지만 콜백을 늘릴 때 주의):
    /// (1) <see cref="HasStarted"/> 가 항상 false — Kestrel 은 본문 쓰기 시작 후 true 가 된다.
    /// (2) 콜백을 등록 순서(FIFO)로 실행 — Kestrel 은 역순(LIFO)이다.
    /// SUT 는 콜백을 1개만 등록하고 HasStarted 를 읽지 않으므로 둘 다 결과에 영향이 없다.
    /// </summary>
    private sealed class RecordingResponseFeature : IHttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _completed = [];

        public int StatusCode { get; set; } = 200;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = Stream.Null;
        public bool HasStarted => false;

        public void OnStarting(Func<object, Task> callback, object state) { }
        public void OnCompleted(Func<object, Task> callback, object state) => _completed.Add((callback, state));

        public async Task CompleteAsync()
        {
            foreach (var (callback, state) in _completed)
            {
                await callback(state);
            }
        }
    }

    private static (HttpContext Context, RecordingResponseFeature Response) Request(string url, string? sessionId)
    {
        var headers = new HeaderDictionary();
        if (sessionId is not null)
        {
            headers["Cookie"] = $"PHPSESSID={sessionId}";
        }

        var response = new RecordingResponseFeature();
        var features = new FeatureCollection();
        features.Set<IHttpRequestFeature>(new HttpRequestFeature { Path = url, Headers = headers });
        features.Set<IHttpResponseFeature>(response);

        return (new DefaultHttpContext(features), response);
    }

    private static (RaidEndNotifier Sut, RecordingLogger<RaidEndNotifier> Log, FakeTimeProvider Time) Make()
    {
        var log = new RecordingLogger<RaidEndNotifier>();
        var time = new FakeTimeProvider();
        return (new RaidEndNotifier(log, time), log, time);
    }

    /// <summary>요청 한 건을 리스너에 통과시키고 응답 완료까지 흉내낸다. 반환값은 CanHandle 의 결과.</summary>
    private static async Task<bool> Dispatch(RaidEndNotifier sut, string url, string? sessionId)
    {
        var (context, response) = Request(url, sessionId);
        var handled = sut.CanHandle(context);
        await response.CompleteAsync();
        return handled;
    }

    /// <summary>
    /// <paramref name="act"/> 를 실행하고 <paramref name="expected"/> 개의 ProfileUpdated 가 도착할
    /// 때까지 기다린다. 발행이 Task.Run 으로 넘어가므로 동기 대기로는 관측할 수 없다.
    ///
    /// 기대 개수를 세는 이유: 고정 지연으로 "다 왔겠지" 하고 넘기면 CI 부하 시 스레드풀 지연으로
    /// 깨진다. 기대치까지는 넉넉히 기다리고, 그 뒤 짧게 한 번 더 기다려 초과 발행을 잡는다.
    /// 구독은 반드시 해제한다 — 한 테스트에서 두 번 호출하면 죽은 구독자가 남는다.
    /// </summary>
    private static async Task<List<string>> Collect(RaidEndNotifier sut, int expected, Func<Task> act)
    {
        var received = new List<string>();
        using var arrived = new CountdownEvent(expected);

        void OnUpdated(string id)
        {
            lock (received) received.Add(id);
            if (!arrived.IsSet) arrived.Signal();
        }

        sut.ProfileUpdated += OnUpdated;
        try
        {
            await act();
            arrived.Wait(TimeSpan.FromSeconds(5));
            await Task.Delay(50);   // 기대치를 넘는 발행이 있다면 도착할 시간
        }
        finally
        {
            sut.ProfileUpdated -= OnUpdated;
        }

        lock (received) return [.. received];
    }

    [Fact]
    public async Task Raises_profile_updated_for_raid_end_url()
    {
        var (sut, _, _) = Make();
        var received = await Collect(sut, 1, () => Dispatch(sut, RaidEndNotifier.RaidEndUrl, Id(1).ToString()));
        Assert.Equal([Id(1).ToString()], received);
    }

    /// <summary>
    /// 이 리스너는 관찰만 한다. true 를 반환하면 HttpServer 가 SptHttpListener 대신 우리에게 요청을
    /// 넘겨버려 레이드 종료 처리 자체가 사라진다.
    /// </summary>
    [Fact]
    public async Task Never_claims_the_request()
    {
        var (sut, _, _) = Make();

        Assert.False(await Dispatch(sut, RaidEndNotifier.RaidEndUrl, Id(1).ToString()));
        Assert.False(await Dispatch(sut, "/client/game/keepalive", Id(1).ToString()));
        Assert.False(await Dispatch(sut, "/questcodex/api/catalog", null));
    }

    [Fact]
    public async Task Ignores_other_urls()
    {
        var (sut, _, _) = Make();
        var received = new List<string>();
        sut.ProfileUpdated += received.Add;

        await Dispatch(sut, "/client/game/keepalive", Id(1).ToString());
        await Task.Delay(100);

        Assert.Empty(received);
    }

    [Fact]
    public async Task Ignores_raid_end_without_session_cookie()
    {
        var (sut, log, _) = Make();
        var received = new List<string>();
        sut.ProfileUpdated += received.Add;

        await Dispatch(sut, RaidEndNotifier.RaidEndUrl, null);
        await Task.Delay(100);

        Assert.Empty(received);
        // Error 부재만 보면 공허한 단언이다 — 이 클래스는 실패를 Warning 으로 남긴다.
        Assert.DoesNotContain(log.Entries, e => e.Level is "Warning" or "Error");
    }

    /// <summary>
    /// 발행은 CanHandle 이 아니라 응답 완료 시점이어야 한다 — 그 전에는 SPT 가 프로필을 아직 안 썼다.
    /// </summary>
    [Fact]
    public async Task Does_not_raise_before_response_completes()
    {
        var (sut, _, _) = Make();
        var received = new List<string>();
        sut.ProfileUpdated += received.Add;

        var (context, response) = Request(RaidEndNotifier.RaidEndUrl, Id(1).ToString());
        sut.CanHandle(context);
        await Task.Delay(100);
        Assert.Empty(received);

        var after = await Collect(sut, 1, () => response.CompleteAsync());
        Assert.Equal([Id(1).ToString()], after);
    }

    [Fact]
    public async Task Debounces_same_profile_within_one_second()
    {
        var (sut, _, time) = Make();
        var received = await Collect(sut, 2, async () =>
        {
            await Dispatch(sut, RaidEndNotifier.RaidEndUrl, Id(1).ToString());
            time.Advance(TimeSpan.FromMilliseconds(500));
            await Dispatch(sut, RaidEndNotifier.RaidEndUrl, Id(1).ToString());
            await Dispatch(sut, RaidEndNotifier.RaidEndUrl, Id(2).ToString());
        });

        Assert.Equal(2, received.Count);
        Assert.Contains(Id(1).ToString(), received);
        Assert.Contains(Id(2).ToString(), received);

        time.Advance(TimeSpan.FromSeconds(1.1));
        var again = await Collect(sut, 1, () => Dispatch(sut, RaidEndNotifier.RaidEndUrl, Id(1).ToString()));
        Assert.Contains(Id(1).ToString(), again);
    }

    [Fact]
    public async Task Subscriber_exception_is_logged_not_propagated()
    {
        var (sut, log, _) = Make();
        sut.ProfileUpdated += _ => throw new InvalidOperationException("subscriber boom");

        // Record.ExceptionAsync 를 쓰지 않는다: Raise 는 Task.Run 위에서 돌아 호출 스택 밖이므로
        // 동기적으로는 예외를 관측할 수 없고, Assert.Null(ex) 는 try/catch 를 지워도 통과하는 공허한
        // 단언이 된다. 실제 보호(구독자 예외를 삼켜 스레드풀로 새어나가지 않게 함)는 로그로 검증한다.
        await Dispatch(sut, RaidEndNotifier.RaidEndUrl, Id(1).ToString());
        await Task.Delay(100);

        Assert.Contains(log.Entries, e => e.Level == "Warning" && e.Message.Contains("subscriber boom"));
    }

    [Fact]
    public async Task Empty_url_is_ignored_without_error()
    {
        var (sut, log, _) = Make();
        var ex = await Record.ExceptionAsync(() => Dispatch(sut, "", Id(1).ToString()));
        Assert.Null(ex);
        Assert.DoesNotContain(log.Entries, e => e.Level is "Warning" or "Error");
    }

    /// <summary>
    /// I-2: 이 클래스가 발화하려면 DI 전제 3개가 모두 성립해야 하는데, 어느 하나가 깨져도 증상은
    /// "예외 없이 조용히 아무 일도 안 일어남"이다. 단위 테스트로 실발화는 못 잡아도 전제는 고정할 수 있다.
    /// </summary>
    [Fact]
    public void Is_registered_as_a_singleton_http_listener_ahead_of_spt()
    {
        var attr = typeof(RaidEndNotifier).GetCustomAttribute<Injectable>();

        Assert.NotNull(attr);
        Assert.Equal(InjectionType.Singleton, attr.InjectionType);
        Assert.True(attr.TypePriority < int.MaxValue,
            "TypePriority 가 SptHttpListener(int.MaxValue) 이상이면 우리가 뒤로 밀려 CanHandle 이 호출되지 않는다");
        Assert.True(typeof(IHttpListener).IsAssignableFrom(typeof(RaidEndNotifier)),
            "IHttpListener 를 구현해야 HttpServer 의 리스너 목록에 등록된다");
    }

    /// <summary>
    /// 쿠키 검증을 MongoId 형식까지 좁혔으므로, 실제 SPT 세션 id 가 그 관문을 통과하는지 고정한다.
    /// 값은 실서버 레이드 종료 시 PHPSESSID 로 실제 관측된 id 다 — 가드가 과하게 좁아지면 감지가
    /// 통째로 죽는데 그 증상이 "조용히 아무 일도 안 일어남"이라 알아채기 어렵다.
    /// </summary>
    [Fact]
    public async Task Accepts_a_real_observed_spt_session_id()
    {
        const string RealSessionId = "6a8328d2c1ae327dd00378cf";

        var (sut, _, _) = Make();
        var received = await Collect(sut, 1, () => Dispatch(sut, RaidEndNotifier.RaidEndUrl, RealSessionId));

        Assert.Equal([RealSessionId], received);
    }

    /// <summary>
    /// I-3: CanHandle 은 HttpServer 의 FirstOrDefault 술어다. 여기서 던지면 리스너 선택 자체가
    /// 실패해 서버의 모든 요청이 죽는다.
    /// </summary>
    [Fact]
    public void CanHandle_never_throws_even_on_a_broken_context()
    {
        var (sut, log, _) = Make();

        Assert.False(sut.CanHandle(null!));
        Assert.Contains(log.Entries, e => e.Level == "Warning" && e.Message.Contains("observe failed"));
    }
}
