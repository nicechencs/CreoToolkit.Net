using CreoToolkit.Interop;
using CreoToolkit.Sdk.Diagnostics;
using CreoToolkit.Sdk.Errors;
using CreoToolkit.Sdk.Lifetime;
using CreoToolkit.Sdk.Native;
using CreoToolkit.Sdk.Session;

namespace CreoToolkit.Sdk;

/// <summary>
/// 已附着 Creo 会话的公共入口。所有 native 调用经 dispatcher 串行调度；
/// 会话关闭后绑定对象立即快速失败。
/// <para>
/// <b>线程合约</b>:<see cref="Dispose"/> 必须在 Creo 主线程调用(已 <see cref="Attach"/> 后,
/// 非主线程调用直接抛 <see cref="InvalidOperationException"/>)。原因:Dispose 经 <see cref="Dispatcher"/>
/// 释放所有 owned 句柄 + 排干 finalizer 队列,Pro/Toolkit 单线程铁律不绕过。
/// 业务侧 <c>using (session)</c> 必须在主线程作用域内,否则 dispose 失败、句柄滞留。
/// </para>
/// <para>
/// <b>finalizer leak 窗口</b>:Dispose **返回之后**才不可达的 owned 对象 finalizer 仍可能滞留
/// <c>FinalizerReleaseQueue</c>(GC 异步),session 已关→无人 pump。**finalizer 不是清理兜底,
/// `using` 才是**——业务代码必须用 <c>using</c> 或显式 <c>Dispose</c> 管 owned 资源(<c>CreoSelectionSet</c>/
/// <c>CreoElementTree</c> 等),不可依赖 GC 兜底。
/// </para>
/// </summary>
public sealed class CreoSession : IDisposable
{
    private readonly ICreoNative _native;
    private readonly CreoMainThreadReleaser? _releaser;
    private readonly object _resourceGate = new();
    private readonly List<SessionTrackedNativeResource> _trackedResources = new();
    private int _closed; // 0=open, 1=closed

    // 全局单调递增 epoch 计数 (long, process-local Interlocked atomic);
    // 每次 ctor 调用 bump 一次, 不同 Session 实例 / 不同 Attach 都拿到不同 epoch 值
    // (Dispose 不 bump, 由下一次 Attach 自然 bump)。
    // long 让序列回绕从 ~2.1B 提升到 ~9.2e18, 现实风险打到可忽略。
    // CreoDtlentity 携带其 epoch, CreoDtlentityGuard.SameSessionEpoch 校验跨 session 误传。
    private static long _epochCounter;

    public CreoModels Models { get; }
    public CreoSelections Selections { get; }
    public CreoFeatures Features { get; }
    public CreoWindows Windows { get; }
    public CreoToolkit.Sdk.Events.CreoEvents Events { get; }
    public CreoToolkit.Sdk.Events.CreoEventBus EventBus { get; }

    internal CreoSession(ICreoNative native, ICreoDispatcher dispatcher)
        : this(native, dispatcher, generated: null, releaser: null)
    {
    }

    /// <summary>测试入口:注入 fake <see cref="IGeneratedInvoker"/> 以保 L3 脱 Creo 单测。
    /// production 默认走 <see cref="GeneratedInvoker"/>(dispatcher + Check.Eval)。</summary>
    internal CreoSession(ICreoNative native, ICreoDispatcher dispatcher, IGeneratedInvoker generated)
        : this(native, dispatcher, generated, releaser: null)
    {
    }

