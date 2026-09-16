using CreoToolkit.Sdk.Native;

namespace CreoToolkit.Sdk.Events;

/// <summary>
/// 事件订阅门面(session-bound)。包 <see cref="ICreoNative"/> 的 NotifySubscribe* 系列, 把
/// owned 订阅句柄经 <see cref="CreoSession.TrackResource"/> 纳入 session 生命周期 — Session.Dispose
/// 自动释放所有未 Dispose 的订阅, 防止 callback 在 session 关闭后被 Creo 触发到野指针。
///
/// 当前暴露 3 个事件:
/// <see cref="SubscribeDirectoryChanged"/> / <see cref="SubscribeModelSavePre"/> /
/// <see cref="SubscribeGroupUngroupPre"/>。需要同一事件多个 handler 时使用
/// <see cref="CreoEventBus"/>；两者共享同一组 native notification 能力。
/// </summary>
public sealed class CreoEvents
{
    private readonly CreoSession _session;

    internal CreoEvents(CreoSession session) => _session = session;

    /// <summary>订阅工作目录变更事件 (PRO_DIRECTORY_CHANGE_POST): Creo 切换 working dir 后回调,
    /// 入参 = 新 path。返回 <see cref="IDisposable"/>: Dispose → ProNotificationUnset；仅在 native
    /// 确认注销成功或订阅已不存在后释放 GC root。注销失败时保留 root，并允许再次 Dispose 重试。
    /// 同 type 仅允许一个订阅 (C 覆盖语义在 SDK 加守卫,重订阅抛 InvalidOperationException)。
    /// callback 在 Creo 主线程执行, handler 内部抛异常会被 SEH 隔离吞掉 (不传 native)。</summary>
    public IDisposable SubscribeDirectoryChanged(Action<string> handler)
    {
        ThrowUtil.IfNull(handler);
        var resource = _session.Run(n => n.NotifySubscribeDirectoryChanged(handler));
        return _session.TrackResource(resource);
    }

    /// <summary>订阅模型保存前事件 (PRO_MODEL_SAVE_PRE): Creo 保存 model 前回调, 入参 = 待保存模型的
    /// 完整路径(由 Creo 提供的 wstring)。该事件取代 deprecated PRO_MDL_SAVE_PRE(Creo 3.0 后停止触发)。
    /// 返回 <see cref="IDisposable"/>: Dispose → ProNotificationUnset；注销失败时保留 GC root，
    /// 避免 Creo 持有悬空 callback，并允许再次 Dispose 重试。
    /// 如需 CreoModel 对象,handler 内可经 path 解析名 + 调 <c>session.Models.Retrieve</c>。</summary>
    public IDisposable SubscribeModelSavePre(Action<string> handler)
    {
        ThrowUtil.IfNull(handler);
        var resource = _session.Run(n => n.NotifySubscribeModelSavePre(handler));
        return _session.TrackResource(resource);
    }

    /// <summary>订阅 ungroup 拦截事件 (PRO_GROUP_UNGROUP_PRE = 77):
    /// Creo UI 触发 ungroup 前回调,handler 返 <see cref="UngroupVote.Allow"/> 放行 /
    /// <see cref="UngroupVote.Block"/> 拦截。入参 <see cref="CreoFeatureGroupContext"/> 是 DATA
    /// 快照(不持 native handle)。同 type 仅允许一个订阅;重订阅抛 InvalidOperationException。
    /// callback 在 Creo 主线程执行;handler 抛异常默认 Allow + warn 日志(避免误拦阻断 UI)。</summary>
    public IDisposable SubscribeGroupUngroupPre(Func<CreoFeatureGroupContext, UngroupVote> handler)
    {
        ThrowUtil.IfNull(handler);
        var resource = _session.Run(n => n.NotifySubscribeGroupUngroupPre(handler));
        return _session.TrackResource(resource);
    }
}
