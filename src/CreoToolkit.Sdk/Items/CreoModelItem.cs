using CreoToolkit.Sdk.Diagnostics;
using CreoToolkit.Sdk.Native;

namespace CreoToolkit.Sdk;

/// <summary>
/// 所有 session-bound 模型项的基类。
/// 封装 session 绑定、ItemRef 持有、通用 ProModelitem 操作。
/// <para>非 abstract——未识别的 ModelItem 类型也可以实例化基类本身(降级而非 null)。</para>
/// </summary>
public class CreoModelItem : ICreoModelItem
{
    internal CreoSession Session { get; }

    internal ItemRef Ref { get; }

    internal CreoModelItem(CreoSession session, ItemRef itemRef)
    {
        Session = session;
        Ref = itemRef;
    }

    /// <inheritdoc />
    public CreoModelItemType Type => Ref.Type;

    /// <summary>模型项 id(构造时已解析进 <see cref="ItemRef"/>,零 I/O 属性)。所有衍生共用此实现。</summary>
    public int Id => Ref.Id;

    /// <summary>取模型项名字(经 ProModelitemNameGet)。无名或不可读返回 null。</summary>
    public string? GetName() => Session.Run(n => n.ModelitemNameGet(Ref));

    /// <summary>改名(ProModelitemNameSet,动作型)。空串/全空白入参立即异常。</summary>
    public void SetName(string name) =>
        CreoSdkLog.Run(
            "item", "setname",
            body: () =>
            {
                ThrowUtil.IfNullOrWhiteSpace(name);
                Session.Run(n => { n.ModelitemNameSet(Ref, name); return 0; });
            },
            failProps: () => new { model = Ref.Model.Name, type = Ref.Type.ToString(), id = Ref.Id, name });

    /// <summary>查询本项是否允许改名(ProModelitemNameCanChange)。不可读返回 null。</summary>
    public bool? CanRename() => Session.Run(n => n.ModelitemNameCanChange(Ref));

    /// <summary>取默认名(ProModelitemDefaultnameGet);不可读返回 null。</summary>
    public string? GetDefaultName() => Session.Run(n => n.ModelitemDefaultnameGet(Ref));

    /// <summary>清除用户自定义名字(ProModelitemUsernameDelete,动作型)。</summary>
    public void ClearUserName() =>
        CreoSdkLog.Run(
            "item", "clearusername",
            body: () => Session.Run(n => { n.ModelitemUsernameDelete(Ref); return 0; }),
            failProps: () => new { model = Ref.Model.Name, type = Ref.Type.ToString(), id = Ref.Id });

    /// <summary>值判等: 基于 (Model, Type, Id) 三元组。
    /// <para>老包按 (id,owner,type) 也判等但 GetHashCode 返常量 0(hash 退化)——
    /// 新 SDK 用 record struct ItemRef 自带值语义,基类 Equals/HashCode 一路转发。</para></summary>
    public override bool Equals(object? obj)
        => obj is CreoModelItem other && other.Ref == Ref;

    /// <inheritdoc />
    public override int GetHashCode() => Ref.GetHashCode();

    /// <summary>调试友好的字符串,格式 "{ClassName}({Type}#{Id}@{ModelName})"。</summary>
    public override string ToString()
        => $"{GetType().Name}({Ref.Type}#{Ref.Id}@{Ref.Model.Name})";

    /// <summary>归属模型名(零 I/O;构造时已解析进内部 ItemRef)。
    /// drawing 建的尺寸默认归属关联 solid 而非 drawing 本身(tkuse 1325),此属性可辨。</summary>
    public string OwnerModelName => Ref.Model.Name;

    /// <summary>安全 downcast 到具体衍生类型(治老包 implicit operator 吃错误的坑)。
    /// 不是该类型返回 null,绝不抛 InvalidCastException。</summary>
    public T? As<T>() where T : CreoModelItem => this as T;

    internal void EnsureBelongsTo(CreoSession session)
    {
        if (!ReferenceEquals(Session, session))
            throw new InvalidOperationException($"{GetType().Name} 不属于该会话。");
    }

    /// <summary>
    /// 共享工厂：按 <see cref="ItemRef.Type"/> 分流实例化具体衍生类。
    /// 已识别类型返对应衍生实例；未识别类型降级返基类 <see cref="CreoModelItem"/> 本身（不返 null）。
    /// </summary>
    internal static CreoModelItem CreateFromRef(CreoSession session, ItemRef itemRef)
        => itemRef.Type switch
        {
            // DHandle 衍生
            CreoModelItemType.Feature      => new CreoFeature(session, itemRef),
            CreoModelItemType.Dimension    => new CreoDimension(session, itemRef),
            CreoModelItemType.RefDimension => new CreoRefDimension(session, itemRef),
            CreoModelItemType.Note         => new CreoNote(session, itemRef),
            CreoModelItemType.Layer        => new CreoLayer(session, itemRef),
            // OHandle 衍生
            CreoModelItemType.Surface      => new CreoSurface(session, itemRef),
            CreoModelItemType.Edge         => new CreoEdge(session, itemRef),
            CreoModelItemType.Axis         => new CreoAxis(session, itemRef),
            CreoModelItemType.Csys         => new CreoCsys(session, itemRef),
            CreoModelItemType.Quilt        => new CreoQuilt(session, itemRef),
            CreoModelItemType.Curve        => new CreoCurve(session, itemRef),
            CreoModelItemType.Point        => new CreoPoint(session, itemRef),
            CreoModelItemType.RelationSet  => new CreoRelationSet(session, itemRef),
            // 未识别类型降级，不返 null
            _                              => new CreoModelItem(session, itemRef),
        };
}
