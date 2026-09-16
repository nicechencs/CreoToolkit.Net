using CreoToolkit.Sdk.Native;

namespace CreoToolkit.Sdk;

/// <summary>drawing 明细项基类(draft entity / detail note / drawing table)。
/// 封装 session 绑定 + 构造期 epoch 快照;
/// 与 <see cref="CreoDtlentity"/> 裸 token 不同,本族是 session-bound 领域对象。</summary>
public abstract class CreoDetailItem : CreoModelItem
{
    /// <summary>构造期捕获的 session epoch(防跨 session 误用)。</summary>
    internal long Epoch { get; }

    internal CreoDetailItem(CreoSession session, ItemRef itemRef, long epoch)
        : base(session, itemRef)
    {
        Epoch = epoch;
    }
}

/// <summary>草图实体(PRO_DRAFT_ENTITY=77)。可经 <see cref="ToToken"/> 重建裸 token 供
/// <see cref="CreoDrawing.GetDtlentityData"/> 读取详细数据。</summary>
public sealed class CreoDraftEntity : CreoDetailItem
{
    internal CreoDraftEntity(CreoSession session, ItemRef itemRef, long epoch)
        : base(session, itemRef, epoch) { }

    /// <summary>重建 <see cref="CreoDtlentity"/> 裸 DATA token(零 I/O)。
    /// 出口 token 携带构造期 epoch,经 <see cref="CreoDrawing.GetDtlentityData"/>
    /// 传入时 epoch guard 自动校验。</summary>
    public CreoDtlentity ToToken()
        => new(Ref.Model.Name, Ref.Model.Type, Type, Id, Epoch);
}

/// <summary>drawing 详细注释(PRO_NOTE=68,drawing 上下文)。
/// 区别于 <see cref="CreoNote"/>(模型注释,solid/part 上下文)。</summary>
public sealed class CreoDetailNote : CreoDetailItem
{
    internal CreoDetailNote(CreoSession session, ItemRef itemRef, long epoch)
        : base(session, itemRef, epoch) { }
}

/// <summary>drawing detail symbol instance(PRO_SYMBOL_INSTANCE=76)只读对象。</summary>
public sealed class CreoDetailSymbolInstance : CreoDetailItem
{
    internal CreoDetailSymbolInstance(CreoSession session, ItemRef itemRef, long epoch)
        : base(session, itemRef, epoch) { }
}

/// <summary>drawing 表格(PRO_DRAW_TABLE=84)。</summary>
public sealed class CreoDrawingTable : CreoDetailItem
{
    internal CreoDrawingTable(CreoSession session, ItemRef itemRef, long epoch)
        : base(session, itemRef, epoch) { }
}
