using System.Runtime.InteropServices;
using CreoToolkit.Interop;
using CreoToolkit.Sdk.Errors;
using GNS = CreoToolkit.Interop.Generated;
using G = CreoToolkit.Interop.Generated.NativeMethods;

namespace CreoToolkit.Sdk.Native;

internal sealed partial class CreoNativeBridge
{
    // ---- Part 独有 API ----

    /// <summary>直绑(退役 Ctk_PartMaterialNamesList, 切 Generated):
    /// resolve + G.ProPartMaterialsGet(out IntPtr ProArray-of-ProName)
    /// + ProArraySizeGet + 每元素定长 wchar_t[32] 解码,经 ProArrayScope 配对释放。
    /// ProName = wchar_t[32]=64 bytes 定长。
    /// 仅 Part 类型有效:非 Part 由 Pro* 透传 PRO_TK_INVALID_TYPE。</summary>
    public IReadOnlyList<string> PartMaterialNames(ModelIdentity model)
    {
        if (model.Type != CreoModelType.Part) return Array.Empty<string>();
        if (!TryResolveMdl(model, out var mdl)) return Array.Empty<string>();
        var rc = G.ProPartMaterialsGet(mdl, out IntPtr namesArr);
        if (IsNotFound(rc) || namesArr == IntPtr.Zero) return Array.Empty<string>();
        using var scope = ProArrayScope.OwnedPlain(namesArr);
        Check.Eval(nameof(G.ProPartMaterialsGet), rc);
        // 严格 SizeGet 校验:失败直接抛(保原语义,不依赖 helper 的 fail-soft Count);
        // 已校验 count 直传读回,免二次 SizeGet
        Check.Eval(nameof(G.ProArraySizeGet), G.ProArraySizeGet(scope.Handle, out int count));
        const int proNameByteSize = 32 * 2; // wchar_t[32]
        return ProArrayMarshal.ReadFixedWStrings(scope.Handle, proNameByteSize, count);
    }

    /// <summary>ProMaterialCurrentGet: 直绑取当前材料名; 无材料返 null。</summary>
    public string? PartMaterialCurrent(ModelIdentity model)
    {
        // B3 直绑:ProMaterialCurrentGet 出参 pro_material,取 matl_name(ushort[])字段
        if (!TryResolveMdl(new ItemRef(model, CreoModelItemType.Unknown, 0), out var mdl)) return null;
        var mat = new GNS.pro_material();
        int st = (int)G.ProMaterialCurrentGet(mdl, ref mat);
        if (st != 0) return null;   // PRO_TK_E_NOT_FOUND 等视为无材料
        string matName = FromProNameUshort(mat.matl_name);
        return string.IsNullOrEmpty(matName) ? null : matName;
    }

    /// <summary>ProPartDensityGet: 直绑取密度; st&lt;0 返 null。</summary>
    public double? PartDensityGet(ModelIdentity model)
    {
        // B3 直绑:ProPartDensityGet
        if (!TryResolveMdl(new ItemRef(model, CreoModelItemType.Unknown, 0), out var mdl)) return null;
        int st = (int)G.ProPartDensityGet(mdl, out var density);
        if (st < 0) return null;
        Ensure(nameof(G.ProPartDensityGet), st);
        return density;
    }

    // ---- Solid.cs 专用 delegate（Cdecl，2 参数，供 ProSolidRelsetVisit callback）----
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate GNS.ProErrors ProRelsetAction(IntPtr relset, IntPtr data);

    // ---- Assembly 独有 API ----

