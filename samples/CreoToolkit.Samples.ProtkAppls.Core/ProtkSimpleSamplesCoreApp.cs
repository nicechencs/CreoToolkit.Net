namespace CreoToolkit.Samples.ProtkAppls.Core;

using CreoToolkit.App;
using CreoToolkit.Sdk;
using CreoToolkit.Samples.AgentDemo;

/// <summary>
/// ProtkAppls 核心宿主（无 WinForms/WPF dialog shell）：注册全部 Module 命令。
/// Module 自身声明的原生 Creo 菜单（例如 ExtCreoMenu）仍会注册；7 个主题 dialog launcher
/// 及其 CreoToolkit 顶级菜单由 WinForms 版本追加。本类型适合 nogui / 集成测试场景。
/// </summary>
public sealed class ProtkSimpleSamplesCoreApp : ICreoApplication
{
    private readonly string _modelName;
    private readonly CreoModelType _modelType;
    private readonly string _parameterName;
    private AgentDemoRuntime? _agentRuntime;

    public ProtkSimpleSamplesCoreApp()
        : this(SampleTargetModelOptions.FromEnvironment(), "CTK_SAMPLE_PARAM")
    {
    }

    private ProtkSimpleSamplesCoreApp(SampleTargetModelOptions target, string parameterName)
        : this(target.Name, target.Type, parameterName)
    {
    }

    public ProtkSimpleSamplesCoreApp(string modelName, CreoModelType modelType, string parameterName)
    {
        ThrowUtil.IfNullOrWhiteSpace(modelName);
        ThrowUtil.IfNullOrWhiteSpace(parameterName);
        _modelName = modelName;
        _modelType = modelType;
        _parameterName = parameterName;
    }

    public void Initialize(CreoAppBuilder app)
    {
        ThrowUtil.IfNull(app);
        _agentRuntime = ProtkSamplesCoreRegistration.Register(app, _modelName, _modelType, _parameterName);
    }

    public void OnInitialize(CreoAppContext ctx)
    {
        ThrowUtil.IfNull(ctx);
        try
        {
            (_agentRuntime ?? throw new InvalidOperationException("Agent runtime was not created during registration."))
                .Initialize(ctx);
        }
        catch
        {
            // Host does not call OnTerminate after a failed OnInitialize.
            DisposeAgentRuntime();
            throw;
        }
    }

    public void OnTerminate(CreoAppContext ctx)
    {
        DisposeAgentRuntime();
    }

    private void DisposeAgentRuntime()
    {
        var runtime = _agentRuntime;
        _agentRuntime = null;
        if (runtime is null) return;
        try { runtime.Dispose(); }
        catch (Exception ex)
        {
            try
            {
                CreoAppLog.Warn("释放 Agent runtime 失败", @event: "app.cleanup-failed", module: "samples.core",
                    props: new { error = ex.Message });
            }
            catch { /* diagnostics cannot block lifecycle rollback */ }
        }
    }
}
