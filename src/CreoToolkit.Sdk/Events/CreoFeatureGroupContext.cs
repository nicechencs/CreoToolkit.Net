namespace CreoToolkit.Sdk.Events;

/// <summary>group ungroup 通知 DATA 快照(non-positional readonly struct,IEquatable)。
/// 不暴露 native handle;wrapper 内部已用短小 read-only API 拆 owner + member ids。
/// 入参 handler 于 Creo 主线程同步触发。</summary>
public readonly struct CreoFeatureGroupContext : IEquatable<CreoFeatureGroupContext>
{
    /// <summary>group 自身 pro_model_item.id(从 group ptr 直接读)。</summary>
    public int GroupId { get; }

    /// <summary>group 显示名:UDF group 经 ProUdfNameGet 实名;local group 或取名失败 → null。</summary>
    public string? Name { get; }

    /// <summary>group 所在模型名(owner Mdl 经 ProModelitemMdlGet + ProMdlMdlnameGet 拆)。</summary>
    public string OwnerModelName { get; }

    /// <summary>group 所在模型类型(ProMdlTypeGet)。</summary>
    public CreoModelType OwnerModelType { get; }

    /// <summary>是否表驱动(ProGroupIsTabledriven):
    /// true=UDF 表驱动;false=UDF 非表驱动;null=local group(BAD_CONTEXT)或查询失败。</summary>
    public bool? IsTabledriven { get; }

    /// <summary>成员 feature ids 快照(经 ProGroupFeaturesCollect → pro_model_item.id 遍历);
    /// 收集失败返空列表(handler 仍调,不当整体失败)。</summary>
    public IReadOnlyList<int> MemberFeatureIds { get; }

    internal CreoFeatureGroupContext(
        int groupId,
        string? name,
        string ownerModelName,
        CreoModelType ownerModelType,
        bool? isTabledriven,
        IReadOnlyList<int> memberFeatureIds)
    {
        ThrowUtil.IfNull(ownerModelName);
        ThrowUtil.IfNull(memberFeatureIds);
        GroupId = groupId;
        Name = name;
        OwnerModelName = ownerModelName;
        OwnerModelType = ownerModelType;
        IsTabledriven = isTabledriven;
        MemberFeatureIds = memberFeatureIds;
    }

    public bool Equals(CreoFeatureGroupContext other) =>
        GroupId == other.GroupId
        && string.Equals(Name, other.Name, StringComparison.Ordinal)
        && string.Equals(OwnerModelName, other.OwnerModelName, StringComparison.Ordinal)
        && OwnerModelType == other.OwnerModelType
        && IsTabledriven == other.IsTabledriven
        && MemberFeatureIds.SequenceEqual(other.MemberFeatureIds);

    public override bool Equals(object? obj) => obj is CreoFeatureGroupContext other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(GroupId, Name, OwnerModelName, OwnerModelType, IsTabledriven);

    public override string ToString() =>
        $"FeatureGroup(id={GroupId}, name={Name ?? "<unnamed>"}, owner={OwnerModelName}.{OwnerModelType}, members={MemberFeatureIds.Count})";

    public static bool operator ==(CreoFeatureGroupContext left, CreoFeatureGroupContext right) => left.Equals(right);
    public static bool operator !=(CreoFeatureGroupContext left, CreoFeatureGroupContext right) => !left.Equals(right);
}