    // Ctk_AssemblyTopLevelComponentsList 退役，
    // 走 G.ProSolidFeatVisit + G.ProFeatureTypeGet 过滤 PRO_FEAT_COMPONENT 直绑。
    // ProFeature ≡ pro_model_item { type:int, id:int, owner:IntPtr }；
    // 用 PtrToStructure 读 id，ProFeatureTypeGet 判断是否为组件特征。
    public IReadOnlyList<int> AssemblyTopLevelComponents(ModelIdentity model)
    {
        if (model.Type != CreoModelType.Assembly) return Array.Empty<int>();
        if (!TryResolveMdl(model, out var mdl)) return Array.Empty<int>();
        var ids = new List<int>();
        Exception? caught = null;

        ProVisitAction cb = (item, filterStatus, _) =>
        {
            if (caught != null) return GNS.ProErrors.PRO_TK_GENERAL_ERROR;
            try
            {
                var mi = Marshal.PtrToStructure<GNS.pro_model_item>(item);
                var featRc = G.ProFeatureTypeGet(ref mi, out int featType);
                // PRO_FEAT_COMPONENT = 1000 (ProFeatType.h:103, #define 宏 — 未产入 Generated)
                if (featRc == GNS.ProErrors.PRO_TK_NO_ERROR && featType == 1000)
                {
                    ids.Add(mi.id);
                }
            }
            catch (Exception ex) { caught = ex; return GNS.ProErrors.PRO_TK_GENERAL_ERROR; }
            return GNS.ProErrors.PRO_TK_NO_ERROR;
        };

        var ptr = _visitRegistry.Register(cb);
        try
        {
            var err = G.ProSolidFeatVisit(mdl, ptr, IntPtr.Zero, IntPtr.Zero);
            if (err != GNS.ProErrors.PRO_TK_NO_ERROR
                && err != GNS.ProErrors.PRO_TK_E_NOT_FOUND
                && err != GNS.ProErrors.PRO_TK_NOT_EXIST)
            {
                if (caught != null) throw caught;
                Check.Eval(nameof(G.ProSolidFeatVisit), err);
            }
        }
        finally { _visitRegistry.Unregister(cb); }

        if (caught != null) throw caught;
        return ids;
    }

    /// <summary>ProAsmSkeletonGet: 直绑取骨架模型名; 无骨架返 null。</summary>
    public string? AssemblySkeleton(ModelIdentity model)
    {
        // B3 直绑:ProAsmSkeletonGet 取骨架 ProMdl 句柄,再 ProMdlMdlnameGet 取名
        if (!TryResolveMdl(new ItemRef(model, CreoModelItemType.Unknown, 0), out var mdl)) return null;
        int st = (int)G.ProAsmSkeletonGet(mdl, out var skelMdl);
        if (st != 0) return null;   // PRO_TK_E_NOT_FOUND 等视为无骨架
        var nameBuf = new char[84];
        int stN = (int)G.ProMdlMdlnameGet(skelMdl, nameBuf);
        if (stN != 0) return null;
        int len = Array.IndexOf(nameBuf, '\0');
        return len < 0 ? new string(nameBuf) : new string(nameBuf, 0, len);
    }

    // ---- ProSolid 共享 API ----

    /// <summary>ProSolidRegenerationstatusGet: 直绑取实体再生状态; st&lt;0 返 null。</summary>
    public int? SolidRegenerationStatusGet(ModelIdentity model)
    {
        // B3 直绑:ProSolidRegenerationstatusGet
        if (!TryResolveMdl(new ItemRef(model, CreoModelItemType.Unknown, 0), out var mdl)) return null;
        int st = (int)G.ProSolidRegenerationstatusGet(mdl, out var status);
        if (st < 0) return null;
        Ensure(nameof(G.ProSolidRegenerationstatusGet), st);
        return (int)status;
    }

    /// <summary>直绑(退役 Ctk_SolidOutlineComputeWithOptions):
    /// resolve + G.ProSolidOutlineCompute(matrix=null,excludes=null/0 缺省坐标系 + 不排除)。
    /// generator 加 in-array of enum 识别后,Generated 的 excludes 签名修正为 [In] enum[]
    /// (而非 IntPtr),null 传入正确 marshal 为 NULL pointer。</summary>
    public CreoBoundingBox? SolidOutlineCompute(ModelIdentity model)
    {
        if (!TryResolveMdl(model, out var mdl)) return null;
        var outlinePts = new double[6];
        var rc = G.ProSolidOutlineCompute(mdl, null, null, 0, outlinePts);
        if (IsNotFound(rc)) return null;
        Check.Eval(nameof(G.ProSolidOutlineCompute), rc);
        return new CreoBoundingBox(outlinePts[0], outlinePts[1], outlinePts[2],
                                   outlinePts[3], outlinePts[4], outlinePts[5]);
    }

