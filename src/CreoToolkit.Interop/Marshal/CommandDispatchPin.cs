namespace CreoToolkit.Interop;

/// <summary>
/// 生命周期钉住：把单一命令 dispatch 委托经 <see cref="CallbackRegistry"/> 的
/// GCHandle 根钉住，暴露在 <see cref="Dispose"/> 前持续有效的 native 函数指针
/// （传给 <c>Ctk_CommandBridgeInitialize</c>）。<see cref="Dispose"/> 解钉，之后函数指针不得再用。
/// </summary>
public sealed class CommandDispatchPin : IDisposable
{
    private readonly CallbackRegistry _registry;
    private readonly CtkManagedCommandDispatch _dispatch;

    /// <summary>钉住后的稳定 native 函数指针。</summary>
    public nint FunctionPointer { get; }

    public CommandDispatchPin(CallbackRegistry registry, CtkManagedCommandDispatch dispatch)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
        FunctionPointer = registry.Register(dispatch);
    }

    /// <summary>解钉委托根；幂等（重复 Dispose 安全）。</summary>
    public void Dispose() => _registry.Unregister(_dispatch);
}
