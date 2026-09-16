using System.Diagnostics;
using CreoToolkit.Interop.Diagnostics;
using CreoToolkit.Sdk.Native;

namespace CreoToolkit.Sdk.Events;

/// <summary>多 handler 事件总线(session-bound)。在 Creo 单回调/type 限制之上复用:
/// 一个 native callback 分发给 N 个 managed handler。与 <see cref="CreoEvents"/> 互斥——同 event type 二选一。
/// <para>PoC:当前仅暴露 3 个事件(DirectoryChanged / ModelSavePre / GroupUngroupPre),
/// 与 <see cref="CreoEvents"/> 覆盖范围相同;更多事件扩面按 roadmap deferred,
/// 等真实插件需求触发。此类非完整生产 API。</para>
/// <para>命名约定:<c>On*</c> 前缀(区别于 <see cref="CreoEvents.SubscribeDirectoryChanged"/> 等 <c>Subscribe*</c> 单 handler API)
/// 表示"注册一个参与者到多 handler 总线"语义;同一事件可注册 N 个 handler,每个 Dispose 仅移除自身。</para>
/// </summary>
/// <remarks>
/// <para><b>Lifecycle 契约</b>(详见 ADR D32 · CreoEventBus MVP 边界):
/// 首个 handler 懒订阅 native + 经 <c>TrackResource</c> 挂入 arena;
/// 最后一个 handler Dispose 时释放 native slot,该事件类型的总线通道回归空闲;
/// 释放后 direct <c>session.Events.Subscribe*</c> 可对同一事件类型重新订阅(互斥语义只在总线占用期间生效)。</para>
/// <para><b>Intercepting handler 异常政策</b>:PRE 拦截型事件(<c>GroupUngroupPre</c>)handler 抛异常时,
/// wrapper 特例返 <c>NO_ERROR</c>(默认 Allow)+ warn 日志 <c>notify.handler.exception</c>——对齐 bridge 通用政策,
/// 防止 handler 异常被 Creo 解读为拦截、误阻用户正常操作。非拦截型(<c>DirectoryChanged</c>/<c>ModelSavePre</c>)handler 异常吞入日志、不影响后续 handler。</para>
/// </remarks>
public sealed class CreoEventBus
{
    private readonly CreoSession _session;
    private readonly object _gate = new();

    private List<Action<string>>? _dirChangedHandlers;
    private IDisposable? _dirChangedNativeSub;

    private List<Action<string>>? _savePreHandlers;
    private IDisposable? _savePreNativeSub;

    private List<Func<CreoFeatureGroupContext, UngroupVote>>? _ungroupHandlers;
    private IDisposable? _ungroupNativeSub;

    internal CreoEventBus(CreoSession session) => _session = session;

    /// <summary>订阅工作目录变更(PRO_DIRECTORY_CHANGE_POST)。支持多 handler;Dispose 移除该 handler。</summary>
    public IDisposable OnDirectoryChanged(Action<string> handler)
    {
        ThrowUtil.IfNull(handler);
        lock (_gate)
        {
            if (_dirChangedHandlers == null)
            {
                _dirChangedHandlers = new();
                var resource = _session.Run(n => n.NotifySubscribeDirectoryChanged(DispatchDirectoryChanged));
                _dirChangedNativeSub = _session.TrackResource(resource);
            }
            _dirChangedHandlers.Add(handler);
        }
        return new HandlerToken(() => RemoveDirectoryChangedHandler(handler));
    }

    /// <summary>订阅模型保存前事件(PRO_MODEL_SAVE_PRE)。支持多 handler;Dispose 移除该 handler。</summary>
    public IDisposable OnModelSavePre(Action<string> handler)
    {
        ThrowUtil.IfNull(handler);
        lock (_gate)
        {
            if (_savePreHandlers == null)
            {
                _savePreHandlers = new();
                var resource = _session.Run(n => n.NotifySubscribeModelSavePre(DispatchModelSavePre));
                _savePreNativeSub = _session.TrackResource(resource);
            }
            _savePreHandlers.Add(handler);
        }
        return new HandlerToken(() => RemoveModelSavePreHandler(handler));
    }