    /// <summary>核心构造(internal)。production 经 <see cref="Attach"/> 注入主线程 releaser；
    /// 测试可直接注入 releaser 以验证 <see cref="Run{T}"/> 路径的 finalizer 队列节流排干。</summary>
    internal CreoSession(ICreoNative native, ICreoDispatcher dispatcher, IGeneratedInvoker? generated, CreoMainThreadReleaser? releaser)
    {
        _native = native ?? throw new ArgumentNullException(nameof(native));
        Dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        Generated = generated ?? new GeneratedInvoker(dispatcher);
        _releaser = releaser;
        CurrentEpoch = Interlocked.Increment(ref _epochCounter);
        CreoSdkLog.Trace("session", "epoch_assigned",
            new { epoch = CurrentEpoch, tid = Environment.CurrentManagedThreadId });
        Models = new CreoModels(this);
        Selections = new CreoSelections(this);
        Features = new CreoFeatures(this);
        Windows = new CreoWindows(this);
        Events = new CreoToolkit.Sdk.Events.CreoEvents(this);
        EventBus = new CreoToolkit.Sdk.Events.CreoEventBus(this);
    }

    /// <summary>当前 session 的 epoch(internal, long);
    /// 每次 ctor 调用经 <see cref="Interlocked.Increment(ref long)"/> 单调 bump,
    /// Dispose 后值废弃(下一次 Attach 自然 bump)。
    /// <para><see cref="CreoDtlentity"/> 等 lifecycle-bound DATA 携带此值,
    /// SDK 内 bridge / <see cref="CreoDtlentityGuard"/> 自取自校跨 session 误传。</para>
    /// <para><b>序列回绕</b>:long 计数到 <see cref="long.MaxValue"/> 后回绕到 <see cref="long.MinValue"/>,
    /// 现实场景不可达(每秒 100 万次 Attach 也需 ~29 万年回绕),撞同值视为不可能;
    /// 极端值 <c>!=</c> 比较仍正确,测试 fact <c>Epoch_Overflow_Comparison_Still_Correct</c> 验不变量。</para>
    /// <para>Host loads the SDK once in the default AppDomain; <c>_epochCounter</c>
    /// is a process-static field in that copy.</para></summary>
    internal long CurrentEpoch { get; }

    public static CreoSession Attach() =>
        CreoSdkLog.Run(
            "session", "attach",
            body: () =>
            {
                CreoThread.Capture();
                CtkAbi.Verify();

                var queue = new FinalizerReleaseQueue();
                var releaser = new CreoMainThreadReleaser(new DirectReleaser().Release, queue);
                CreoReleaseGate.SetReleaser(releaser);

                return new CreoSession(new CreoNativeBridge(), new SynchronousCreoDispatcher(), generated: null, releaser);
            },
            failProps: () => new { tid = Environment.CurrentManagedThreadId });

    internal ICreoDispatcher Dispatcher { get; }

    /// <summary>L3 调用生成的 <c>Pro*.g.cs</c> 绑定经此 invoker 走 dispatch+错误归一+日志。
    /// 与 <see cref="ICreoNative"/> 并存(手写 <c>Ctk_*</c> 走前者,生成绑定走此者);测试可经 3 参 ctor 注入 fake。</summary>
    internal IGeneratedInvoker Generated { get; }

    internal INativeResource TrackResource(INativeResource resource)
    {
        ThrowUtil.IfNull(resource);

        var tracked = new SessionTrackedNativeResource(this, resource);
        var disposeImmediately = false;

        lock (_resourceGate)
        {
            if (Volatile.Read(ref _closed) == 0)
                _trackedResources.Add(tracked);
            else
                disposeImmediately = true;
        }

        if (disposeImmediately)
            Dispatcher.Invoke(tracked.DisposeFromSession);

        return tracked;
    }

    private void UntrackResource(SessionTrackedNativeResource resource)
    {
        lock (_resourceGate)
        {
            _trackedResources.Remove(resource);
        }
    }

    private SessionTrackedNativeResource[] DrainTrackedResources()
    {
        lock (_resourceGate)
        {
            if (_trackedResources.Count == 0)
                return Array.Empty<SessionTrackedNativeResource>();

            var resources = _trackedResources.ToArray();
            Array.Reverse(resources);
            _trackedResources.Clear();
            return resources;
        }
    }

    internal void EnsureOpen()
    {
        if (Volatile.Read(ref _closed) != 0)
            throw new CreoSessionClosedException();
    }