    /// <summary>ProSolidAccuracyGet: 直绑取精度; st&lt;0 返 null。</summary>
    public SolidAccuracyRecord? SolidAccuracyGet(ModelIdentity model)
    {
        // B3 直绑:ProSolidAccuracyGet
        if (!TryResolveMdl(new ItemRef(model, CreoModelItemType.Unknown, 0), out var mdl)) return null;
        int st = (int)G.ProSolidAccuracyGet(mdl, out var rType, out var rAccuracy);
        if (st < 0) return null;
        Ensure(nameof(G.ProSolidAccuracyGet), st);
        return new SolidAccuracyRecord((int)rType, rAccuracy);
    }

    /// <summary>直绑(退役 Ctk_SolidIsNoresolveMode):
    /// 静态 ProSolidRegenerationIsNoresolvemode(全局,无 mdl);model 参数仅保留 API 兼容性。</summary>
    public bool? SolidIsNoresolveMode(ModelIdentity model)
    {
        _ = model;
        var rc = G.ProSolidRegenerationIsNoresolvemode(out var v);
        if (IsNotFound(rc)) return null;
        Check.Eval(nameof(G.ProSolidRegenerationIsNoresolvemode), rc);
        return v != GNS.ProBooleans.PRO_B_FALSE;
    }

    /// <summary>直绑(退役 Ctk_SolidFailedfeaturesList):
    /// resolve + ProSolidFailedfeaturesList(取 failed_ids,co_failed_ids/co_x_failed_ids 不要)
    /// + ProArraySizeGet + Marshal.Copy 解码 int[],经 ProArrayScope 配对释放;NOT_FOUND → 空列表。</summary>
    public IReadOnlyList<int> SolidFailedFeatures(ModelIdentity model)
    {
        if (!TryResolveMdl(model, out var mdl)) return Array.Empty<int>();
        var rc = G.ProSolidFailedfeaturesList(mdl,
            out IntPtr failedArr, out IntPtr coFailedArr, out IntPtr coXFailedArr);
        // 三个 out 参数都可能被分配;只取 failed_ids,其余无脑 free。
        // using 逆序释放:声明序取释放序(failed → coFailed → coXFailed)的反向。
        using var coXFailedScope = ProArrayScope.OwnedPlain(coXFailedArr);
        using var coFailedScope = ProArrayScope.OwnedPlain(coFailedArr);
        using var failedScope = ProArrayScope.OwnedPlain(failedArr);
        if (IsNotFound(rc)) return Array.Empty<int>();
        Check.Eval(nameof(G.ProSolidFailedfeaturesList), rc);
        if (failedScope.Handle == IntPtr.Zero) return Array.Empty<int>();
        // 严格 SizeGet 校验:失败抛(保原语义)
        Check.Eval(nameof(G.ProArraySizeGet), G.ProArraySizeGet(failedScope.Handle, out var n));
        if (n <= 0) return Array.Empty<int>();
        var ids = new int[n];
        // int[] 直拷,与原 Marshal.Copy 语义一致
        Marshal.Copy(failedScope.Handle, ids, 0, n);
        return ids;
    }

