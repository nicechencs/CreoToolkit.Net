using CreoToolkit.Sdk.Diagnostics;

namespace CreoToolkit.Sdk;

/// <summary>Native <c>ProHleStandType</c> values supported by <see cref="StandardHoleBuilder"/>.</summary>
public enum StandardHoleKind
{
    Tapped = 14,
    Clearance = 15,
    Drilled = 17,
    Tapered = 25,
}

/// <summary>Native <c>ProHleStdDepType</c> values.</summary>
public enum StandardHoleDepthType
{
    Variable = 29,
    ThruNext = 30,
    ThruAll = 31,
    ThruUntil = 40,
    ToSelected = 41,
}

public enum StandardHoleFitType
{
    Close = 0,
    Free = 1,
    Medium = 2,
}

public enum StandardThreadDepthType
{
    Thru = 32,
    Variable = 33,
}

public enum StandardHoleCreationSide
{
    SideOne = -1,
    SideTwo = 34,
}

/// <summary>Type-safe snapshot of the standard-hole configuration written to the element tree.</summary>
public readonly record struct StandardHoleSpec(
    StandardHoleKind Kind,
    StandardHoleDepthType DepthType,
    int ThreadSeriesIndex,
    int ScrewSizeIndex,
    StandardHoleFitType Fit,
    double Diameter,
    double DrillDepth,
    double ThreadDepth,
    StandardThreadDepthType ThreadDepthType,
    bool AddThread);

/// <summary>
/// 标准 tapped/clearance 孔 builder（线性放置）。thread-series 与 screw-size
/// 是激活的 Creo <c>*.hol</c> 表索引，必须由调用方提供；SDK 绝不猜测站点相关的表项。
/// </summary>
public sealed class StandardHoleBuilder
{
    private StandardHoleKind _kind = StandardHoleKind.Tapped;
    private StandardHoleDepthType _depthType = StandardHoleDepthType.Variable;
    private StandardHoleFitType _fit = StandardHoleFitType.Close;
    private StandardThreadDepthType _threadDepthType = StandardThreadDepthType.Thru;
    private StandardHoleCreationSide _creationSide = StandardHoleCreationSide.SideOne;
    private int _threadSeriesIndex = -1;
    private int _screwSizeIndex = -1;
    private double _diameter;
    private double _drillDepth;
    private double _threadDepth;
    private double _drillAngle = 118.0;
    private bool _addThread = true;
    private bool _topClearance;
    private CreoModelItem? _depthReference;
    private CreoModelItem? _placementPlane;
    private readonly List<(CreoModelItem Reference, double Distance)> _linearRefs = new();

    public StandardHoleBuilder Kind(StandardHoleKind value) { _kind = value; return this; }
    public StandardHoleBuilder DepthType(StandardHoleDepthType value) { _depthType = value; return this; }
    public StandardHoleBuilder ThreadSeriesIndex(int value) { _threadSeriesIndex = value; return this; }
    public StandardHoleBuilder ScrewSizeIndex(int value) { _screwSizeIndex = value; return this; }
    public StandardHoleBuilder Fit(StandardHoleFitType value) { _fit = value; return this; }
    public StandardHoleBuilder Diameter(double value) { _diameter = value; return this; }
    public StandardHoleBuilder DrillDepth(double value) { _drillDepth = value; return this; }
    public StandardHoleBuilder DrillAngle(double degrees) { _drillAngle = degrees; return this; }
    public StandardHoleBuilder AddThread(bool value = true) { _addThread = value; return this; }
    public StandardHoleBuilder ThroughThread() { _threadDepthType = StandardThreadDepthType.Thru; _threadDepth = 0; return this; }
    public StandardHoleBuilder ThreadDepth(double value) { _threadDepthType = StandardThreadDepthType.Variable; _threadDepth = value; return this; }
    public StandardHoleBuilder CreationSide(StandardHoleCreationSide value) { _creationSide = value; return this; }
    public StandardHoleBuilder TopClearance(bool explicitClearance = true) { _topClearance = explicitClearance; return this; }

    public StandardHoleBuilder DepthReference(CreoModelItem reference)
    {
        ThrowUtil.IfNull(reference);
        _depthReference = reference;
        return this;
    }

    public StandardHoleBuilder PlacementPlane(CreoModelItem plane)
    {
        ThrowUtil.IfNull(plane);
        _placementPlane = plane;
        return this;
    }

    public StandardHoleBuilder LinearReference(CreoModelItem reference, double distance)
    {
        ThrowUtil.IfNull(reference);
        if (_linearRefs.Count >= 2)
            throw new InvalidOperationException("线性放置只接受两个 LinearReference。");
        _linearRefs.Add((reference, distance));
        return this;
    }