    /// <summary>诊断用:当前 session 跟踪的 owned native 资源计数(BORROWED 副本集 / 元素树句柄等)。
    /// 公开以便 sample host probe 做资源平衡 smoke。</summary>
    public int TrackedResourceCountForDiagnostics
    {
        get
        {
            lock (_resourceGate)
                return _trackedResources.Count;
        }
    }

    /// <summary>诊断用:断言当前 session 无残留 owned native 资源,否则抛 <see cref="InvalidOperationException"/>。
    /// 典型用在 smoke 命令末尾验证 "owned 资源平衡"。</summary>
    public void AssertNoTrackedResourcesForDiagnostics()
    {
        var count = TrackedResourceCountForDiagnostics;
        if (count != 0)
            throw new InvalidOperationException($"CreoSession still tracks {count} owned native resource(s).");
    }

    // ---- 配置项 ----

    /// <summary>读取 Creo 配置项（单值）；选项不存在返 null。
    /// <para>多值项（如 <c>search_path</c>）仅返回首值，取全部值用 <see cref="ListConfigOptionValues"/>。</para></summary>
    public string? GetConfigOption(string option)
    {
        ThrowUtil.IfNullOrEmpty(option);
        return Run(n => n.ConfigOptionGet(option));
    }

    /// <summary>读取多值配置项的全部值（<c>ProConfigoptArrayGet</c>，如 <c>search_path</c>）；
    /// 选项未设置返回空列表。单值项返回单元素列表。</summary>
    public IReadOnlyList<string> ListConfigOptionValues(string option)
    {
        ThrowUtil.IfNullOrEmpty(option);
        return Run(n => n.ConfigOptionArrayGet(option));
    }

    /// <summary>设置 Creo 配置项。
    /// <para><b>多值语义</b>：对多值项（如 <c>search_path</c>），native <c>ProConfigoptSet</c>
    /// 为<b>追加</b>新条目而非覆盖既有值；单值项则替换。故重复调用多值项会累积，
    /// 全部值经 <see cref="ListConfigOptionValues"/> 读取。</para></summary>
    public void SetConfigOption(string option, string value)
    {
        ThrowUtil.IfNullOrEmpty(option);
        ThrowUtil.IfNullOrEmpty(value);
        Run(n => n.ConfigOptionSet(option, value));
    }

    internal T Run<T>(Func<ICreoNative, T> func)
    {
        EnsureOpen();
        return Dispatcher.Invoke(() =>
        {
            var result = func(_native);
            PumpFinalizerReleases();
            return result;
        });
    }

    internal void Run(Action<ICreoNative> action)
    {
        EnsureOpen();
        Dispatcher.Invoke(() =>
        {
            action(_native);
            PumpFinalizerReleases();
        });
    }

    // 每次业务 native 调用后, 在主线程顺带排干少量 finalizer 入队的释放,
    // 使未 Dispose 的 owned 句柄不在会话存续期无限滞留(`using` 仍是唯一兜底, 此处仅缓解漂移)。
    // 上限取小值: 单次 Run 只泵少量, 避免长会话内偶发释放尖刺造成命令延迟。
    private const int RunReleasePumpMaxItems = 16;
    private int _pumping; // 防重入: 万一 free 再触发 Run, 不得递归进入 pump

    private void PumpFinalizerReleases()
    {
        var releaser = _releaser;
        // 快路径: 测试注入路径无 releaser, 或队列空时零 Pump 开销
        if (releaser is null || releaser.Queue.Count == 0)
            return;
        // 挂点已在 Dispatcher 主线程内, 天然满足释放的主线程约束
        if (Interlocked.Exchange(ref _pumping, 1) != 0)
            return;
        try
        {
            releaser.Pump(RunReleasePumpMaxItems);
        }
        finally
        {
            Volatile.Write(ref _pumping, 0);
        }
    }

    /// <summary>
    /// 在 Creo 主线程串行执行 <paramref name="action"/>（命令 handler 经此满足单线程约束）。
    /// 会话已关闭则快速失败。这是 App 层 host 调度的唯一 public 接入点（薄封装内部串行调度）。
    /// </summary>
    public void RunOnMainThread(Action action)
    {
        ThrowUtil.IfNull(action);
        EnsureOpen();
        Dispatcher.Invoke(action);
    }

