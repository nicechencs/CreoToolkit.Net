namespace CreoToolkit.App;

/// <summary>
/// Creo 应用入口契约。
/// <para><b>Initialize</b>：声明命令/菜单（经 <see cref="CreoAppBuilder"/>）。在 host 的 user_initialize 阶段调用。</para>
/// <para><b>OnInitialize / OnTerminate</b>：可选生命周期钩子，映射 native <c>user_initialize</c>/<c>user_terminate</c>。
/// 默认空实现——业务侧仅在需要订阅 Creo 事件、启停后台资源、读写配置时 override。</para>
/// <para><b>配对契约</b>：框架仅在 <see cref="OnInitialize"/> <i>完整返回</i>（未抛）之后才会在卸载时调用 <see cref="OnTerminate"/>。
/// 若 OnInitialize 抛异常，框架不补调 OnTerminate——OnInitialize 内 throw 前产生的应用侧副作用（订阅、worker、连接）必须由 app 自己在 throw 之前回滚。
/// 该契约与 .NET 构造器异常语义一致，与 native user_initialize 返回非零时 Creo 不调 user_terminate 的行为对齐。</para>
/// <para><b>线程</b>：三个回调都在 Creo 主线程上同步执行，可安全调用 <c>ctx.Session.*</c> API。</para>
/// </summary>
public interface ICreoApplication
{
    /// <summary>声明命令/菜单。在 host 的 user_initialize 阶段调用。</summary>
    void Initialize(CreoAppBuilder app);

    /// <summary>
    /// 在 <c>CreoAppHost.Run()</c> 末尾（命令与菜单注册完成后）同步调用。默认空实现，按需 override。
    /// 抛异常将使 Run() 整体失败 → Bootstrap 返回非零 rc → Creo 不会回调 <c>user_terminate</c>。
    /// <para><b>※ 框架契约</b>：当 host 的 <c>_session</c> 为 null（<see cref="CreoAppHost.ForTest"/> 默认）时，
    /// 整段 lifecycle 钩子调用被跳过（视为测试便利）；生产路径 <see cref="CreoAppHost.ForHost"/> 工厂强制 session non-null。</para>
    /// </summary>
    /// <example>
    /// 推荐模板（预检 → 主体 → subs/worker try/catch；失败点回滚提示参见 ADR D30 与 <c>docs/creo-host-loading.md §9.1</c> 的 F1-F5 表）：
    /// <code>
    /// public void OnInitialize(CreoAppContext ctx)
    /// {
    ///     // 1. 预检：如需前置守卫（配置文件、命令行、依赖 session 状态）
    ///     if (string.IsNullOrEmpty(_configPath))
    ///         throw new InvalidOperationException("config path 未配置");
    ///
    ///     // 2. 主体：订阅 / 启动 worker；用 List 累计副作用便于回滚
    ///     var subs = new List&lt;IDisposable&gt;();
    ///     try
    ///     {
    ///         subs.Add(ctx.Session.Events.ModelSavePre.Subscribe(OnSave));
    ///         _worker = StartWorker();
    ///     }
    ///     catch
    ///     {
    ///         // 3. 失败回滚：throw 前必须自行释放已产生副作用 —— 框架不补调 OnTerminate（F5）
    ///         foreach (var s in subs) s.Dispose();
    ///         _worker?.Dispose();
    ///         throw;
    ///     }
    ///     _subs = subs;
    /// }
    /// </code>
    /// </example>
    void OnInitialize(CreoAppContext ctx);

    /// <summary>
    /// 在 <c>CreoAppHost.Dispose()</c> 首步（BridgeTerminate 之前）同步调用。默认空实现，按需 override。
    /// 仅当 <see cref="OnInitialize"/> 完整返回后才会调用；OnTerminate 内异常被框架 Safe 包装（记入 diagnostics 日志，不抛回 native）。
    /// <para><b>※ 框架契约</b>：当 host 的 <c>_session</c> 为 null（<see cref="CreoAppHost.ForTest"/> 默认）时，
    /// 整段 lifecycle 钩子调用被跳过（视为测试便利）；生产路径 <see cref="CreoAppHost.ForHost"/> 工厂强制 session non-null。</para>
    /// </summary>
    void OnTerminate(CreoAppContext ctx);
}
