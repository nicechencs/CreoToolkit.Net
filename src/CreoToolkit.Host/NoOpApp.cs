using CreoToolkit.App;

namespace CreoToolkit.Host;

/// <summary>
/// 兜底空应用:不注册任何命令。
/// <para>当 <c>CTK_APP_ASSEMBLY</c> + <c>CTK_APP_TYPE</c> 未配置时,<see cref="CreoApplicationLoader"/>
/// 返回本类型,以便 Host 启动闭环(attach session、注册命令桥、走 lifecycle)。
/// 客户要看真命令时设置 env 加载自己的 <see cref="ICreoApplication"/>;
/// 仓内 demo 走 samples/(DemoApp 已退役迁出 runtime)。</para>
/// </summary>
public sealed class NoOpApp : ICreoApplication
{
    public void Initialize(CreoAppBuilder app)
    {
        // 故意空实现。
    }

    public void OnInitialize(CreoAppContext ctx) { }

    public void OnTerminate(CreoAppContext ctx) { }
}
