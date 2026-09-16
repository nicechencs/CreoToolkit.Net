using GenProErrors = CreoToolkit.Interop.Generated.ProErrors;

namespace CreoToolkit.Sdk.Errors;

/// <summary>
/// 所有 native 调用返回值的统一入口。<see cref="Eval"/> 经 <see cref="ProErrorPolicy"/>
/// 按 (api, code) 分类：真实错误抛 <see cref="CreoException"/>，
/// 其余（成功、条件查询、交互中止）以 <see cref="CreoResult"/> 返回供调用方分支。
/// </summary>
public static class Check
{
    /// <summary>
    /// 对 <paramref name="api"/> 返回的状态码 <paramref name="e"/> 分类。
    /// 结果为 <see cref="CreoOutcome.Error"/> 时抛 <see cref="CreoException"/>；
    /// 否则返回携带原始码的 <see cref="CreoResult"/>。
    /// </summary>
    /// <param name="api">产生 <paramref name="e"/> 的 Creo TOOLKIT 函数名。</param>
    /// <param name="e">native 返回的原始状态码。</param>
    /// <param name="nativeMessage">native 侧附加的错误详情，无则为 null。</param>
    /// <exception cref="CreoException">分类为错误时抛出。</exception>
    public static CreoResult Eval(string api, ProError e, string? nativeMessage = null)
    {
        var outcome = ProErrorPolicy.Classify(api, e);
        if (outcome == CreoOutcome.Error)
            throw new CreoException(api, e, nativeMessage);

        return new CreoResult(e, outcome);
    }

    /// <summary>
    /// 桥接:生成绑定(<c>Pro*.g.cs</c>)吐 <see cref="GenProErrors"/> 全量枚举,通过此重载归一到
    /// SDK 的 <see cref="ProError"/> 子集 + <see cref="ProErrorPolicy"/> 错误模型。数值完全镜像
    /// (PRO_TK_NO_ERROR=0/PRO_TK_GENERAL_ERROR=-1/...),直接 (int) 强转值兼容。
    /// </summary>
    public static CreoResult Eval(string api, GenProErrors e, string? nativeMessage = null)
        => Eval(api, (ProError)(int)e, nativeMessage);
}
