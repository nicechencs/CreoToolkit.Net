namespace CreoToolkit.Sdk;

/// <summary>draft entity DATA 快照(对应 native <c>ProDtlentity</c> = <c>ProModelitem</c> POD typedef)。
/// 不持 native 句柄;Owner/Type/Id/SessionEpoch 五元组重建 ProModelitem 即可。
/// <para><b>lifecycle 边界</b>:
/// <list type="bullet">
/// <item>跨 session 误用(<see cref="SessionEpoch"/> 不匹配)→ <see cref="CreoDtlentityGuard.SameSessionEpoch"/> 抛 <see cref="InvalidOperationException"/></item>
/// <item>owner 模型 erase 后 → bridge 返 null + trace reason=owner_unresolved</item>
/// <item>同名跨 Load(不同 ProMdl)id 失效 → bridge 返 null + trace reason=entity_notfound</item>
/// </list></para></summary>
public readonly struct CreoDtlentity : IEquatable<CreoDtlentity>
{
    internal string OwnerModelName { get; }

    internal CreoModelType OwnerModelType { get; }

    /// <summary>SDK enum 隔离 native <c>ProType</c>。
    /// <para>当前恒为 <see cref="CreoModelItemType.DraftEntity"/>(对应 native <c>PRO_DRAFT_ENTITY=77</c>);
    /// future dimension/note 等 enum 内 type 才会用其他值。</para></summary>
    public CreoModelItemType Type { get; }

    /// <summary>entity id(对应 <c>ProModelitem.id</c>)。</summary>
    public int Id { get; }

    /// <summary>绑定的 session epoch (long, 防序列回绕撞同值);
    /// 与 <see cref="CreoSession.CurrentEpoch"/> 不匹配时
    /// <see cref="CreoDtlentityGuard.SameSessionEpoch"/> 抛 <see cref="InvalidOperationException"/>。
    /// <para>default 值时为 0(也无效,经 <see cref="IsUninitialized"/> guard 拒)。</para>
    /// <para><b>不支持跨进程序列化</b>:epoch 是 process-local <c>Interlocked.Increment</c> 计数,
    /// 跨进程不可比;public 面零 <c>Serializable</c> attribute,业务侧不应自行序列化。</para>
    /// <para><b>equality 语义</b>:<see cref="Equals(CreoDtlentity)"/> 含 <see cref="SessionEpoch"/>
    /// (lifecycle capability),跨 session 同 id 不同 epoch 不算同一 entity;若要"按业务 id 去重"
    /// 应自行提供 comparer/key projection,不依赖 <see cref="Equals(CreoDtlentity)"/>。</para></summary>
    internal long SessionEpoch { get; }

    internal CreoDtlentity(string ownerModelName, CreoModelType ownerType, CreoModelItemType type, int id, long sessionEpoch)
    {
        OwnerModelName = ownerModelName;
        OwnerModelType = ownerType;
        Type = type;
        Id = id;
        SessionEpoch = sessionEpoch;
    }

    /// <summary>是否为未初始化的 default 值(<c>default(CreoDtlentity).OwnerModelName</c> 是 null)。
    /// <para>对 default 值调用 SDK API 触发 <see cref="InvalidOperationException"/>。</para></summary>
    public bool IsUninitialized => OwnerModelName is null;

    public bool Equals(CreoDtlentity other) =>
        OwnerModelName == other.OwnerModelName &&
        OwnerModelType == other.OwnerModelType &&
        Type == other.Type &&
        Id == other.Id &&
        SessionEpoch == other.SessionEpoch;

    public override bool Equals(object? obj) => obj is CreoDtlentity other && Equals(other);

    // 显式 GetHashCode 保 Dictionary key 行为可预测;
    // SessionEpoch 纳入 hash, 跨 session 同 id 不同 epoch 不冲撞
    public override int GetHashCode() => HashCode.Combine(OwnerModelName, OwnerModelType, Type, Id, SessionEpoch);

    public static bool operator ==(CreoDtlentity left, CreoDtlentity right) => left.Equals(right);
    public static bool operator !=(CreoDtlentity left, CreoDtlentity right) => !left.Equals(right);
}
