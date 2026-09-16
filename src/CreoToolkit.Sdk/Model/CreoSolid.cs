using CreoToolkit.Sdk.Diagnostics;
using CreoToolkit.Sdk.Errors;
using CreoToolkit.Sdk.Native;
using CreoToolkit.Sdk.Units;

namespace CreoToolkit.Sdk;

/// <summary>实体模型(Part/Assembly)共同层(对应 ProSolid):3D 几何模型,可重生成、可挂 feature。
/// PTC 三套 SDK 真实中间层(ProSolid/pfcSolid/IpfcSolid);把"仅实体有效"操作放这层。
/// 具体类型 <see cref="CreoPart"/>/<see cref="CreoAssembly"/> 由工厂分流。</summary>
public class CreoSolid : CreoModel
{
    internal CreoSolid(CreoSession session, ModelIdentity id) : base(session, id) { }

    /// <summary>重生成模型(动作型, 经 dispatcher; 失败抛 <see cref="CreoException"/>)。
    /// 仅 Part/Assembly 有效。</summary>
    public void Regenerate() =>
        CreoSdkLog.Run(
            "model", "regenerate",
            body: () => _session.Run(n => n.ModelRegenerate(Identity)),
            failProps: () => new { name = Identity.Name, type = Identity.Type.ToString() });

    /// <summary>主动显示实体模型(经 <c>ProMdlDisplay</c>)。<c>pt_examples/pt_dbase/TestDbms.c</c> 的 display 切片。
    /// 仅 Part/Assembly 有效(2D Drawing 不走此入口)。</summary>
    public void Display() =>
        CreoSdkLog.Run(
            "model", "display",
            body: () => _session.Run(n => n.ModelDisplay(Identity)),
            failProps: () => new { name = Identity.Name, type = Identity.Type.ToString() });

    /// <summary>取再生包围盒(基坐标系,DATA 快照)。仅 Part/Assembly 有效。</summary>
    public CreoBoundingBox GetOutline() => _session.Run(n => n.ModelOutlineGet(Identity));

    /// <summary>取质量属性(DATA 快照,v1:7 个关键标量)。仅 Part/Assembly 有效。</summary>
    public CreoMassProperty GetMassProperty() => _session.Run(n => n.ModelMassPropertyGet(Identity));

    /// <summary>取指定坐标系下的质量属性(DATA 快照,v1:7 个关键标量)。仅 Part/Assembly 有效。</summary>
    public CreoMassProperty GetMassProperty(string coordinateSystemName)
    {
        ThrowUtil.IfNullOrWhiteSpace(coordinateSystemName);
        if (coordinateSystemName.Length > 31)
            throw new ArgumentException("坐标系名长度必须 <= 31 字符(ProName 上限)。", nameof(coordinateSystemName));

        return _session.Run(n => n.ModelMassPropertyGet(Identity, coordinateSystemName));
    }

    // ---- ProSolid 共享 API ----

    /// <summary>取实体再生状态 int(ProSolidRegenerationStatusGet); 不可读返回 null。</summary>
    public int? GetRegenerationStatus() => _session.Run(n => n.SolidRegenerationStatusGet(Identity));

    /// <summary>计算实体包围盒(ProSolidOutlineCompute,基坐标系且不排除几何); 不可读返回 null。</summary>
    public CreoBoundingBox? ComputeOutline()
        => _session.Run(n => n.SolidOutlineCompute(Identity));

    /// <summary>旧参数兼容入口；method/coordSys 从未映射到 native，请改用 <see cref="ComputeOutline()"/>。</summary>
    [Obsolete("Use ComputeOutline() (无参) instead. method/coordSys 从未转发到 native ProSolidOutlineCompute。", error: false)]
    public CreoBoundingBox? ComputeOutline(int method = 0, int coordSys = 0)
    {
        _ = method;
        _ = coordSys;
        return ComputeOutline();
    }

    /// <summary>取实体精度(ProSolidAccuracyGet); 不可读返回 null。</summary>
    public SolidAccuracyRecord? GetAccuracy() => _session.Run(n => n.SolidAccuracyGet(Identity));