    /// <summary>直绑(退役 Ctk_ModelToModelitem):
    /// resolve + ProMdlToModelitem(ref pro_model_item)。</summary>
    public ItemRef? ModelToModelItem(ModelIdentity model)
    {
        if (!TryResolveMdl(model, out var mdl)) return null;
        var mi = new GNS.pro_model_item();
        var rc = G.ProMdlToModelitem(mdl, ref mi);
        if (IsNotFound(rc)) return null;
        Check.Eval(nameof(G.ProMdlToModelitem), rc);
        var type = Enum.IsDefined(typeof(CreoModelItemType), (int)mi.type)
            ? (CreoModelItemType)(int)mi.type
            : CreoModelItemType.Unknown;
        return new ItemRef(model, type, mi.id);
    }

    // ---- 基类(直绑,退役 Ctk_ModelIsSkeleton/DependenciesCleanup) ----

    public bool? ModelIsSkeleton(ModelIdentity model)
    {
        if (!TryResolveMdl(model, out var mdl)) return null;
        var rc = G.ProMdlIsSkeleton(mdl, out var v);
        if (IsNotFound(rc)) return null;
        Check.Eval(nameof(G.ProMdlIsSkeleton), rc);
        return v != GNS.ProBooleans.PRO_B_FALSE;
    }

    public void ModelDependenciesCleanup(ModelIdentity model)
    {
        Check.Eval(nameof(G.ProMdlnameInit),
            G.ProMdlnameInit(ToProName(model.Name, 180),
                (GNS.ProMdlfileType)ToProMdlType(model.Type), out var mdl));
        Check.Eval(nameof(G.ProMdlDependenciesCleanup), G.ProMdlDependenciesCleanup(mdl));
    }

    // ---- ProSolid (直绑,退役 Ctk_SolidFamtableCheck/IsFaminstance) ----

    /// <summary>resolve + ProSolidFamtableCheck;返回值 = ProError 状态码(0=OK)。</summary>
    public int? SolidFamtableCheckStatus(ModelIdentity model)
    {
        if (!TryResolveMdl(model, out var mdl)) return null;
        return (int)G.ProSolidFamtableCheck(mdl);
    }

    /// <summary>resolve + ProFaminstanceGenericGet:有 generic → true,NOT_FOUND → false,真错抛。</summary>
    public bool? SolidIsFaminstance(ModelIdentity model)
    {
        if (!TryResolveMdl(model, out var mdl)) return null;
        var rc = G.ProFaminstanceGenericGet(mdl, 0 /* PRO_B_FALSE */, out _);
        if (rc == GNS.ProErrors.PRO_TK_NO_ERROR) return true;
        if (IsNotFound(rc)) return false;
        Check.Eval(nameof(G.ProFaminstanceGenericGet), rc);
        return null;
    }

    /// <summary>ProFamtableInit + ProFamtableInstanceVisit 遍历族表实例名;无族表返空。</summary>
    public IReadOnlyList<string> FamilyTableInstanceNames(ModelIdentity model)
    {
        if (!TryResolveMdl(model, out var mdl)) return Array.Empty<string>();
        var famtab = new GNS.pro_model_item();
        var rc = G.ProFamtableInit(mdl, ref famtab);
        if (IsNotFound(rc)) return Array.Empty<string>();
        Check.Eval(nameof(G.ProFamtableInit), rc);

        var names = new List<string>();
        Exception? caught = null;

        ProVisitAction cb = (item, filterStatus, _) =>
        {
            if (caught != null) return GNS.ProErrors.PRO_TK_GENERAL_ERROR;
            try
            {
                var inst = Marshal.PtrToStructure<GNS.profaminstance>(item);
                names.Add(FromProNameUshort(inst.name));
            }
            catch (Exception ex) { caught = ex; return GNS.ProErrors.PRO_TK_GENERAL_ERROR; }
            return GNS.ProErrors.PRO_TK_NO_ERROR;
        };

        var ptr = _visitRegistry.Register(cb);
        try
        {
            var err = G.ProFamtableInstanceVisit(ref famtab, ptr, IntPtr.Zero, IntPtr.Zero);
            if (err != GNS.ProErrors.PRO_TK_NO_ERROR
                && err != GNS.ProErrors.PRO_TK_E_NOT_FOUND
                && err != GNS.ProErrors.PRO_TK_NOT_EXIST)
            {
                if (caught != null) throw caught;
                Check.Eval(nameof(G.ProFamtableInstanceVisit), err);
            }
        }
        finally { _visitRegistry.Unregister(cb); }
        if (caught != null) throw caught;
        return names;
    }

