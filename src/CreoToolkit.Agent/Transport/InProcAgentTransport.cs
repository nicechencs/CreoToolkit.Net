using System.Threading;
using CreoToolkit.Agent.Commands;

namespace CreoToolkit.Agent.Transport;

/// <summary>In-proc transport:入口检查取消,同步路由,用 Task.FromResult 保持异步形状。
/// 线程契约:同步直算无 marshal,须在 Creo 主线程调用——从后台线程 await 会被
/// dispatcher 主线程断言 fail-fast 拦下,不会静默错线程执行 native 调用。
/// 实现 IDisposable,Dispose 链式释放 router(兜底 HandleRegistry)。</summary>
public sealed class InProcAgentTransport : IAgentTransport, IDisposable
{
    private readonly CreoCommandRouter _router;
    private int _disposed;

    public InProcAgentTransport(CreoCommandRouter router)
        => _router = router ?? throw new ArgumentNullException(nameof(router));

    public Task<AgentResult> ExecuteAsync(AgentCommand command, CancellationToken ct = default)
    {
        ThrowUtil.IfNull(command);
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_router.Route(command));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _router.Dispose();
    }
}