    /// <summary>订阅 ungroup 拦截事件(PRO_GROUP_UNGROUP_PRE)。支持多 handler;投票聚合:任一 Block → Block。</summary>
    public IDisposable OnGroupUngroupPre(Func<CreoFeatureGroupContext, UngroupVote> handler)
    {
        ThrowUtil.IfNull(handler);
        lock (_gate)
        {
            if (_ungroupHandlers == null)
            {
                _ungroupHandlers = new();
                var resource = _session.Run(n => n.NotifySubscribeGroupUngroupPre(DispatchGroupUngroupPre));
                _ungroupNativeSub = _session.TrackResource(resource);
            }
            _ungroupHandlers.Add(handler);
        }
        return new HandlerToken(() => RemoveGroupUngroupPreHandler(handler));
    }

    private void DispatchDirectoryChanged(string path)
    {
        Action<string>[] snapshot;
        lock (_gate) { snapshot = _dirChangedHandlers?.ToArray() ?? Array.Empty<Action<string>>(); }
        foreach (var h in snapshot)
        {
            try { h(path); }
            catch (Exception ex) { LogHandlerException("DirectoryChanged", ex); }
        }
    }

    private void DispatchModelSavePre(string path)
    {
        Action<string>[] snapshot;
        lock (_gate) { snapshot = _savePreHandlers?.ToArray() ?? Array.Empty<Action<string>>(); }
        foreach (var h in snapshot)
        {
            try { h(path); }
            catch (Exception ex) { LogHandlerException("ModelSavePre", ex); }
        }
    }

    private UngroupVote DispatchGroupUngroupPre(CreoFeatureGroupContext ctx)
    {
        Func<CreoFeatureGroupContext, UngroupVote>[] snapshot;
        lock (_gate)
        {
            snapshot = _ungroupHandlers?.ToArray()
                       ?? Array.Empty<Func<CreoFeatureGroupContext, UngroupVote>>();
        }
        var result = UngroupVote.Allow;
        foreach (var h in snapshot)
        {
            try
            {
                if (h(ctx) == UngroupVote.Block)
                    result = UngroupVote.Block;
            }
            catch (Exception ex) { LogHandlerException("GroupUngroupPre", ex); }
        }
        return result;
    }

    private static void LogHandlerException(string eventType, Exception ex)
    {
        using var act = CreoTelemetry.SdkSource.StartActivity("eventbus.handler.exception");
        if (act == null) return;
        act.SetTag("ctk.domain", "eventbus");
        act.SetTag("ctk.action", "handler.exception");
        act.SetTag("ctk.type", eventType);
        act.SetTag("exception.type", ex.GetType().FullName);
        act.SetTag("exception.message", ex.Message);
        act.SetStatus(ActivityStatusCode.Error, ex.Message);
    }

    private void RemoveDirectoryChangedHandler(Action<string> handler)
    {
        RemoveHandler(ref _dirChangedHandlers, handler, ref _dirChangedNativeSub);
    }

    private void RemoveModelSavePreHandler(Action<string> handler)
    {
        RemoveHandler(ref _savePreHandlers, handler, ref _savePreNativeSub);
    }

    private void RemoveGroupUngroupPreHandler(Func<CreoFeatureGroupContext, UngroupVote> handler)
    {
        RemoveHandler(ref _ungroupHandlers, handler, ref _ungroupNativeSub);
    }

    private void RemoveHandler<T>(ref List<T>? list, T handler, ref IDisposable? nativeSubscription) where T : Delegate
    {
        IDisposable? toDispose = null;
        lock (_gate)
        {
            if (list is null)
                return;

            list.Remove(handler);
            if (list.Count != 0)
                return;

            list = null;
            toDispose = nativeSubscription;
            nativeSubscription = null;
        }

        toDispose?.Dispose();
    }

    private sealed class HandlerToken : IDisposable
    {
        private Action? _onDispose;

        internal HandlerToken(Action onDispose) => _onDispose = onDispose;

        public void Dispose()
        {
            Interlocked.Exchange(ref _onDispose, null)?.Invoke();
        }
    }
}