    /// <summary>
    /// 报告所选标准孔变体是否已实现、跨选项组合是否合法。
    /// 必填值与放置完整性由 <see cref="Build"/> 校验。
    /// </summary>
    public HoleBuildCapability GetCapability()
    {
        if (_kind is not (StandardHoleKind.Tapped or StandardHoleKind.Clearance))
            return new(false, $"StandardHoleBuilder 当前支持 Tapped/Clearance，不支持 {_kind}。");
        if (_depthType is not (StandardHoleDepthType.Variable
            or StandardHoleDepthType.ThruNext
            or StandardHoleDepthType.ThruAll
            or StandardHoleDepthType.ThruUntil
            or StandardHoleDepthType.ToSelected))
            return new(false, $"未知或未支持的标准孔深度类型：{_depthType}。");
        if (_kind == StandardHoleKind.Clearance && _depthType == StandardHoleDepthType.Variable)
            return new(false, "Creo 不支持标准 clearance 孔使用 Variable 深度。");
        if (_kind == StandardHoleKind.Clearance && _addThread)
            return new(false, "标准 clearance 孔不能加螺纹；请调用 AddThread(false)。");
        if (_kind == StandardHoleKind.Tapped && _fit != StandardHoleFitType.Close)
            return new(false, "PTC 要求标准 tapped 孔使用 Close fit。");
        if (!Enum.IsDefined(_fit.GetType(), _fit))
            return new(false, $"未知的标准孔 fit 类型：{_fit}。");
        if (!Enum.IsDefined(_creationSide.GetType(), _creationSide))
            return new(false, $"未知的标准孔 creation side：{_creationSide}。");
        return new(true, null);
    }

    public StandardHoleSpec ToSpec() => new(
        _kind, _depthType, _threadSeriesIndex, _screwSizeIndex, _fit, _diameter,
        _drillDepth, _threadDepth, _threadDepthType, _addThread);

    public CreoFeature Build(CreoModel model)
    {
        ThrowUtil.IfNull(model);
        Validate(model);
        var tree = ToElemSpec();
        var featureId = 0;
        return CreoSdkLog.Run(
            "feature", "create_standard_hole",
            body: () =>
            {
                var item = model.Session.Run(n => n.FeatureCreateFromElemtree(model.Identity, tree));
                featureId = item.Id;
                return new CreoFeature(model.Session, item);
            },
            failProps: () => new { kind = _kind.ToString(), depth = _depthType.ToString(), diameter = _diameter },
            okProps: _ => new { id = featureId, kind = _kind.ToString(), depth = _depthType.ToString(), diameter = _diameter });
    }

    private void Validate(CreoModel model)
    {
        GetCapability().EnsureSupported();
        if (_threadSeriesIndex < 0)
            throw new InvalidOperationException("ThreadSeriesIndex 必须指向激活 Creo .hol 表中的条目。");
        if (_screwSizeIndex < 0)
            throw new InvalidOperationException("ScrewSizeIndex 必须指向所选 Creo .hol 表中的条目。");
        if (_diameter <= 0)
            throw new InvalidOperationException("标准孔直径必须为正。");
        if (_kind == StandardHoleKind.Tapped && _fit != StandardHoleFitType.Close)
            throw new InvalidOperationException("PTC 要求标准 tapped 孔使用 Close fit。");
        if (_kind == StandardHoleKind.Clearance && _addThread)
            throw new InvalidOperationException("标准 clearance 孔不能加螺纹。");
        if (_depthType == StandardHoleDepthType.Variable && _drillDepth <= 0)
            throw new InvalidOperationException("Variable 深度的标准孔需要正的 DrillDepth。");
        if (_drillAngle <= 0 || _drillAngle >= 180)
            throw new InvalidOperationException("DrillAngle 必须在 0 到 180 度之间。");
        if (_addThread && _threadDepthType == StandardThreadDepthType.Variable && _threadDepth <= 0)
            throw new InvalidOperationException("Variable 螺纹深度需要正的 ThreadDepth。");
        if (_depthType is StandardHoleDepthType.ThruUntil or StandardHoleDepthType.ToSelected
            && _depthReference is null)
            throw new InvalidOperationException($"{_depthType} 需要 DepthReference。");
        if (_placementPlane is null || _linearRefs.Count != 2)
            throw new InvalidOperationException("线性放置需要主放置面和恰好两个 LinearReference。");

        ValidateReference(model, _placementPlane, "PlacementPlane");
        foreach (var (reference, _) in _linearRefs)
            ValidateReference(model, reference, "LinearReference");
        if (_depthReference is not null)
            ValidateReference(model, _depthReference, "DepthReference");
    }

    private static void ValidateReference(CreoModel model, CreoModelItem item, string role)
    {
        item.EnsureBelongsTo(model.Session);
        if (item.Ref.Model != model.Identity)
            throw new InvalidOperationException($"{role} 必须属于目标模型(跨模型参照未支持)。");
    }

