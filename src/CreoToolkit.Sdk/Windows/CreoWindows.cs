using CreoToolkit.Sdk.Diagnostics;

namespace CreoToolkit.Sdk;

/// <summary>窗口查询门面。当前阶段只暴露 DATA 快照，不持有窗口资源。</summary>
public sealed class CreoWindows
{
    private readonly CreoSession _session;

    internal CreoWindows(CreoSession session) => _session = session;

    /// <summary>当前窗口 id；若当前上下文没有可用窗口 id，返回 null。</summary>
    public int? CurrentId() => _session.Run(n => n.WindowCurrentId());

    /// <summary>重绘指定窗口 id(须为有效非负 id;重绘当前窗口请用 <see cref="RepaintCurrent"/>)。
    /// 严格语义:底层重绘失败抛 <see cref="Errors.CreoException"/>。</summary>
    public void Repaint(int windowId) =>
        CreoSdkLog.Run(
            "window", "repaint",
            body: () =>
            {
                ThrowUtil.IfNegative(windowId);
                _session.Run(n => n.WindowRepaint(windowId));
            },
            failProps: () => new { type = "explicit", id = windowId });

    /// <summary>重绘当前窗口;无当前窗口、或当前上下文不可重绘(如启动期)时返回 false(best-effort,不抛)。
    /// 取 id 与重绘在单次 dispatch 内完成,避免两次调度间窗口上下文漂移(TOCTOU)。
    /// </summary>
    public bool RepaintCurrent()
    {
        int? windowId = null;

        return CreoSdkLog.Run(
            "window", "repaintcurrent",
            body: () => _session.Run(n =>
            {
                windowId = n.WindowCurrentId();
                return windowId is { } w && n.WindowTryRepaint(w);
            }),
            failProps: () => new { type = "current", id = windowId },
            okProps: repainted => new { type = "current", id = windowId, repainted });
    }
}
