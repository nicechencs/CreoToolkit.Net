using CreoToolkit.Sdk;

namespace CreoToolkit.App;

/// <summary>
/// Lifecycle 钩子上下文。<see cref="ICreoApplication.OnInitialize"/> 与
/// <see cref="ICreoApplication.OnTerminate"/> 收到的 <see cref="Session"/> / <see cref="Messages"/>
/// 都保证 non-null（host 在 EnsureSession 成功后才构造）。
/// 与 <see cref="CreoCommandContext"/> 区分：命令侧 Session 可空（脱 Creo 路由测试不构造 session）；
/// lifecycle 侧 Session 必非空（若 Attach 失败，Bootstrap 已在更上层 catch 并不会调到这里）。
/// </summary>
public sealed class CreoAppContext
{
    public CreoSession Session { get; }
    public CreoMessages Messages { get; }
    public string MsgFile { get; }

    // 宿主私有引用:仅供 InvokeCommand 窄面转发,不整包暴露——
    // Run() 无幂等守卫(重入=命令重复注册)、Dispose() 在 OnInitialize 内被调会跳过
    // OnTerminate 配对,都不该被 ICreoApplication 实现触达。
    private readonly CreoAppHost _host;

    internal CreoAppContext(CreoSession session, CreoMessages messages, string msgFile, CreoAppHost host)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
        Messages = messages ?? throw new ArgumentNullException(nameof(messages));
        MsgFile = msgFile ?? throw new ArgumentNullException(nameof(msgFile));
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    /// <summary>按名派发已注册命令(与菜单点击/diagnostic 同一 dispatch 路径)。
    /// 返回 rc:0=成功 / 1=handler 抛异常 / 2=命令未注册。
    /// <para>线程约束:必须在 Creo 主线程调用;非主线程调用时 dispatcher 主线程门抛出,
    /// dispatch 异常边界兜住后返回 rc=1。</para>
    /// <para>用途:samples/agent 侧在 OnInitialize 内取本方法为派发委托
    /// (如注入 Agent ICommandInvoker adapter),实现 post-init 按名触发命令。</para></summary>
    public int InvokeCommand(string commandId) => _host.InvokeCommand(commandId);
}
