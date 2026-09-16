using System.Collections.Concurrent;
using CreoToolkit.Agent.Commands;
using CreoToolkit.Sdk.Session;

namespace CreoToolkit.Agent.Transport;

/// <summary>主线程 marshal 执行器:后台线程排队命令,主线程 Pump 执行。
/// <para>取消语义(诚实分层):排队期被显式取消时主线程跳过并回 agent.canceled;
/// 一旦开始执行不可抢占;等待方超时由 transport 层统一回 agent.timeout。</para></summary>
public sealed class MainThreadAgentExecutor : IDisposable
{
    private readonly CreoCommandRouter _router;
    private readonly ConcurrentQueue<WorkItem> _queue = new();
    private readonly AutoResetEvent _workAvailable = new(false);
    private volatile bool _disposed;
    private volatile bool _pumpActive;

    public MainThreadAgentExecutor(CreoCommandRouter router)
    {
        _router = router ?? throw new ArgumentNullException(nameof(router));
    }

    /// <summary>泵循环是否活跃(RunPumpLoop 运行中)。无泵时 transport 应拒收业务命令而非静默超时。</summary>
    public bool IsPumpActive => _pumpActive;

    /// <summary>等待句柄:有新工作入队时信号通知。</summary>
    internal WaitHandle WorkAvailable => _workAvailable;

    /// <summary>从后台线程排队命令,返回 Task 等待主线程执行结果。</summary>
    internal Task<AgentResult> EnqueueAsync(AgentCommand command, CancellationToken ct)
    {
        if (_disposed)
        {
            return Task.FromResult(AgentResult.Fail(
                command.CorrelationId, "agent.shutdown", "执行器已关闭"));
        }

        var tcs = new TaskCompletionSource<AgentResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var item = new WorkItem(command, tcs, ct);

        // 取消回调:排队期被显式取消时直接完成 TCS;registration 在 TCS 完成后回收
        if (ct.CanBeCanceled)
        {
            var registration = ct.Register(static state =>
            {
                var wi = (WorkItem)state!;
                wi.Completion.TrySetResult(AgentResult.Fail(
                    wi.Command.CorrelationId, "agent.canceled", "排队期间被取消"));
            }, item, useSynchronizationContext: false);

            tcs.Task.ContinueWith(
                static (_, state) => ((CancellationTokenRegistration)state!).Dispose(),
                registration,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        _queue.Enqueue(item);
        _workAvailable.Set();

        return tcs.Task;
    }

    /// <summary>
    /// Processes at most <paramref name="maxItems"/> queued commands on the main thread.
    /// <paramref name="stopping"/> is observed before every new command, never during a
    /// running router/native Pro* call. This keeps an explicit pump window bounded even if
    /// producers continuously enqueue work.
    /// </summary>
    public int Pump() => Pump(int.MaxValue);

    public int Pump(int maxItems, CancellationToken stopping = default)
    {
        if (maxItems <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxItems));
        if (CreoThread.MainThreadId is int && !CreoThread.IsMainThread)
            throw new InvalidOperationException(
                "MainThreadAgentExecutor.Pump 必须在 Creo 主线程调用;检测到非主线程调用。");

        var processed = 0;
        var dequeued = 0;
        while (dequeued < maxItems && !stopping.IsCancellationRequested && _queue.TryDequeue(out var item))
        {
            // The batch limit applies to every dequeued item, including already-canceled work.
            // Otherwise a continuous canceled producer could still starve the stop check.
            dequeued++;
            // 排队期已被取消:跳过
            if (item.CancellationToken.IsCancellationRequested)
            {
                item.Completion.TrySetResult(AgentResult.Fail(
                    item.Command.CorrelationId, "agent.canceled", "排队期间被取消"));
                continue;
            }

            try
            {
                var result = _router.Route(item.Command);
                item.Completion.TrySetResult(result);
            }
            catch (OperationCanceledException)
            {
                item.Completion.TrySetResult(AgentResult.Fail(
                    item.Command.CorrelationId, "agent.canceled", "执行被取消"));
            }
            catch (Exception ex)
            {
                item.Completion.TrySetResult(AgentResult.Fail(
                    item.Command.CorrelationId, "agent.unexpected",
                    $"{ex.GetType().Name}: {ex.Message}"));
            }
            processed++;
        }
        return processed;
    }

    /// <summary>
    /// Blocking explicit pump loop. It waits for work only until <paramref name="stopping"/>
    /// fires and uses a bounded batch each iteration, so stop is observed between commands.
    /// A running native call is deliberately not preempted.
    /// </summary>
    public void RunPumpLoop(CancellationToken stopping) => RunPumpLoop(stopping, 16);

    public void RunPumpLoop(CancellationToken stopping, int maxItemsPerIteration)
    {
        if (maxItemsPerIteration <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxItemsPerIteration));
        if (_disposed)
            throw new ObjectDisposedException(nameof(MainThreadAgentExecutor));
        if (CreoThread.MainThreadId is int && !CreoThread.IsMainThread)
            throw new InvalidOperationException("MainThreadAgentExecutor.RunPumpLoop must run on the Creo main thread.");

        _pumpActive = true;
        try
        {
            while (!stopping.IsCancellationRequested)
            {
                // 等待新工作、停止信号或定时唤醒。
                WaitHandle.WaitAny(new[] { stopping.WaitHandle, _workAvailable }, 50);
                if (!stopping.IsCancellationRequested)
                    Pump(maxItemsPerIteration, stopping);
            }
        }
        finally
        {
            _pumpActive = false;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        // 排干队列中残留项
        while (_queue.TryDequeue(out var item))
        {
            item.Completion.TrySetResult(AgentResult.Fail(
                item.Command.CorrelationId, "agent.shutdown", "执行器已关闭"));
        }
        _workAvailable.Dispose();
    }

    private sealed class WorkItem
    {
        public AgentCommand Command { get; }
        public TaskCompletionSource<AgentResult> Completion { get; }
        public CancellationToken CancellationToken { get; }

        public WorkItem(AgentCommand command, TaskCompletionSource<AgentResult> completion, CancellationToken ct)
        {
            Command = command;
            Completion = completion;
            CancellationToken = ct;
        }
    }
}
