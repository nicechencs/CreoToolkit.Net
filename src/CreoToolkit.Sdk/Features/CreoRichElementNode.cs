using CreoToolkit.Sdk.Native;

namespace CreoToolkit.Sdk;

/// <summary>元素值类型鉴别(对齐 Pro/Toolkit pro_value_data_type 子集)。</summary>
public enum CreoElementValueKind
{
    None = 0,
    Integer = 1,
    Double = 2,
    String = 5,
    Reference = 6,
    Transform = 7,
    Boolean = 8,
}

/// <summary>
/// 带类型化值的元素树节点(纯 DATA,释放后仍可持有)。
/// ValueKind 鉴别哪个值字段有效; Reference/Transform 仅标记种类,不读具体值。
/// </summary>
public readonly record struct CreoRichElementNode(
    int ElementId,
    int Level,
    CreoElementValueKind ValueKind,
    int IntValue,
    double DoubleValue,
    string? StringValue,
    bool BoolValue);

/// <summary>
/// owned LIVE 元素树(深度模式,带类型化元素值)。与 <see cref="CreoElementTree"/> 同生命周期约定:
/// Dispose 经 dispatcher 释放 owned 句柄; Walk 在 Dispose 后抛 <see cref="ObjectDisposedException"/>。
/// </summary>
public sealed class CreoRichElementTree : IDisposable
{
    private readonly CreoSession _session;
    private readonly INativeResource _tree;
    private readonly IReadOnlyList<CreoRichElementNode> _nodes;
    private int _disposed;

    internal CreoRichElementTree(CreoSession session, ElemtreeExtractDeepResult result)
    {
        _session = session;
        _tree = result.Tree;
        _nodes = result.Nodes;
    }

    /// <summary>只读遍历元素树(返回抽取时的带值节点快照)。</summary>
    public IEnumerable<CreoRichElementNode> Walk()
    {
        ThrowUtil.IfDisposed(Volatile.Read(ref _disposed) != 0, this);
        _session.EnsureOpen();
        return _nodes;
    }

    /// <summary>释放 owned LIVE 树句柄(幂等; 经 dispatcher 在主线程)。</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _session.Dispatcher.Invoke(() => _tree.Dispose());
    }
}