    public void Dispose()
    {
        // 前置线程校验：不改 _closed，失败后可在正确线程重试
        if (CreoThread.MainThreadId is int && !CreoThread.IsMainThread)
            throw new InvalidOperationException(
                "CreoSession.Dispose 必须在 Creo 主线程调用；检测到非主线程调用。");

        if (Interlocked.Exchange(ref _closed, 1) != 0)
            return;

        var resourceCount = 0;
        var failed = 0;

        CreoSdkLog.Run(
            "session", "dispose",
            body: () =>
            {
                var resources = DrainTrackedResources();
                resourceCount = resources.Length;

                List<Exception>? failures = null;

                Dispatcher.Invoke(() =>
                {
                    foreach (var resource in resources)
                    {
                        try
                        {
                            resource.DisposeFromSession();
                        }
                        catch (Exception ex)
                        {
                            (failures ??= new List<Exception>()).Add(ex);
                        }
                    }

                    if (_releaser is not null)
                    {
                        try
                        {
                            // pump 合约:Dispose 路径标准排干 finalizer 范式——
                            // GC.Collect + WaitForPendingFinalizers + GC.Collect → while-Pump,保证所有 owned 句柄
                            // (包括 Dispose 前刚不可达、finalizer 尚未运行的)都被排进队 + 排干。
                            // 注意:WaitForPendingFinalizers 是 stop-the-world,业务命令路径**绝不可用**;仅 Dispose
                            // 路径(用户已显式关会话)语义可接受。
                            // 漏洞承认:Dispose **返回之后**才不可达的对象 finalizer 仍可能滞留队中——是 leak 窗口,
                            // 文档化"finalizer 不是清理兜底,`using` 才是"。
                            GC.Collect();
                            GC.WaitForPendingFinalizers();
                            GC.Collect();
                            while (_releaser.Pump() > 0) { }
                        }
                        catch (Exception ex)
                        {
                            (failures ??= new List<Exception>()).Add(ex);
                        }
                    }
                });

                if (failures is not null)
                {
                    failed = failures.Count;
                    throw new AggregateException("One or more session resources failed to dispose.", failures);
                }
            },
            failProps: () => new { resources = resourceCount, failed },
            okProps: () => new { resources = resourceCount });
    }

    private sealed class SessionTrackedNativeResource : IWrappedNativeResource
    {
        private readonly CreoSession _session;
        private readonly INativeResource _inner;
        private int _disposed;

        public SessionTrackedNativeResource(CreoSession session, INativeResource inner)
        {
            _session = session;
            _inner = inner;
        }

        public bool IsDisposed => Volatile.Read(ref _disposed) == 2 || _inner.IsDisposed;

        public INativeResource Inner => _inner;

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
                return;

            try
            {
                _inner.Dispose();
                if (_inner.IsDisposed)
                {
                    _session.UntrackResource(this);
                    Volatile.Write(ref _disposed, 2);
                }
                else
                {
                    // native 注销失败但仍可能回调：保持 tracked/可重试，不能伪装成已释放。
                    Volatile.Write(ref _disposed, 0);
                }
            }
            catch
            {
                Volatile.Write(ref _disposed, 0);
                throw;
            }
        }

        public void DisposeFromSession()
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
                return;

            try
            {
                _inner.Dispose();
                if (_inner.IsDisposed)
                {
                    Volatile.Write(ref _disposed, 2);
                }
                else
                {
                    // The session is now closed, so a failed callback unregistration cannot be
                    // retried through this owner. Move callback resources to their process-lifetime
                    // safety quarantine; native may still call them until runtime termination.
                    if (_inner is ISessionCloseAwareNativeResource closeAware)
                        closeAware.MarkSessionClosed();
                    Volatile.Write(ref _disposed, 0);
                }
            }
            catch
            {
                Volatile.Write(ref _disposed, 0);
                throw;
            }
        }
    }
}
