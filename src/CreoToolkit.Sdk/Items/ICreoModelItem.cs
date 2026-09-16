namespace CreoToolkit.Sdk;

/// <summary>
/// session-bound 模型项契约。
/// <para>
/// 治"strict-typed selection"的字符串化漏洞 — 原 <c>CreoSelection.Label</c> 是字符串
/// (<c>type=...,id=...</c>),业务侧分流靠 parse,易碎。本接口让 selection 出口走强类型分流
/// (<c>switch (sel.Item) { case CreoFeature f: ... }</c>)。
/// </para>
/// <para>
/// v1 衍生只 <see cref="CreoFeature"/> 实现;Surface/Edge/Axis 等其余 10 衍生留 future。
/// <c>Owner: CreoModel</c> 暂不入接口(允许 <c>feature.Owner.Erase()</c> 后
/// <c>feature.Name()</c> 行为未定义);未来实施时通过 internal 桥接取。
/// </para>
/// <para>
/// <b>Id 是零 I/O 属性</b>:所有衍生的 id 在构造时已解析进内部 <c>ItemRef</c>
/// (DHandle 结构字段 / OHandle 构造期已 resolve),读取只回取内存值,不触发 Pro 往返,
/// 故统一属性化不隐藏 I/O,提升到接口消除各衍生类重复声明。
/// </para>
/// </summary>
public interface ICreoModelItem
{
    /// <summary>模型项类型标识。</summary>
    CreoModelItemType Type { get; }

    /// <summary>模型项 id(构造时已解析进内部 ItemRef,零 I/O 属性)。</summary>
    int Id { get; }

    /// <summary>取模型项名字(经 ProModelitemNameGet)。无名或不可读返回 null。</summary>
    string? GetName();
}
