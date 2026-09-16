using CreoToolkit.App;
using CreoToolkit.Sdk;

namespace CreoToolkit.Samples.HostProbe;

/// <summary>
/// 让 HostProbe 既可作为聚合 sample 的一个模块(经 <see cref="HostProbeRegistration.Register"/>),
/// 也可单独经 <c>CTK_APP_ASSEMBLY=CreoToolkit.Samples.HostProbe.dll</c> +
/// <c>CTK_APP_TYPE=CreoToolkit.Samples.HostProbe.HostProbeApp</c> 加载,
/// 用于最薄 host smoke(只装一条 <c>ctk.probe.smoke.model</c>)。
/// </summary>
public sealed class HostProbeApp : ICreoApplication
{
    private readonly string _smokeModelName;
    private readonly CreoModelType _smokeModelType;

    public HostProbeApp()
        : this(HostProbeRegistration.DefaultSmokeModelName, CreoModelType.Part)
    {
    }

    public HostProbeApp(string smokeModelName, CreoModelType smokeModelType)
    {
        ThrowUtil.IfNullOrWhiteSpace(smokeModelName);
        _smokeModelName = smokeModelName;
        _smokeModelType = smokeModelType;
    }

    public void Initialize(CreoAppBuilder app)
        => HostProbeRegistration.Register(app, _smokeModelName, _smokeModelType);

    public void OnInitialize(CreoAppContext ctx) { }

    public void OnTerminate(CreoAppContext ctx) { }
}
