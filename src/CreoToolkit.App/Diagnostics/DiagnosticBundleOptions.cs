namespace CreoToolkit.App.Diagnostics;

/// <summary>
/// 诊断包参数（record + 默认值，可调用方覆写）；
/// 调用方仅传 <see cref="OutputZipPath"/>，其余字段默认满足合规 (约定 > 抽象)。
/// </summary>
public sealed record DiagnosticBundleOptions(
    /// <summary>输出 zip 文件绝对路径(必填)。</summary>
    string OutputZipPath)
{
    /// <summary>默认开启路径脱敏(PII 合规) — 绝对路径替换为 <c>&lt;PATH-N&gt;</c>。</summary>
    public bool RedactPaths { get; init; } = true;

    /// <summary>单文件大小上限 (100 MB) — 超限尾截断保留最近日志。0 = 不限。</summary>
    public long MaxLogBytes { get; init; } = 100L * 1024 * 1024;

    /// <summary>客户/未来扩展口:额外采集的绝对文件路径(可选)。
    /// <para>⚠ 此参数是**任意文件读取入口**,API 默认信任调用方。
    /// 不可信场景(如远程触发 Collect):调用方应自行 allowlist 过滤。</para></summary>
    public IReadOnlyList<string>? AdditionalFiles { get; init; }
}
