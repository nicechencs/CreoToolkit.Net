using CreoToolkit.Sdk.Diagnostics;

namespace CreoToolkit.Sdk;

/// <summary>从现有孔特征读取参数快照。依赖深度元素树导出。</summary>
public static class HoleSpecReader
{
    /// <summary>
    /// 从已有特征的深度元素树读取孔参数。特征不是孔(type!=911)或读取失败返回 null。
    /// 元素 id 与枚举值均为头文件真值(见 <see cref="HoleElementIds"/>);
    /// 直径:straight 孔在 PRO_E_DIAMETER,standard 孔在 PRO_E_HLE_HOLEDIAM,取非零者。
    /// </summary>
    public static HoleSpec? FromFeature(CreoFeature feature)
    {
        ThrowUtil.IfNull(feature);

        var info = feature.GetInfo();
        if (info is not { FeatureType: HoleElementIds.PRO_FEAT_HOLE }) return null;

        using var tree = feature.ExtractElementTreeDeep();
        var nodes = tree.Walk().ToList();

        double diameter = 0;
        double depth = 0;
        var holeType = HoleType.Unknown;
        var depthType = HoleDepthType.Unknown;

        foreach (var node in nodes)
        {
            switch (node.ElementId)
            {
                case HoleElementIds.PRO_E_DIAMETER or HoleElementIds.PRO_E_HLE_HOLEDIAM
                    when node.ValueKind == CreoElementValueKind.Double && node.DoubleValue != 0:
                    diameter = node.DoubleValue;
                    break;
                case HoleElementIds.PRO_E_EXT_DEPTH_TO_VALUE
                    when node.ValueKind == CreoElementValueKind.Double:
                    depth = node.DoubleValue;
                    break;
                case HoleElementIds.PRO_E_HLE_DRILLDEPTH
                    when node.ValueKind == CreoElementValueKind.Double && node.DoubleValue != 0:
                    depth = node.DoubleValue;
                    break;
                case HoleElementIds.PRO_E_HLE_TYPE_NEW
                    when node.ValueKind == CreoElementValueKind.Integer:
                    holeType = Enum.IsDefined(typeof(HoleType), node.IntValue)
                        ? (HoleType)node.IntValue : HoleType.Unknown;
                    break;
                case HoleElementIds.PRO_E_HOLE_DEPTH_TO_TYPE
                    when node.ValueKind == CreoElementValueKind.Integer:
                    depthType = Enum.IsDefined(typeof(HoleDepthType), node.IntValue)
                        ? (HoleDepthType)node.IntValue : HoleDepthType.Unknown;
                    break;
            }
        }

        return new HoleSpec(diameter, depth, holeType, depthType);
    }
}

/// <summary>
/// 孔特征 fluent builder。当前支持 straight 孔 + 线性放置(主放置面 + 两个线性参照/偏距)，
/// 深度类型已放行 Blind/ThruNext/ThruAll/ThruUntil/UptoRef(后二者需 DepthReference);
/// Sketched/Standard 类型与径向/同轴放置未实现,Build 时明确拒绝(Standard 走 StandardHoleBuilder)。
/// 子树蓝本:官方样例 UgHoleCreate.c + 内部 fixture 真机 dump。
/// </summary>
public sealed class HoleBuilder
{
    private double _diameter;
    private double _depth;
    private HoleType _type = HoleType.Straight;
    private HoleDepthType _depthType = HoleDepthType.Blind;
    private CreoModelItem? _depthReference;
    private CreoModelItem? _placementPlane;
    private readonly List<(CreoModelItem Reference, double Distance)> _linearRefs = new();

    public HoleBuilder Diameter(double d) { _diameter = d; return this; }
    public HoleBuilder Depth(double d) { _depth = d; return this; }
    /// <summary>设置孔型。调用 <see cref="GetCapability"/> 可在 Build 前判断该组合是否已实现。</summary>
    public HoleBuilder Type(HoleType t) { _type = t; return this; }
    /// <summary>设置深度类型。调用 <see cref="GetCapability"/> 可在 Build 前判断该组合是否已实现。</summary>
    public HoleBuilder DepthType(HoleDepthType dt) { _depthType = dt; return this; }