    /// <summary>查询实体是否处于 noresolve 模式(ProSolidIsNoresolveMode); 不可读返回 null。</summary>
    public bool? IsNoresolveMode() => _session.Run(n => n.SolidIsNoresolveMode(Identity));

    /// <summary>列出失败特征 id(ProSolidFailedfeaturesList); 无失败返回空列表。</summary>
    public IReadOnlyList<int> ListFailedFeatureIds() => _session.Run(n => n.SolidFailedFeatures(Identity));

    /// <summary>列出失败特征对象; 无失败返回空列表。</summary>
    public IReadOnlyList<CreoFeature> ListFailedFeatures()
    {
        var ids = _session.Run(n => n.SolidFailedFeatures(Identity));
        if (ids.Count == 0) return Array.Empty<CreoFeature>();
        var result = new CreoFeature[ids.Count];
        for (int i = 0; i < ids.Count; i++)
            result[i] = new CreoFeature(_session, new ItemRef(Identity, CreoModelItemType.Feature, ids[i]));
        return result;
    }

    /// <summary>读取指定 cable(<see cref="CreoModelItemType.Cable"/>=PRO_CABLE=96)每段末端的
    /// boundary(<c>ProCableLocationsOnSegEndGet</c>)。每段 boundary = (start, end) 两个 model item
    /// 快照(TypeCode + Id)。<br/>
    /// 模型/cable 不存在返 null;无段(空 cable)返空列表。挂 CreoSolid:cable 只存在于
    /// harness part / assembly,不给 Drawing 等继承。<br/>
    /// <b>实验 API / 所有权语义</b>: PTC 头文件未明写 free 契约,Bridge 内保守 borrowed
    /// 不做 ProArrayFree；每次非空读取都有运行时诊断计数。高频调用可能累积 native 内存，
    /// 待 PTC 书面澄清或专用真机压力门禁确认后才能转 owned 并补 free。<br/>
    /// <b>真机验证状态</b>:有效 cable 数据流未经真机验证(fixture 无 cable 模型);
    /// 已验证的仅为"无效 id 返 rc=0+空数组不 crash"。</summary>
    [System.Diagnostics.CodeAnalysis.Experimental(
        CablingBoundaryOwnershipPolicy.ExperimentalDiagnosticId,
        UrlFormat = "https://support.ptc.com/help/creo_toolkit/protoolkit_pma/r12/usascii/creo_toolkit/api/dita/335.html")]
    public IReadOnlyList<CableSegmentBoundary>? GetCableSegmentBoundaries(int cableId)
    {
        var itemRef = new ItemRef(Identity, CreoModelItemType.Cable, cableId);
        var result = _session.Run(n => n.CableLocationsOnSegEndGet(itemRef));
        CreoSdkLog.Trace("solid", "cable.segment.boundaries",
            new { model = Identity.Name, cableId, count = result?.Count });
        return result;
    }

    // ---- CreoSolid 新增方法 ----

    /// <summary>检查 Family Table 状态 int（Ctk_SolidFamtableCheck）；不可读返回 null。0=OK,其他值=异常状态。</summary>
    public int? CheckFamilyTable() => _session.Run(n => n.SolidFamtableCheckStatus(Identity));

    /// <summary>查询模型是否为 Family Table 实例（Ctk_SolidIsFaminstance）；不可读返回 null。</summary>
    public bool? IsFamilyInstance() => _session.Run(n => n.SolidIsFaminstance(Identity));

    /// <summary>列出族表中所有实例名；无族表返空列表。</summary>
    public IReadOnlyList<string> ListFamilyInstanceNames()
        => _session.Run(n => n.FamilyTableInstanceNames(Identity));

    /// <summary>取当前实例对应的类属(Generic)模型名；非实例返 null。</summary>
    public string? GetGenericName()
        => _session.Run(n => n.FamilyInstanceGenericName(Identity));

    // ---- P7 Solid Visitor ----

    /// <summary>列出实体轴(DATA 快照)；无则返空列表。</summary>
    public IReadOnlyList<ICreoModelItem> ListAxes()
        => Materialize(_session.Run(n => n.SolidAxisList(Identity)));

