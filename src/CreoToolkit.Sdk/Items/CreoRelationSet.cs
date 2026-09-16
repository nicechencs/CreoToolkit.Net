using CreoToolkit.Sdk.Native;

namespace CreoToolkit.Sdk;

/// <summary>
/// 关系集模型项(<c>PRO_RELSET</c>)。初版契约刻意只暴露通用模型项 identity/name 操作；
/// 关系条目自有 API 未排期(见 roadmap 挂账)。
/// </summary>
public sealed class CreoRelationSet : CreoModelItem
{
    internal CreoRelationSet(CreoSession session, ItemRef itemRef)
        : base(session, itemRef) { }
}