    /// <summary>深度终止参照(ThruUntil/UptoRef 必需)。</summary>
    public HoleBuilder DepthReference(CreoModelItem reference)
    {
        ThrowUtil.IfNull(reference);
        _depthReference = reference;
        return this;
    }

    /// <summary>返回当前 Type/DepthType 的明确能力状态，不执行 native 调用。</summary>
    public HoleBuildCapability GetCapability()
        => HoleBuilderCapabilities.Evaluate(_type, _depthType);

    /// <summary>主放置面(平面 surface / 基准面)。</summary>
    public HoleBuilder PlacementPlane(CreoModelItem plane)
    {
        ThrowUtil.IfNull(plane);
        _placementPlane = plane;
        return this;
    }

    /// <summary>线性定位参照 + 偏距(需恰好两个;参照可为面/基准面/边)。</summary>
    public HoleBuilder LinearReference(CreoModelItem reference, double distance)
    {
        ThrowUtil.IfNull(reference);
        if (_linearRefs.Count >= 2)
            throw new InvalidOperationException("线性放置只接受两个 LinearReference。");
        _linearRefs.Add((reference, distance));
        return this;
    }

    /// <summary>参数快照(不含放置)。</summary>
    public HoleSpec ToSpec() => new(_diameter, _depth, _type, _depthType);

    /// <summary>
    /// 在模型上创建孔特征(经通用元素树构造链)。
    /// 仅支持 <see cref="HoleType.Straight"/>,深度类型支持 Blind/ThruNext/ThruAll/ThruUntil/UptoRef
    /// (能力面见 <see cref="GetCapability"/>),其余类型抛 <see cref="NotSupportedException"/>;
    /// 放置信息不完整抛 <see cref="InvalidOperationException"/>。
    /// </summary>
    public CreoFeature Build(CreoModel model)
    {
        ThrowUtil.IfNull(model);
        Validate(model);
        var spec = ToElemSpec();
        int fid = 0;
        return CreoSdkLog.Run(
            "feature", "create_hole",
            body: () =>
            {
                var fr = model.Session.Run(n => n.FeatureCreateFromElemtree(model.Identity, spec));
                fid = fr.Id;
                return new CreoFeature(model.Session, fr);
            },
            failProps: () => new { diameter = _diameter, depth = _depth },
            okProps: _ => new { id = fid, diameter = _diameter, depth = _depth });
    }

    private void Validate(CreoModel model)
    {
        GetCapability().EnsureSupported();
        if (_placementPlane is null)
            throw new InvalidOperationException("缺少 PlacementPlane(线性放置必需主放置面)。");
        if (_linearRefs.Count != 2)
            throw new InvalidOperationException(
                $"线性放置需要恰好两个 LinearReference,当前 {_linearRefs.Count} 个。");
        if (_diameter <= 0)
            throw new InvalidOperationException($"直径必须为正,当前 {_diameter}。");
        if (_depthType == HoleDepthType.Blind && _depth <= 0)
            throw new InvalidOperationException($"Blind 深度必须为正,当前 {_depth}。");
        if (_depthType is HoleDepthType.ThruUntil or HoleDepthType.UptoRef && _depthReference is null)
            throw new InvalidOperationException($"{_depthType} 需要 DepthReference。");

        _placementPlane.EnsureBelongsTo(model.Session);
        if (_placementPlane.Ref.Model != model.Identity)
            throw new InvalidOperationException("放置面必须属于目标模型(跨模型参照未支持)。");
        foreach (var (r, _) in _linearRefs)
        {
            r.EnsureBelongsTo(model.Session);
            if (r.Ref.Model != model.Identity)
                throw new InvalidOperationException("线性参照必须属于目标模型(跨模型参照未支持)。");
        }
        if (_depthReference is not null)
        {
            _depthReference.EnsureBelongsTo(model.Session);
            if (_depthReference.Ref.Model != model.Identity)
                throw new InvalidOperationException("DepthReference 必须属于目标模型(跨模型参照未支持)。");
        }
    }