    /// <summary>列出坐标系(DATA 快照)；无则返空列表。</summary>
    public IReadOnlyList<ICreoModelItem> ListCsys()
        => Materialize(_session.Run(n => n.SolidCsysList(Identity)));

    /// <summary>列出曲面(DATA 快照)；无则返空列表。</summary>
    public IReadOnlyList<ICreoModelItem> ListSurfaces()
        => Materialize(_session.Run(n => n.SolidSurfaceList(Identity)));

    /// <summary>列出面组(DATA 快照)；无则返空列表。</summary>
    public IReadOnlyList<ICreoModelItem> ListQuilts()
        => Materialize(_session.Run(n => n.SolidQuiltList(Identity)));

    /// <summary>列出尺寸(DATA 快照)；includeRefDimensions=true 时含参考尺寸。</summary>
    public IReadOnlyList<ICreoModelItem> ListDimensions(bool includeRefDimensions = false)
        => Materialize(_session.Run(n => n.SolidDimensionList(Identity, includeRefDimensions)));

    /// <summary>列出强类型关系集(DATA 快照)；无则返空列表。
    /// forward-compat：native 返回的未知类型条目跳过并记 Warn（不抛），只返已识别项。</summary>
    public IReadOnlyList<CreoRelationSet> ListRelationSets()
    {
        var refs = _session.Run(n => n.SolidRelsetList(Identity));
        if (refs.Count == 0) return Array.Empty<CreoRelationSet>();
        var result = new List<CreoRelationSet>(refs.Count);
        foreach (var r in refs)
        {
            if (r.Type != CreoModelItemType.RelationSet)
            {
                CreoSdkLog.Warn("solid", "relset.list.unknown-type",
                    "SolidRelsetList 返回未知模型项类型，已跳过（forward-compat）",
                    new { model = Identity.Name, type = r.Type.ToString(), rawType = (int)r.Type, id = r.Id });
                continue;
            }
            result.Add(new CreoRelationSet(_session, r));
        }
        return result;
    }

    /// <summary>
    /// 列出 datum plane 复合视图:datum plane 特征(<c>PRO_FEAT_DATUM</c>=923)所辖的
    /// 平面 surface(<c>PRO_SRF_PLANE</c>=34)。纯 L3 组合,不新增 native 调用:
    /// <see cref="CreoFeatures.ListDatumPlaneFeatures"/> → 每个 datum feat 的
    /// <see cref="CreoFeature.ListGeomItems"/>(Surface) → <see cref="CreoSurface.GetSurfaceType"/> 筛 PLANE。
    /// 无 datum plane / 非实体返空列表。
    /// <para>头文件不提供"surface → owner feature"反查路径,故走 feature-first 组装
    /// (语义等价:datum plane feature 就是 datum plane surface 的产生者)。</para>
    /// </summary>
    public IReadOnlyList<CreoSurface> ListDatumPlanes()
    {
        var datumFeats = _session.Features.ListDatumPlaneFeatures(this);
        if (datumFeats.Count == 0) return Array.Empty<CreoSurface>();
        var result = new List<CreoSurface>();
        foreach (var feat in datumFeats)
        {
            var geomItems = feat.ListGeomItems(CreoModelItemType.Surface);
            if (geomItems.Count == 0)
            {
                // datum feat 存在但 geomitem 集空:与"根本没有 datum plane"区分,
                // 便于开发期定位为什么少收(fixture 未预置 / native 边缘态)。
                CreoSdkLog.Trace("solid", "listdatumplanes.empty_geomitems",
                    new { model = Identity.Name, featureId = feat.Id });
                continue;
            }
            foreach (var gi in geomItems)
            {
                if (gi is not CreoSurface surf) continue;
                // PLANE=34 校验属兜底:PRO_FEAT_DATUM 依 PTC 语义只产平面 surface,
                // 但保留显式过滤避免 native 边缘态漏筛。SurfaceTypeGet null 视为不匹配。
                var srfType = surf.GetSurfaceType();
                if (srfType is null)
                {
                    CreoSdkLog.Trace("solid", "listdatumplanes.null_type",
                        new { model = Identity.Name, featureId = feat.Id, surfaceId = surf.Id });
                    continue;
                }
                if (srfType != (int)CreoToolkit.Interop.Generated.pro_srf_type.PRO_SRF_PLANE)
                {
                    CreoSdkLog.Trace("solid", "listdatumplanes.not_plane",
                        new { model = Identity.Name, featureId = feat.Id, surfaceId = surf.Id, srfType });
                    continue;
                }
                result.Add(surf);
            }
        }
        return result;
    }

