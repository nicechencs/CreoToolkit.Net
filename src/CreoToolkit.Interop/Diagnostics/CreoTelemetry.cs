using System.Diagnostics;

namespace CreoToolkit.Interop.Diagnostics;

/// <summary>
/// OpenTelemetry ActivitySource 门面(BCL 内置,无 OpenTelemetry 依赖)。
/// </summary>
public static class CreoTelemetry
{
    /// <summary>SDK 域 ActivitySource。</summary>
    public static readonly ActivitySource SdkSource = new("CreoToolkit.Sdk", "1.0.0");

    /// <summary>Host 域 ActivitySource。</summary>
    public static readonly ActivitySource HostSource = new("CreoToolkit.Host", "1.0.0");

    /// <summary>Native 桥域 ActivitySource。</summary>
    public static readonly ActivitySource NativeSource = new("CreoToolkit.Native", "1.0.0");
}