    /// <summary>ProFaminstanceGenericGet + ProMdlMdlnameGet 取类属名;非实例返 null。</summary>
    public string? FamilyInstanceGenericName(ModelIdentity model)
    {
        if (!TryResolveMdl(model, out var mdl)) return null;
        var rc = G.ProFaminstanceGenericGet(mdl, (int)GNS.ProBooleans.PRO_B_FALSE, out var genMdl);
        if (IsNotFound(rc)) return null;
        Check.Eval(nameof(G.ProFaminstanceGenericGet), rc);
        if (genMdl == IntPtr.Zero) return null;
        var nameBuf = new char[180];
        var rcName = G.ProMdlMdlnameGet(genMdl, nameBuf);
        if (IsNotFound(rcName)) return null;
        Check.Eval(nameof(G.ProMdlMdlnameGet), rcName);
        return FromProName(nameBuf);
    }

    // ---- Assembly (直绑,退役 Ctk_Assembly*) ----

    public void AssemblyExplode(ModelIdentity model)
    {
        Check.Eval(nameof(G.ProMdlnameInit),
            G.ProMdlnameInit(ToProName(model.Name, 180),
                (GNS.ProMdlfileType)ToProMdlType(model.Type), out var mdl));
        Check.Eval(nameof(G.ProAssemblyExplode), G.ProAssemblyExplode(mdl));
    }

    public void AssemblyUnexplode(ModelIdentity model)
    {
        Check.Eval(nameof(G.ProMdlnameInit),
            G.ProMdlnameInit(ToProName(model.Name, 180),
                (GNS.ProMdlfileType)ToProMdlType(model.Type), out var mdl));
        Check.Eval(nameof(G.ProAssemblyUnexplode), G.ProAssemblyUnexplode(mdl));
    }

    public bool? AssemblyIsExploded(ModelIdentity model)
    {
        if (!TryResolveMdl(model, out var mdl)) return null;
        var rc = G.ProAssemblyIsExploded(mdl, out var v);
        if (IsNotFound(rc)) return null;
        Check.Eval(nameof(G.ProAssemblyIsExploded), rc);
        return v != GNS.ProBooleans.PRO_B_FALSE;
    }

    /// <summary>静态 ProSimprepMdlnameRetrieve:按装配名 + simpRepName 检索简化表示;
    /// 当前用 PRO_SIMPREP_USER_DEFINED 类型(SDK 接口未暴露 rep_type)。</summary>
    public void AssemblySimprepRetrieve(string assemName, CreoModelType fileType, string simpRepName)
    {
        Check.Eval(nameof(G.ProSimprepMdlnameRetrieve),
            G.ProSimprepMdlnameRetrieve(ToProName(assemName, 180),
                (GNS.ProMdlfileType)ToProMdlType(fileType),
                GNS.pro_simprep_types.PRO_SIMPREP_USER_DEFINED,
                ToProName(simpRepName, 32),
                out _));
    }

    // ---- P7 Solid Visitor bridge ----

    // Ctk_SolidAxisList 退役，走 G.ProSolidAxisVisit + ProAxisIdGet 直绑。
    public IReadOnlyList<ItemRef> SolidAxisList(ModelIdentity model)
    {
        if (!TryResolveMdl(model, out var mdl)) return Array.Empty<ItemRef>();
        return VisitCollect<ItemRef>(G.ProSolidAxisVisit, mdl, (item, _) =>
        {
            if ((int)G.ProAxisIdGet(item, out var id) != 0) return null;
            return new ItemRef(model, CreoModelItemType.Axis, id);
        });
    }