    /// <summary>组装 straight+linear 孔元素树 spec(结构对照 ProHole.h 头注/UgHoleCreate.c)。</summary>
    internal ElemSpec ToElemSpec()
    {
        var depthChildren = new List<ElemSpec>
        {
            ElemSpec.Integer(HoleElementIds.PRO_E_HOLE_DEPTH_TO_TYPE, (int)_depthType),
        };
        if (_depthType == HoleDepthType.Blind)
            depthChildren.Add(ElemSpec.Double(HoleElementIds.PRO_E_EXT_DEPTH_TO_VALUE, _depth));
        else if (_depthType is HoleDepthType.ThruUntil or HoleDepthType.UptoRef)
            depthChildren.Add(ElemSpec.Reference(HoleElementIds.PRO_E_EXT_DEPTH_TO_REF, _depthReference!.Ref));
        var depthTo = ElemSpec.Compound(HoleElementIds.PRO_E_HOLE_DEPTH_TO, depthChildren.ToArray());

        return ElemSpec.Compound(FeatureElementIds.PRO_E_FEATURE_TREE,
            ElemSpec.Integer(FeatureElementIds.PRO_E_FEATURE_TYPE, HoleElementIds.PRO_FEAT_HOLE),
            ElemSpec.Integer(FeatureElementIds.PRO_E_FEATURE_FORM, HoleElementIds.PRO_HLE_TYPE_STRAIGHT_FORM),
            ElemSpec.Compound(HoleElementIds.PRO_E_HLE_COM,
                ElemSpec.Integer(HoleElementIds.PRO_E_HLE_TYPE_NEW, (int)HoleType.Straight),
                ElemSpec.Integer(HoleElementIds.PRO_E_HLE_MAKE_LIGHTWT, HoleElementIds.PRO_HLE_REGULAR),
                ElemSpec.Double(HoleElementIds.PRO_E_DIAMETER, _diameter),
                ElemSpec.Compound(HoleElementIds.PRO_E_HOLE_STD_DEPTH,
                    depthTo,
                    ElemSpec.Compound(HoleElementIds.PRO_E_HOLE_DEPTH_FROM,
                        ElemSpec.Integer(HoleElementIds.PRO_E_HOLE_DEPTH_FROM_TYPE,
                            HoleElementIds.PRO_HLE_STRGHT_NONE_DEPTH))),
                ElemSpec.Integer(HoleElementIds.PRO_E_HLE_TOP_CLEARANCE,
                    HoleElementIds.PRO_HOLE_GEN_CLRNCE)),
            ElemSpec.Compound(HoleElementIds.PRO_E_HLE_PLACEMENT,
                ElemSpec.Reference(HoleElementIds.PRO_E_HLE_PRIM_REF, _placementPlane!.Ref),
                ElemSpec.Integer(HoleElementIds.PRO_E_HLE_PL_TYPE, HoleElementIds.PRO_HLE_PL_TYPE_LIN),
                ElemSpec.Reference(HoleElementIds.PRO_E_HLE_DIM_REF1, _linearRefs[0].Reference.Ref),
                ElemSpec.Double(HoleElementIds.PRO_E_HLE_DIM_DIST1, _linearRefs[0].Distance),
                ElemSpec.Reference(HoleElementIds.PRO_E_HLE_DIM_REF2, _linearRefs[1].Reference.Ref),
                ElemSpec.Double(HoleElementIds.PRO_E_HLE_DIM_DIST2, _linearRefs[1].Distance)));
    }
}