    // ---- TestMeasure 几何评估 (ProGeomitem*Eval 直绑) ----

    /// <summary>测两个几何项之间的角度(度);跨模型/NotFound/类型不匹配返 null。
    /// 对应 TestMeasure.c USER_ANGLE_EVAL 切片(2 个 edge/线性几何项)。</summary>
    public double? EvaluateAngle(ICreoModelItem item1, ICreoModelItem item2)
    {
        ThrowUtil.IfNull(item1);
        ThrowUtil.IfNull(item2);
        var a = item1 as CreoModelItem ?? throw new ArgumentException("item1 不是本 SDK 产生的 ModelItem。", nameof(item1));
        var b = item2 as CreoModelItem ?? throw new ArgumentException("item2 不是本 SDK 产生的 ModelItem。", nameof(item2));
        a.EnsureBelongsTo(_session);
        b.EnsureBelongsTo(_session);
        return _session.Run(n => n.GeomitemAngleEval(a.Ref, b.Ref));
    }

    /// <summary>测两个几何项之间的距离(模型长度单位);跨模型/NotFound 返 null。
    /// 对应 TestMeasure.c USER_DISTANCE_EVAL 切片(surface/point/axis 任意 2 个)。</summary>
    public double? EvaluateDistance(ICreoModelItem item1, ICreoModelItem item2)
    {
        ThrowUtil.IfNull(item1);
        ThrowUtil.IfNull(item2);
        var a = item1 as CreoModelItem ?? throw new ArgumentException("item1 不是本 SDK 产生的 ModelItem。", nameof(item1));
        var b = item2 as CreoModelItem ?? throw new ArgumentException("item2 不是本 SDK 产生的 ModelItem。", nameof(item2));
        a.EnsureBelongsTo(_session);
        b.EnsureBelongsTo(_session);
        return _session.Run(n => n.GeomitemDistanceEval(a.Ref, b.Ref));
    }

    /// <summary>测单个 surface 直径(仅圆柱/圆锥面有效);NotFound 返 null。
    /// 对应 TestMeasure.c USER_DIAMETER_EVAL 切片。</summary>
    public double? EvaluateDiameter(ICreoModelItem item)
    {
        ThrowUtil.IfNull(item);
        var a = item as CreoModelItem ?? throw new ArgumentException("item 不是本 SDK 产生的 ModelItem。", nameof(item));
        a.EnsureBelongsTo(_session);
        return _session.Run(n => n.GeomitemDiameterEval(a.Ref));
    }

    // ---- 主单位系统设置 ----

    /// <summary>设主单位系统(短形式)。默认 ignoreParamUnits=true 贴 PTC OTK/VB 语义。</summary>
    public void SetPrincipalUnitSystem(CreoUnitSystem newSystem, CreoUnitConversionMode conversionMode)
        => SetPrincipalUnitSystem(newSystem, conversionMode, ignoreParamUnits: true);

    /// <summary>设主单位系统(显式控参形式,动作型,触发再生)。
    /// <para><b>阻塞风险</b>: 转换导致再生失败时 Creo 弹 Fix Model UI,异步 exe 可能无限阻塞。</para></summary>
    public void SetPrincipalUnitSystem(
        CreoUnitSystem newSystem,
        CreoUnitConversionMode conversionMode,
        bool ignoreParamUnits)
    {
        CreoSdkLog.Run(
            "unitsystem", "principal-set",
            body: () => _session.Run(n => n.ModelPrincipalUnitSystemSet(
                Identity, newSystem, conversionMode, ignoreParamUnits)),
            failProps: () => new
            {
                name = Identity.Name,
                type = Identity.Type.ToString(),
                newSystem = newSystem.Name,
                mode = conversionMode.ToString(),
                ignoreParamUnits,
            });
    }
}
