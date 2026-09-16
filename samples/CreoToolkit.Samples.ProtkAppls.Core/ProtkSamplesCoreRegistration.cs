namespace CreoToolkit.Samples.ProtkAppls.Core;

using CreoToolkit.App;
using CreoToolkit.Sdk;
using CreoToolkit.Samples.AgentDemo;

/// <summary>
/// PTC protk_appls 中 simple 示例集合的核心注册器（与 UI 解耦）。
/// 独立 Module Registration 按固定顺序加入 builder，命令数与顺序稳定。
/// </summary>
internal static class ProtkSamplesCoreRegistration
{
    internal static AgentDemoRuntime Register(CreoAppBuilder app, string modelName, CreoModelType modelType, string parameterName)
    {
        ThrowUtil.IfNull(app);
        var runtime = new AgentDemoRuntime();
        try
        {
            SampleModuleCatalog.RegisterAll(app, modelName, modelType, parameterName, runtime);
            return runtime;
        }
        catch
        {
            // Registration owns the runtime until the full catalog has completed.
            // Preserve the catalog failure even if defensive cleanup itself encounters a fault.
            try { runtime.Dispose(); }
            catch { /* AgentDemoRuntime cleanup is best-effort during registration rollback. */ }
            throw;
        }
    }

    /// <summary>全部模块注册完毕后，后附元数据。</summary>
    internal static void AttachAllMetadata(CreoAppBuilder app)
        => SampleCommandMetadata.AttachAll(app);

    // dialog Run 按钮必须走 host dispatch，使 message mirror / done echo / 失败码
    // 都归属真实 command。生产 UI 不得绕过 dispatcher 直接调用 handler。
    internal static void RunSampleByName(
        Func<string, int>? commandInvoker,
        string commandName)
    {
        ThrowUtil.IfNullOrWhiteSpace(commandName);

        if (commandInvoker is null)
            throw new InvalidOperationException(
                $"Host command invoker is unavailable; cannot dispatch '{commandName}' before application initialization.");

        var rc = commandInvoker(commandName);
        if (rc != 0)
            throw new InvalidOperationException($"Command dispatch failed: {commandName} rc={rc}");
    }

    // dialog Detail Panel 显 token 数值：取 builder 分配的 token（0-based 顺序号）。
    internal static int? ResolveCommandToken(CreoAppBuilder app, string commandName)
    {
        ThrowUtil.IfNull(app);
        return app.Build().FirstOrDefault(c => string.Equals(c.Name, commandName, StringComparison.Ordinal))?.Token;
    }
}
