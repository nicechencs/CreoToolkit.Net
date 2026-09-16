namespace CreoToolkit.Sdk.Native;

/// <summary>
/// <c>ProCableLocationsOnSegEndGet</c> ProArray 所有权未决期间的诊断节奏策略。
/// 本策略刻意不裁决所有权本身。
/// </summary>
internal static class CablingBoundaryOwnershipPolicy
{
    internal const string ExperimentalDiagnosticId = "CTKEXP001";
    internal const int WarningInterval = 100;

    /// <summary>首次暴露必警告，高频使用期间按间隔周期性警告。</summary>
    internal static bool ShouldWarn(int nonEmptyReadCount)
        => nonEmptyReadCount == 1
           || (nonEmptyReadCount > 0 && nonEmptyReadCount % WarningInterval == 0);
}