    // Ctk_SolidCsysList 退役，走 G.ProSolidCsysVisit + ProCsysIdGet 直绑。
    public IReadOnlyList<ItemRef> SolidCsysList(ModelIdentity model)
    {
        if (!TryResolveMdl(model, out var mdl)) return Array.Empty<ItemRef>();
        return VisitCollect<ItemRef>(G.ProSolidCsysVisit, mdl, (item, _) =>
        {
            if ((int)G.ProCsysIdGet(item, out var id) != 0) return null;
            return new ItemRef(model, CreoModelItemType.Csys, id);
        });
    }

    // Ctk_SolidSurfaceList 退役，走 G.ProSolidSurfaceVisit + ProSurfaceIdGet 直绑。
    public IReadOnlyList<ItemRef> SolidSurfaceList(ModelIdentity model)
    {
        if (!TryResolveMdl(model, out var mdl)) return Array.Empty<ItemRef>();
        return VisitCollect<ItemRef>(G.ProSolidSurfaceVisit, mdl, (item, _) =>
        {
            if ((int)G.ProSurfaceIdGet(item, out var id) != 0) return null;
            return new ItemRef(model, CreoModelItemType.Surface, id);
        });
    }

    // Ctk_SolidQuiltList 退役，走 G.ProSolidQuiltVisit + ProQuiltIdGet 直绑。
    public IReadOnlyList<ItemRef> SolidQuiltList(ModelIdentity model)
    {
        if (!TryResolveMdl(model, out var mdl)) return Array.Empty<ItemRef>();
        return VisitCollect<ItemRef>(G.ProSolidQuiltVisit, mdl, (item, _) =>
        {
            if ((int)G.ProQuiltIdGet(item, out var id) != 0) return null;
            return new ItemRef(model, CreoModelItemType.Quilt, id);
        });
    }

    // Ctk_SolidDimensionList 退役，走 G.ProSolidDimensionVisit 直绑。
    // refdim=PRO_B_TRUE 时含参考尺寸；ProDimension ≡ pro_model_item，PtrToStructure 读 type/id。
    // ProSolidDimensionVisit 5 参数(solid, refdim, action, filter, data)，用 lambda 包装适配 VisitCollect。
    public IReadOnlyList<ItemRef> SolidDimensionList(ModelIdentity model, bool includeRefDimensions)
    {
        if (!TryResolveMdl(model, out var mdl)) return Array.Empty<ItemRef>();
        var refdim = includeRefDimensions
            ? GNS.ProBooleans.PRO_B_TRUE
            : GNS.ProBooleans.PRO_B_FALSE;
        return VisitCollect<ItemRef>(
            (h, cb, filter, data) => G.ProSolidDimensionVisit(h, refdim, cb, filter, data),
            mdl,
            (item, _) =>
            {
                var mi = Marshal.PtrToStructure<GNS.pro_model_item>(item);
                return new ItemRef(model, (CreoModelItemType)(int)mi.type, mi.id);
            });
    }

    /// <summary>读取尺寸值；尺寸不存在或解析失败返 null。</summary>
    public double? DimensionValueGet(ItemRef dimension)
    {
        if (!TryResolveMdl(dimension.Model, out _)) return null;
        // owner 故意不设:ValueGet/TypeGet/TextGet 三函数 owner=0 正常工作,
        // 补设 owner 反而真机 crash——与 Delete/AttachpointsGet 的「owner 必设」相反,
        // ProDimension 族对 owner 的处理不统一,禁一刀切。
        var dim = new GNS.pro_model_item { type = (GNS.pro_obj_types)(int)dimension.Type, id = dimension.Id };
        var rc = G.ProDimensionValueGet(ref dim, out double value);
        if (IsNotFound(rc)) return null;
        Check.Eval(nameof(G.ProDimensionValueGet), rc);
        return value;
    }

