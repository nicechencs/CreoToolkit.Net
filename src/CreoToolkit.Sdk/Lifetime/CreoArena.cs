namespace CreoToolkit.Sdk.Lifetime;

/// <summary>
/// 一组 disposable 的作用域所有者，按 LIFO 顺序（后注册先释放）在 <see cref="Dispose"/> 时统一释放，
/// 与 native 嵌套分配的生命周期镜像对称。
/// 单个项释放失败不阻断其余项，所有失败汇总为 <see cref="AggregateException"/> 抛出。
/// </summary>
public sealed class CreoArena : IDisposable
{
    private readonly Stack<IDisposable> _items = new();
    private bool _disposed;

    /// <summary>
    /// 将 <paramref name="item"/> 注册到 arena 并原样返回，支持内联写法：
    /// <c>var h = arena.Track(Acquire(...));</c>
    /// </summary>
    /// <returns>传入的同一实例。</returns>
    public T Track<T>(T item) where T : IDisposable
    {
        ThrowUtil.IfDisposed(_disposed, this);

        if (item is not null)
            _items.Push(item);

        return item!;
    }

    /// <summary>
    /// 按 LIFO 顺序释放所有注册项。单项失败不阻断其余项，
    /// 全部尝试后将收集的异常汇总为 <see cref="AggregateException"/> 抛出。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        List<Exception>? failures = null;

        while (_items.Count > 0)
        {
            var item = _items.Pop(); // LIFO：与注册顺序相反
            try
            {
                item.Dispose();
            }
            catch (Exception ex)
            {
                (failures ??= new List<Exception>()).Add(ex);
            }
        }

        if (failures is not null)
            throw new AggregateException("One or more tracked resources failed to dispose.", failures);
    }
}
