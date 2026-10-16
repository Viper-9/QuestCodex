namespace QuestCodex.Services;

/// <summary>
/// 성공한 값만 기억하는 Lazy. 동시 호출은 진행 중인 한 번을 기다린다(<c>ExecutionAndPublication</c> 과 같음).
/// 표준 Lazy 는 예외도 기억해서, 한 번 실패하면 세대를 바꿀 때까지 모든 요청이 실패했다(16 catalog-warmup 스펙 §5).
/// </summary>
public sealed class RetryingLazy<T>(Func<T> factory)
{
    private readonly Lock _lock = new();
    private bool _created;
    private T? _value;

    public T Value
    {
        get
        {
            if (Volatile.Read(ref _created)) return _value!;
            lock (_lock)
            {
                if (!_created)
                {
                    _value = factory();
                    Volatile.Write(ref _created, true);
                }

                return _value!;
            }
        }
    }
}