    /// <summary>读取尺寸类型 int（pro_dimensiontype 枚举）；尺寸不存在返 null。</summary>
    public int? DimensionTypeGet(ItemRef dimension)
    {
        if (!TryResolveMdl(dimension.Model, out _)) return null;
        // owner 故意不设,见 DimensionValueGet 注释(9.10)
        var dim = new GNS.pro_model_item { type = (GNS.pro_obj_types)(int)dimension.Type, id = dimension.Id };
        var rc = G.ProDimensionTypeGet(ref dim, out var dimType);
        if (IsNotFound(rc)) return null;
        Check.Eval(nameof(G.ProDimensionTypeGet), rc);
        return (int)dimType;
    }

    /// <summary>读取尺寸显示文本；native wstring 数组经 ProWstringproarrayFree 递归释放。</summary>
    public string? DimensionTextGet(ItemRef dimension)
    {
        if (!TryResolveMdl(dimension.Model, out _)) return null;
        // owner 故意不设,见 DimensionValueGet 注释(9.10)
        var dim = new GNS.pro_model_item { type = (GNS.pro_obj_types)(int)dimension.Type, id = dimension.Id };
        var rc = G.ProDimensionTextWstringsGet(ref dim, out IntPtr pText);
        if (IsNotFound(rc) || pText == IntPtr.Zero) return null;
        Check.Eval(nameof(G.ProDimensionTextWstringsGet), rc);

        using var scope = ProArrayScope.OwnedCustom(pText, WstringProarrayFreeAction);
        var pointers = ProArrayMarshal.ReadPointers(scope.Handle);
        if (pointers.Length == 0) return string.Empty;

        var lines = new string[pointers.Length];
        for (int i = 0; i < pointers.Length; i++)
        {
            var wstrPtr = pointers[i];
            lines[i] = wstrPtr == IntPtr.Zero
                ? string.Empty
                // D7-EXEMPT: 元素归 ProWstringproarray 容器,由 ProWstringproarrayFree 递归释放。
                : Marshal.PtrToStringUni(wstrPtr) ?? string.Empty;
        }

        return lines.Length == 1 ? lines[0] : string.Join("\n", lines);
    }

    // Ctk_SolidRelsetList 退役，走 G.ProSolidRelsetVisit 直绑。
    // ProRelset ≡ pro_model_item，PtrToStructure 读 type/id。
    // ProSolidRelsetVisit callback 签名 (relset, data) 2 参数，无 filterStatus，不能用 VisitCollect；
    // 使用 ProRelsetAction delegate 手动注册。
    public IReadOnlyList<ItemRef> SolidRelsetList(ModelIdentity model)
    {
        if (!TryResolveMdl(model, out var mdl)) return Array.Empty<ItemRef>();
        var items = new List<ItemRef>();
        Exception? caught = null;

        ProRelsetAction cb = (relset, _) =>
        {
            if (caught != null) return GNS.ProErrors.PRO_TK_GENERAL_ERROR;
            try
            {
                var mi = Marshal.PtrToStructure<GNS.pro_model_item>(relset);
                items.Add(new ItemRef(model, (CreoModelItemType)(int)mi.type, mi.id));
            }
            catch (Exception ex) { caught = ex; return GNS.ProErrors.PRO_TK_GENERAL_ERROR; }
            return GNS.ProErrors.PRO_TK_NO_ERROR;
        };

        var ptr = _visitRegistry.Register(cb);
        try
        {
            var err = G.ProSolidRelsetVisit(mdl, ptr, IntPtr.Zero);
            if (err != GNS.ProErrors.PRO_TK_NO_ERROR
                && err != GNS.ProErrors.PRO_TK_E_NOT_FOUND
                && err != GNS.ProErrors.PRO_TK_NOT_EXIST)
            {
                if (caught != null) throw caught;
                Check.Eval(nameof(G.ProSolidRelsetVisit), err);
            }
        }
        finally { _visitRegistry.Unregister(cb); }

        if (caught != null) throw caught;
        return items;
    }
}
