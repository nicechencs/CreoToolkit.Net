using CreoToolkit.Sdk.Native;

namespace CreoToolkit.Sdk;

/// <summary>零件(Part):继承 CreoSolid。Part 独有能力(Material/Density)。</summary>
public sealed class CreoPart : CreoSolid
{
    internal CreoPart(CreoSession session, ModelIdentity id) : base(session, id) { }

    /// <summary>列出所有材料名称(DATA 快照); 无材料或不支持返回空列表。</summary>
    public IReadOnlyList<string> ListMaterialNames()
        => _session.Run(n => n.PartMaterialNames(Identity));

    /// <summary>当前材料名; 未配置返回 null。</summary>
    public string? GetCurrentMaterial()
        => _session.Run(n => n.PartMaterialCurrent(Identity));

    /// <summary>零件密度; 不支持返回 null。</summary>
    public double? GetDensity()
        => _session.Run(n => n.PartDensityGet(Identity));
}
