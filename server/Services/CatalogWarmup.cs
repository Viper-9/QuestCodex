using System.Diagnostics;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;

namespace QuestCodex.Services;

/// <summary>미리 만들기가 쓰는 카탈로그 쪽 기능. <see cref="CatalogService"/> 가 구현하고, 테스트에서 가짜로 바꾼다.</summary>
public interface ICatalogWarmSource
{
    /// <summary>지난 세션까지 요청받은 언어(<c>cache/warm-langs.json</c>). 비어 있으면 미리 만들지 않는다.</summary>
    IReadOnlyList<string> RecentLangs { get; }

    IReadOnlySet<string> SupportedLangs { get; }

    /// <summary>카탈로그를 만들어 캐시에 넣는다. 요청과 달리 언어 기록은 건드리지 않는다.</summary>
    void Prebuild(string lang);
}

/// <summary>
/// 서버 시작 직후 백그라운드에서 카탈로그를 미리 만든다(16 catalog-warmup 스펙 §3). 첫 Briefing 탭이 빌드를 기다리지 않게.
/// IOnLoad 가 아니라 IOnUpdate 를 쓴다: SPTStartupHostedService 는 모든 IOnLoad 를 하나씩 기다린 뒤에야 업데이트 루프를
/// 시작하므로, 첫 업데이트는 다른 모드의 IOnLoad(퀘스트·아이템 추가, 이미지 라우트 등록)가 모두 끝난 뒤가 보장된다.
/// OnLoad 순서는 다른 모드가 더 큰 우선순위를 쓰면 뒤집힌다.
/// </summary>
[Injectable(InjectionType.Singleton)]
public class CatalogWarmup(ICatalogWarmSource catalogs, ISptLogger<CatalogWarmup> logger) : IOnUpdate
{
    private int _started;

    /// <summary>진행 중이거나 끝난 미리 만들기. 테스트가 기다리는 용도.</summary>
    internal Task? Warming { get; private set; }

    public Task<bool> OnUpdateAsync(long secondsSinceLastRun, CancellationToken cancellationToken)
    {
        // 업데이트 루프는 모든 IOnUpdate 를 차례로 기다리므로, 여기서 빌드를 기다리면 다른 모드의 업데이트가 멈춘다.
        if (Interlocked.Exchange(ref _started, 1) == 0)
        {
            Warming = StartLowPriority(() => Warm(cancellationToken));
        }

        return Task.FromResult(true);
    }

    /// <summary>
    /// 스레드 풀 대신 BelowNormal 전용 스레드 — 서버 시작 직후는 게임 클라이언트가 로딩하는 때라 CPU 가 바쁘면 게임이 먼저 쓰게.
    /// 백그라운드 스레드라 서버 종료를 막지 않는다. 전용 스레드에서 처리되지 않은 예외는 서버 프로세스를 죽이므로
    /// Warm 이 이미 다 잡지만 여기서도 한 번 더 막는다.
    /// </summary>
    private static Task StartLowPriority(Action work)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                work();
            }
            catch
            {
                // Warm 이 로그를 남긴다. 여기까지 온 예외는 서버를 지키는 것만 한다.
            }
            finally
            {
                done.SetResult();
            }
        })
        {
            Name = "QuestCodex catalog warm-up",
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal,
        };
        thread.Start();
        return done.Task;
    }

    private void Warm(CancellationToken cancellationToken)
    {
        try
        {
            var langs = catalogs.RecentLangs.Where(catalogs.SupportedLangs.Contains).ToList();
            if (langs.Count == 0) return;

            logger.Info($"[QuestCodex] catalog warm-up started ({string.Join(", ", langs)})");
            var started = Stopwatch.GetTimestamp();
            // 하나씩 차례로 — 동시에 만들면 최대 메모리만 늘어난다.
            foreach (var lang in langs)
            {
                if (cancellationToken.IsCancellationRequested) return;
                try
                {
                    catalogs.Prebuild(lang);
                }
                catch (Exception ex)
                {
                    // 실패한 언어는 캐시에 남지 않으므로 다음 요청이 다시 만든다.
                    logger.Warning($"[QuestCodex] catalog warm-up for '{lang}' failed, it will be built on the first request: {ex.Message}");
                }
            }

            logger.Info($"[QuestCodex] catalog warm-up finished in {Stopwatch.GetElapsedTime(started).TotalMilliseconds:F0} ms");
        }
        catch (Exception ex)
        {
            logger.Warning($"[QuestCodex] catalog warm-up skipped: {ex.Message}");
        }
    }
}
