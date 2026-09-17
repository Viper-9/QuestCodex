using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Time.Testing;
using QuestCodex.Progress.Models;
using QuestCodex.Services;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Servers.Http;
using static QuestCodex.Tests.Fixtures;

namespace QuestCodex.Tests.Services;

public class ProfileChangeNotifierTests
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

    /// <summary>메모리 안의 PmcData 를 그대로 돌려주는 프로필 소스. 테스트가 그 객체를 직접 변형해 "SPT 가 처리했다"를 흉내낸다.</summary>
    private sealed class FakeProfiles : IProfileSource
    {
        public Dictionary<string, PmcData> Pmcs { get; } = new(StringComparer.Ordinal);
        /// <summary>이 횟수째 Find 부터 던진다. 응답 완료 후 스냅샷(두 번째 호출)이 실패하는 상황을 흉내낸다.</summary>
        public int ThrowFromCall { get; set; } = int.MaxValue;
        private int _calls;
        public IReadOnlyList<ProfileSummary> List() => [];
        public ProfileLookup Find(string id)
            => ++_calls >= ThrowFromCall
                ? throw new InvalidOperationException("profile store boom")
                : Pmcs.TryGetValue(id, out var pmc)
                ? new ProfileLookup(ProfileLookupState.Ready, pmc, true)
                : new ProfileLookup(ProfileLookupState.NotFound, null, false);
    }

    private static (ProfileChangeNotifier Sut, RecordingLogger<ProfileChangeNotifier> Log, FakeTimeProvider Time, FakeProfiles Profiles) Make()
    {
        var log = new RecordingLogger<ProfileChangeNotifier>();
        var time = new FakeTimeProvider();
        var profiles = new FakeProfiles();
        return (new ProfileChangeNotifier(profiles, log, time), log, time, profiles);
    }

    /// <summary>요청 한 건을 리스너에 통과시키고 응답 완료까지 흉내낸다. 반환값은 CanHandle 의 결과.</summary>
    private static async Task<bool> Dispatch(ProfileChangeNotifier sut, string url, string? sessionId)
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
    private static async Task<List<ProfileChange>> Collect(ProfileChangeNotifier sut, int expected, Func<Task> act)
    {
        var received = new List<ProfileChange>();
        using var arrived = new CountdownEvent(expected);

        void OnUpdated(ProfileChange change)
        {
            lock (received) received.Add(change);
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
        var (sut, _, _, _) = Make();
        var received = await Collect(sut, 1, () => Dispatch(sut, ProfileChangeNotifier.RaidEndUrl, Id(1).ToString()));
        Assert.Equal([Id(1).ToString()], received.Select(c => c.ProfileId));
    }

    /// <summary>
    /// 이 리스너는 관찰만 한다. true 를 반환하면 HttpServer 가 SptHttpListener 대신 우리에게 요청을
    /// 넘겨버려 레이드 종료 처리 자체가 사라진다.
    /// </summary>
    [Fact]
    public async Task Never_claims_the_request()
    {
        var (sut, _, _, _) = Make();

        Assert.False(await Dispatch(sut, ProfileChangeNotifier.RaidEndUrl, Id(1).ToString()));
        Assert.False(await Dispatch(sut, "/client/game/keepalive", Id(1).ToString()));
        Assert.False(await Dispatch(sut, "/questcodex/api/catalog", null));
    }

    [Fact]
    public async Task Ignores_other_urls()
    {
        var (sut, _, _, _) = Make();
        var received = new List<ProfileChange>();
        sut.ProfileUpdated += received.Add;

        await Dispatch(sut, "/client/game/keepalive", Id(1).ToString());
        await Task.Delay(100);

        Assert.Empty(received);
    }

    [Fact]
    public async Task Ignores_raid_end_without_session_cookie()
    {
        var (sut, log, _, _) = Make();
        var received = new List<ProfileChange>();
        sut.ProfileUpdated += received.Add;

        await Dispatch(sut, ProfileChangeNotifier.RaidEndUrl, null);
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
        var (sut, _, _, _) = Make();
        var received = new List<ProfileChange>();
        sut.ProfileUpdated += received.Add;

        var (context, response) = Request(ProfileChangeNotifier.RaidEndUrl, Id(1).ToString());
        sut.CanHandle(context);
        await Task.Delay(100);
        Assert.Empty(received);

        var after = await Collect(sut, 1, () => response.CompleteAsync());
        Assert.Equal([Id(1).ToString()], after.Select(c => c.ProfileId));
    }

    [Fact]
    public async Task Debounces_same_profile_within_one_second()
    {
        var (sut, _, time, _) = Make();
        var received = await Collect(sut, 2, async () =>
        {
            await Dispatch(sut, ProfileChangeNotifier.RaidEndUrl, Id(1).ToString());
            time.Advance(TimeSpan.FromMilliseconds(500));
            await Dispatch(sut, ProfileChangeNotifier.RaidEndUrl, Id(1).ToString());
            await Dispatch(sut, ProfileChangeNotifier.RaidEndUrl, Id(2).ToString());
        });

        Assert.Equal(2, received.Count);
        Assert.Contains(Id(1).ToString(), received.Select(c => c.ProfileId));
        Assert.Contains(Id(2).ToString(), received.Select(c => c.ProfileId));

        time.Advance(TimeSpan.FromSeconds(1.1));
        var again = await Collect(sut, 1, () => Dispatch(sut, ProfileChangeNotifier.RaidEndUrl, Id(1).ToString()));
        Assert.Contains(Id(1).ToString(), again.Select(c => c.ProfileId));
    }

    [Fact]
    public async Task Subscriber_exception_is_logged_not_propagated()
    {
        var (sut, log, _, _) = Make();
        sut.ProfileUpdated += _ => throw new InvalidOperationException("subscriber boom");

        // Record.ExceptionAsync 를 쓰지 않는다: Raise 는 Task.Run 위에서 돌아 호출 스택 밖이므로
        // 동기적으로는 예외를 관측할 수 없고, Assert.Null(ex) 는 try/catch 를 지워도 통과하는 공허한
        // 단언이 된다. 실제 보호(구독자 예외를 삼켜 스레드풀로 새어나가지 않게 함)는 로그로 검증한다.
        await Dispatch(sut, ProfileChangeNotifier.RaidEndUrl, Id(1).ToString());
        await Task.Delay(100);

        Assert.Contains(log.Entries, e => e.Level == "Warning" && e.Message.Contains("subscriber boom"));
    }

    [Fact]
    public async Task Empty_url_is_ignored_without_error()
    {
        var (sut, log, _, _) = Make();
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
        var attr = typeof(ProfileChangeNotifier).GetCustomAttribute<Injectable>();

        Assert.NotNull(attr);
        Assert.Equal(InjectionType.Singleton, attr.InjectionType);
        Assert.True(attr.TypePriority < int.MaxValue,
            "TypePriority 가 SptHttpListener(int.MaxValue) 이상이면 우리가 뒤로 밀려 CanHandle 이 호출되지 않는다");
        Assert.True(typeof(IHttpListener).IsAssignableFrom(typeof(ProfileChangeNotifier)),
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

        var (sut, _, _, _) = Make();
        var received = await Collect(sut, 1, () => Dispatch(sut, ProfileChangeNotifier.RaidEndUrl, RealSessionId));

        Assert.Equal([RealSessionId], received.Select(c => c.ProfileId));
    }

    /// <summary>
    /// I-3: CanHandle 은 HttpServer 의 FirstOrDefault 술어다. 여기서 던지면 리스너 선택 자체가
    /// 실패해 서버의 모든 요청이 죽는다.
    /// </summary>
    [Fact]
    public void CanHandle_never_throws_even_on_a_broken_context()
    {
        var (sut, log, _, _) = Make();

        Assert.False(sut.CanHandle(null!));
        Assert.Contains(log.Entries, e => e.Level == "Warning" && e.Message.Contains("observe failed"));
    }

    // ----- 메뉴 조작 감지: items/moving 앞뒤 퀘스트 상태 diff (스펙 §4.4) -----

    /// <summary>
    /// 퀘스트 완료는 전용 URL 이 없고 items/moving 본문에 실려 온다. 본문을 읽지 않고, 요청 전후의
    /// 퀘스트 상태를 비교해 바뀐 퀘스트 id 를 실어 발행해야 한다.
    /// </summary>
    [Fact]
    public async Task Raises_changed_quest_ids_when_items_moving_changes_a_quest_status()
    {
        var (sut, _, _, profiles) = Make();
        var pmc = Pmc();
        pmc.Quests!.Add(ProfileQuest(Id(1), QuestStatusEnum.Started));
        pmc.Quests.Add(ProfileQuest(Id(2), QuestStatusEnum.Started));
        profiles.Pmcs[Id(9).ToString()] = pmc;

        var (context, response) = Request(ProfileChangeNotifier.ItemMovingUrl, Id(9).ToString());
        var received = await Collect(sut, 1, async () =>
        {
            sut.CanHandle(context);
            pmc.Quests[0].Status = QuestStatusEnum.Success;   // SPT 가 QuestComplete 를 처리했다
            await response.CompleteAsync();
        });

        var change = Assert.Single(received);
        Assert.Equal(Id(9).ToString(), change.ProfileId);
        Assert.Equal([Id(1).ToString()], change.ChangedQuestIds);
    }

    /// <summary>본문을 파싱하지 않아도 되는 이유: 인벤 정리처럼 퀘스트가 안 바뀐 요청은 diff 가 비어 발행되지 않는다.</summary>
    [Fact]
    public async Task Does_not_raise_when_items_moving_changes_no_quest()
    {
        var (sut, log, _, profiles) = Make();
        var pmc = Pmc();
        pmc.Quests!.Add(ProfileQuest(Id(1), QuestStatusEnum.Started));
        profiles.Pmcs[Id(9).ToString()] = pmc;
        var received = new List<ProfileChange>();
        sut.ProfileUpdated += received.Add;

        await Dispatch(sut, ProfileChangeNotifier.ItemMovingUrl, Id(9).ToString());   // 아이템만 옮김
        await Task.Delay(100);

        Assert.Empty(received);
        Assert.DoesNotContain(log.Entries, e => e.Level is "Warning" or "Error");
    }

    /// <summary>
    /// CompletedConditions 가 바뀌면 Status 가 그대로여도 잡는다. 처음엔 이걸 "핸드오버 감지"로 알았는데
    /// 실서버에서 핸드오버는 여기를 안 건드렸다 — SPT 는 TaskConditionCounters 를 올린다 (아래 테스트).
    /// </summary>
    [Fact]
    public async Task Detects_completed_conditions_change()
    {
        var (sut, _, _, profiles) = Make();
        var pmc = Pmc();
        var quest = ProfileQuest(Id(1), QuestStatusEnum.Started);
        quest.CompletedConditions = [];
        pmc.Quests!.Add(quest);
        profiles.Pmcs[Id(9).ToString()] = pmc;

        var (context, response) = Request(ProfileChangeNotifier.ItemMovingUrl, Id(9).ToString());
        var received = await Collect(sut, 1, async () =>
        {
            sut.CanHandle(context);
            quest.CompletedConditions!.Add(Id(7).ToString());   // 조건 하나 충족
            await response.CompleteAsync();
        });

        Assert.Equal([Id(1).ToString()], Assert.Single(received).ChangedQuestIds);
    }

    /// <summary>
    /// 핸드오버의 실제 동작 (ilspycmd QuestController.HandoverQuest): Status·CompletedConditions 는 그대로이고
    /// TaskConditionCounters[conditionId].Value 만 올라간다. 카운터의 SourceId 가 퀘스트 id 다.
    /// 실서버에서 물 1개·3개를 제출해도 알림이 안 온 원인.
    /// </summary>
    [Fact]
    public async Task Detects_handover_through_task_condition_counter()
    {
        var (sut, _, _, profiles) = Make();
        var pmc = Pmc();
        pmc.Quests!.Add(ProfileQuest(Id(1), QuestStatusEnum.Started));
        pmc.TaskConditionCounters![Id(700)] = new TaskConditionCounter { Id = Id(700), SourceId = Id(1), Value = 1 };
        profiles.Pmcs[Id(9).ToString()] = pmc;

        var (context, response) = Request(ProfileChangeNotifier.ItemMovingUrl, Id(9).ToString());
        var received = await Collect(sut, 1, async () =>
        {
            sut.CanHandle(context);
            pmc.TaskConditionCounters[Id(700)].Value = 2;   // 물 하나 더 제출
            await response.CompleteAsync();
        });

        Assert.Equal([Id(1).ToString()], Assert.Single(received).ChangedQuestIds);
    }

    /// <summary>첫 핸드오버는 카운터가 없어서 새로 생성된다 (UpdateProfileTaskConditionCounterValue 의 Add 분기).</summary>
    [Fact]
    public async Task Detects_first_handover_that_creates_the_counter()
    {
        var (sut, _, _, profiles) = Make();
        var pmc = Pmc();
        pmc.Quests!.Add(ProfileQuest(Id(1), QuestStatusEnum.Started));
        profiles.Pmcs[Id(9).ToString()] = pmc;

        var (context, response) = Request(ProfileChangeNotifier.ItemMovingUrl, Id(9).ToString());
        var received = await Collect(sut, 1, async () =>
        {
            sut.CanHandle(context);
            pmc.TaskConditionCounters![Id(700)] = new TaskConditionCounter { Id = Id(700), SourceId = Id(1), Value = 1 };
            await response.CompleteAsync();
        });

        Assert.Equal([Id(1).ToString()], Assert.Single(received).ChangedQuestIds);
    }

    /// <summary>수락은 프로필 Quests 에 항목이 새로 생기는 경우다 — after 에만 있는 키도 변경으로 본다.</summary>
    [Fact]
    public async Task Detects_quest_accept_as_a_new_quest_entry()
    {
        var (sut, _, _, profiles) = Make();
        var pmc = Pmc();
        profiles.Pmcs[Id(9).ToString()] = pmc;

        var (context, response) = Request(ProfileChangeNotifier.ItemMovingUrl, Id(9).ToString());
        var received = await Collect(sut, 1, async () =>
        {
            sut.CanHandle(context);
            pmc.Quests!.Add(ProfileQuest(Id(3), QuestStatusEnum.Started));   // QuestAccept
            await response.CompleteAsync();
        });

        Assert.Equal([Id(3).ToString()], Assert.Single(received).ChangedQuestIds);
    }

    /// <summary>
    /// diff 알림은 디바운스하지 않는다. 실제로 바뀐 경우에만 발화하니 중복이 구조적으로 불가능하고,
    /// 디바운스를 걸면 1초 안의 두 번째 변경이 가진 changedQuestIds 가 유실된다.
    /// </summary>
    [Fact]
    public async Task Diff_raises_are_not_debounced()
    {
        var (sut, _, _, profiles) = Make();
        var pmc = Pmc();
        pmc.Quests!.Add(ProfileQuest(Id(1), QuestStatusEnum.Started));
        pmc.Quests.Add(ProfileQuest(Id(2), QuestStatusEnum.Started));
        profiles.Pmcs[Id(9).ToString()] = pmc;

        var received = await Collect(sut, 2, async () =>
        {
            var (c1, r1) = Request(ProfileChangeNotifier.ItemMovingUrl, Id(9).ToString());
            sut.CanHandle(c1);
            pmc.Quests[0].Status = QuestStatusEnum.Success;
            await r1.CompleteAsync();

            var (c2, r2) = Request(ProfileChangeNotifier.ItemMovingUrl, Id(9).ToString());   // 같은 초 안에 두 번째
            sut.CanHandle(c2);
            pmc.Quests[1].Status = QuestStatusEnum.Success;
            await r2.CompleteAsync();
        });

        Assert.Equal(2, received.Count);
        Assert.Contains(received, c => c.ChangedQuestIds!.SequenceEqual([Id(1).ToString()]));
        Assert.Contains(received, c => c.ChangedQuestIds!.SequenceEqual([Id(2).ToString()]));
    }

    /// <summary>레이드 종료는 "무엇이 바뀌었는지 모름" — changedQuestIds 가 null 이어야 브라우저가 전체 재요청한다.</summary>
    [Fact]
    public async Task Raid_end_carries_no_changed_quest_ids()
    {
        var (sut, _, _, _) = Make();
        var received = await Collect(sut, 1, () => Dispatch(sut, ProfileChangeNotifier.RaidEndUrl, Id(1).ToString()));
        Assert.Null(Assert.Single(received).ChangedQuestIds);
    }

    [Fact]
    public async Task Items_moving_is_never_claimed_and_tolerates_unknown_profile()
    {
        var (sut, log, _, _) = Make();   // 프로필 소스가 비어 있음
        var received = new List<ProfileChange>();
        sut.ProfileUpdated += received.Add;

        Assert.False(await Dispatch(sut, ProfileChangeNotifier.ItemMovingUrl, Id(9).ToString()));
        await Task.Delay(100);

        Assert.Empty(received);
        Assert.DoesNotContain(log.Entries, e => e.Level is "Warning" or "Error");
    }

    /// <summary>
    /// 응답 완료 후의 스냅샷·diff 도 격리돼야 한다 (스펙 §4.4 "스냅샷·diff 에서 나는 예외도 전부 삼킨다").
    /// 여기서 새면 Kestrel 이 잡아 SPT 로그에 Error 로 무제한 찍히고, CanHandle 의 1회 억제를 우회한다.
    /// </summary>
    [Fact]
    public async Task Diff_failure_after_response_is_swallowed_and_logged()
    {
        var (sut, log, _, profiles) = Make();
        profiles.Pmcs[Id(9).ToString()] = Pmc();
        profiles.ThrowFromCall = 2;   // before 스냅샷은 성공, after 스냅샷에서 던짐
        var received = new List<ProfileChange>();
        sut.ProfileUpdated += received.Add;

        var (context, response) = Request(ProfileChangeNotifier.ItemMovingUrl, Id(9).ToString());
        sut.CanHandle(context);
        var ex = await Record.ExceptionAsync(response.CompleteAsync);
        await Task.Delay(100);

        Assert.Null(ex);
        Assert.Empty(received);
        Assert.Contains(log.Entries, e => e.Level == "Warning" && e.Message.Contains("profile store boom"));
    }

    /// <summary>SPT 는 Quests 에서 항목을 제거하기도 한다(데일리 만료 등). before 에만 있는 키도 변경이다.</summary>
    [Fact]
    public async Task Detects_quest_removed_from_profile()
    {
        var (sut, _, _, profiles) = Make();
        var pmc = Pmc();
        pmc.Quests!.Add(ProfileQuest(Id(1), QuestStatusEnum.Started));
        pmc.Quests.Add(ProfileQuest(Id(2), QuestStatusEnum.Started));
        profiles.Pmcs[Id(9).ToString()] = pmc;

        var (context, response) = Request(ProfileChangeNotifier.ItemMovingUrl, Id(9).ToString());
        var received = await Collect(sut, 1, async () =>
        {
            sut.CanHandle(context);
            pmc.Quests.RemoveAt(1);
            await response.CompleteAsync();
        });

        Assert.Equal([Id(2).ToString()], Assert.Single(received).ChangedQuestIds);
    }

    /// <summary>QuestComplete 는 다른 퀘스트를 Fail 로 바꿀 수 있다. 여러 개가 바뀌면 전부, 정렬된 순서로 실린다.</summary>
    [Fact]
    public async Task Reports_all_changed_quests_in_ordinal_order()
    {
        var (sut, _, _, profiles) = Make();
        var pmc = Pmc();
        pmc.Quests!.Add(ProfileQuest(Id(3), QuestStatusEnum.Started));
        pmc.Quests.Add(ProfileQuest(Id(1), QuestStatusEnum.Started));
        pmc.Quests.Add(ProfileQuest(Id(2), QuestStatusEnum.Started));
        profiles.Pmcs[Id(9).ToString()] = pmc;

        var (context, response) = Request(ProfileChangeNotifier.ItemMovingUrl, Id(9).ToString());
        var received = await Collect(sut, 1, async () =>
        {
            sut.CanHandle(context);
            pmc.Quests[0].Status = QuestStatusEnum.Success;   // Id(3)
            pmc.Quests[2].Status = QuestStatusEnum.Fail;      // Id(2)
            await response.CompleteAsync();
        });

        Assert.Equal([Id(2).ToString(), Id(3).ToString()], Assert.Single(received).ChangedQuestIds);
    }
}
