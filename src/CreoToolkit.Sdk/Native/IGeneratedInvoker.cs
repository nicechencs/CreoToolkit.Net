using CreoToolkit.Sdk.Errors;
using CreoToolkit.Sdk.Session;
using GenProErrors = CreoToolkit.Interop.Generated.ProErrors;

namespace CreoToolkit.Sdk.Native;

/// <summary>
/// L3 调用**生成的 P/Invoke 绑定**(`Pro*.g.cs`)的统一通道。
/// 解决两道关键问题:
///   1. **fake 接缝保住**:`static extern` 不能注入,但 invoker 可——production 走 static extern,
///      测试注入 in-memory fake。同 <see cref="ICreoNative"/> 一样保 L3 脱 Creo 单测能力。
///   2. **统一约束**:所有生成绑定调用经此 invoker → dispatch(主线程)+ Check.Eval(错误归一)+ 日志。
/// 手写 `Ctk_*` 仍走 <see cref="ICreoNative"/>。
/// </summary>
internal interface IGeneratedInvoker
{
    /// <summary>调用生成绑定的纯动作(无返回值业务结果)。
    /// <paramref name="api"/> 是 native 函数名(用于 Check.Eval 错误归一与日志);
    /// <paramref name="call"/> 由调用方提供,真实 production 实现内部直接调 `NativeMethods.ProXxx(...)` 并返回其 <see cref="GenProErrors"/>。</summary>
    void Invoke(string api, Func<GenProErrors> call);

    /// <summary>调用生成绑定并取一个出参(传统 Pro/Toolkit 风格:返回 ProError,出参经 ref/out 形态)。
    /// 把"取出参"的过程封进 <paramref name="call"/>,invoker 只保 dispatch+错误归一,不感知具体出参类型。</summary>
    T Invoke<T>(string api, GeneratedInvokeFunc<T> call);
}

/// <summary>生成绑定调用的委托形态:调用方负责把"调 NativeMethods + 取出参"塞进委托,
/// 委托执行后返回 <see cref="GenProErrors"/>(错误码)与一个出参值。</summary>
internal delegate GenProErrors GeneratedInvokeFunc<T>(out T result);

/// <summary>
/// production 实现:经 <see cref="ICreoDispatcher"/> 在 Creo 主线程串行执行 + <see cref="Check.Eval"/> 错误归一。
/// </summary>
internal sealed class GeneratedInvoker : IGeneratedInvoker
{
    private readonly ICreoDispatcher _dispatcher;

    internal GeneratedInvoker(ICreoDispatcher dispatcher)
        => _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

    public void Invoke(string api, Func<GenProErrors> call)
    {
        ThrowUtil.IfNullOrEmpty(api);
        ThrowUtil.IfNull(call);
        _dispatcher.Invoke(() =>
        {
            var e = call();
            Check.Eval(api, e);
        });
    }

    public T Invoke<T>(string api, GeneratedInvokeFunc<T> call)
    {
        ThrowUtil.IfNullOrEmpty(api);
        ThrowUtil.IfNull(call);
        return _dispatcher.Invoke(() =>
        {
            var e = call(out var result);
            Check.Eval(api, e);
            return result;
        });
    }
}
