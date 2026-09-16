namespace CreoToolkit.Sdk.Native;

internal enum NativeSubscriptionState
{
    Registered,
    Disposing,
    UnsetFailedRetained,
    Unregistered,
    SessionClosedRetained,
    NativeTerminated
}

/// <summary>回调资源实现此接口：所属 session 关闭、无法再经它重试 native 注销时被通知。</summary>
internal interface ISessionCloseAwareNativeResource
{
    void MarkSessionClosed();
}

/// <summary>
/// 可重试的长生命周期 native 回调订阅。注销失败时保持未释放状态，
/// 以保证 native 仍可能回调时托管 delegate root 继续存活。
/// </summary>
internal sealed class RetriableNativeSubscription : INativeResource, ISessionCloseAwareNativeResource
{
    // session 关闭时注销仍失败的订阅进程级隔离：即使 session/bridge 对象图已不可达，
    // delegate root 也必须存活——宁可有界泄漏，不可 use-after-free 回调。
    // 能证明 native 运行时已终止的宿主可调 ReleaseAfterNativeTermination 释放。
    private static readonly object QuarantineGate = new();
    private static readonly HashSet<RetriableNativeSubscription> Quarantine = new();
    private readonly Func<bool> _tryUnset;
    private readonly Action _releaseAfterNativeTermination;
    private readonly object _gate = new();
    private NativeSubscriptionState _state = NativeSubscriptionState.Registered;

    internal RetriableNativeSubscription(
        Func<bool> tryUnset,
        Action? releaseAfterNativeTermination = null)
    {
        _tryUnset = tryUnset ?? throw new ArgumentNullException(nameof(tryUnset));
        _releaseAfterNativeTermination = releaseAfterNativeTermination ?? (() => { });
    }

    public bool IsDisposed
    {
        get
        {
            lock (_gate)
                return _state is NativeSubscriptionState.Unregistered
                    or NativeSubscriptionState.NativeTerminated;
        }
    }

    internal NativeSubscriptionState State
    {
        get { lock (_gate) return _state; }
    }

    public void Dispose()
    {
        bool unregistered;
        lock (_gate)
        {
            if (_state is NativeSubscriptionState.Unregistered
                or NativeSubscriptionState.NativeTerminated
                or NativeSubscriptionState.Disposing)
                return;

            _state = NativeSubscriptionState.Disposing;
            try
            {
                _state = _tryUnset()
                    ? NativeSubscriptionState.Unregistered
                    : NativeSubscriptionState.UnsetFailedRetained;
            }
            catch
            {
                _state = NativeSubscriptionState.UnsetFailedRetained;
            }
            unregistered = _state == NativeSubscriptionState.Unregistered;
        }

        // 注销成功（含隔离期后重试成功）即出隔离集合，避免残留
        if (unregistered)
        {
            lock (QuarantineGate)
                Quarantine.Remove(this);
        }
    }

    public void MarkSessionClosed()
    {
        lock (_gate)
        {
            if (_state is NativeSubscriptionState.Unregistered or NativeSubscriptionState.NativeTerminated)
                return;
            _state = NativeSubscriptionState.SessionClosedRetained;
        }

        lock (QuarantineGate)
            Quarantine.Add(this);
    }

    internal void ReleaseAfterNativeTermination()
    {
        lock (_gate)
        {
            if (_state is NativeSubscriptionState.Unregistered or NativeSubscriptionState.NativeTerminated)
                return;

            _releaseAfterNativeTermination();
            _state = NativeSubscriptionState.NativeTerminated;
        }

        lock (QuarantineGate)
            Quarantine.Remove(this);
    }

    internal bool IsQuarantined
    {
        get { lock (QuarantineGate) return Quarantine.Contains(this); }
    }
}
