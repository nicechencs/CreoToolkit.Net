using System.Diagnostics;

namespace CreoToolkit.Interop.Diagnostics;

/// <summary>注册默认 ActivityListener,让 ActivitySource.StartActivity 可返回 Activity。</summary>
public static class CreoTelemetryBootstrap
{
    private static int _registered;

    public static void RegisterDefault()
    {
        if (Interlocked.Exchange(ref _registered, 1) != 0)
            return;

        ActivitySource.AddActivityListener(new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("CreoToolkit.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = _ => { },
            ActivityStopped = _ => { },
        });
    }
}