    internal ElemSpec ToElemSpec()
    {
        var com = new List<ElemSpec>
        {
            ElemSpec.Integer(HoleElementIds.PRO_E_HLE_TYPE_NEW, (int)HoleType.Standard),
            ElemSpec.Integer(HoleElementIds.PRO_E_HLE_STAN_TYPE, (int)_kind),
            ElemSpec.Integer(HoleElementIds.PRO_E_HLE_THRDSERIS, _threadSeriesIndex),
            ElemSpec.Integer(HoleElementIds.PRO_E_HLE_FITTYPE,
                (int)(_kind == StandardHoleKind.Tapped ? StandardHoleFitType.Close : _fit)),
            ElemSpec.Integer(HoleElementIds.PRO_E_HLE_SCREWSIZE, _screwSizeIndex),
            ElemSpec.Integer(HoleElementIds.PRO_E_HLE_ADD_THREAD, _addThread ? 26 : -1),
            ElemSpec.Integer(HoleElementIds.PRO_E_HLE_ADD_CBORE, -1),
            ElemSpec.Integer(HoleElementIds.PRO_E_HLE_ADD_CSINK, -1),
            ElemSpec.Double(HoleElementIds.PRO_E_HLE_HOLEDIAM, _diameter),
        };

        if (_depthType == StandardHoleDepthType.Variable)
            com.Add(ElemSpec.Double(HoleElementIds.PRO_E_HLE_DRILLANGLE, _drillAngle));

        if (_kind == StandardHoleKind.Tapped)
        {
            // PTC 要求即使无螺纹/贯通变体也要给全两个 double，特征才能在 Creo UI 里重定义。
            com.Add(ElemSpec.Double(HoleElementIds.PRO_E_HLE_THRDDEPTH, _threadDepth));
            com.Add(ElemSpec.Double(HoleElementIds.PRO_E_HLE_DRILLDEPTH, _drillDepth));
            com.Add(ElemSpec.Integer(HoleElementIds.PRO_E_HLE_THRD_DEPTH, (int)_threadDepthType));
        }

        com.Add(ElemSpec.Integer(HoleElementIds.PRO_E_HLE_DEPTH, (int)_depthType));
        if (_depthType is StandardHoleDepthType.ThruUntil or StandardHoleDepthType.ToSelected)
            com.Add(ElemSpec.Reference(HoleElementIds.PRO_E_STD_HOLE_DEPTH_REF, _depthReference!.Ref));
        if (_depthType == StandardHoleDepthType.Variable)
            com.Add(ElemSpec.Integer(HoleElementIds.PRO_E_HLE_DEPTH_DIM_TYPE, -1));
        com.Add(ElemSpec.Integer(HoleElementIds.PRO_E_HLE_CRDIR_FLIP, (int)_creationSide));
        if (_depthType == StandardHoleDepthType.ThruAll)
            com.Add(ElemSpec.Integer(HoleElementIds.PRO_E_HLE_ADD_EXIT_CSINK, -1));
        com.Add(ElemSpec.Integer(HoleElementIds.PRO_E_HLE_TOP_CLEARANCE, _topClearance ? 43 : -1));

        return ElemSpec.Compound(FeatureElementIds.PRO_E_FEATURE_TREE,
            ElemSpec.Integer(FeatureElementIds.PRO_E_FEATURE_TYPE, HoleElementIds.PRO_FEAT_HOLE),
            ElemSpec.Integer(FeatureElementIds.PRO_E_FEATURE_FORM, HoleElementIds.PRO_HLE_TYPE_STRAIGHT_FORM),
            ElemSpec.Compound(HoleElementIds.PRO_E_HLE_COM, com.ToArray()),
            ElemSpec.Compound(HoleElementIds.PRO_E_HLE_PLACEMENT,
                ElemSpec.Reference(HoleElementIds.PRO_E_HLE_PRIM_REF, _placementPlane!.Ref),
                ElemSpec.Integer(HoleElementIds.PRO_E_HLE_PL_TYPE, HoleElementIds.PRO_HLE_PL_TYPE_LIN),
                ElemSpec.Reference(HoleElementIds.PRO_E_HLE_DIM_REF1, _linearRefs[0].Reference.Ref),
                ElemSpec.Double(HoleElementIds.PRO_E_HLE_DIM_DIST1, _linearRefs[0].Distance),
                ElemSpec.Reference(HoleElementIds.PRO_E_HLE_DIM_REF2, _linearRefs[1].Reference.Ref),
                ElemSpec.Double(HoleElementIds.PRO_E_HLE_DIM_DIST2, _linearRefs[1].Distance)));
    }
}
