using System.Runtime.InteropServices;
using CreoToolkit.Interop;
using CreoToolkit.Sdk.Errors;
using GNS = CreoToolkit.Interop.Generated;
using G = CreoToolkit.Interop.Generated.NativeMethods;

namespace CreoToolkit.Sdk.Native;

internal sealed partial class CreoNativeBridge
{
    // ---- 模型 (DATA / name+type 弱身份) ----

    /// <summary>直绑(退役 Ctk_ModelCurrent):
    /// ProMdlCurrentGet 取活动句柄(BAD_CONTEXT/NOT_FOUND 视为"无当前模型"→ null),
    /// ProMdlMdlnameGet 取名(180 wchar), ProMdlTypeGet 取类型, 拼成 ModelIdentity。
    /// 业务编码:无活动模型不是错误,返 null。</summary>
    public ModelIdentity? ModelCurrent()
    {
        var rc = G.ProMdlCurrentGet(out var mdl);
        if (rc == GNS.ProErrors.PRO_TK_BAD_CONTEXT || IsNotFound(rc)) return null;
        Check.Eval(nameof(G.ProMdlCurrentGet), rc);
        if (mdl == IntPtr.Zero) return null;

        var nameBuf = new char[180];
        Check.Eval(nameof(G.ProMdlMdlnameGet), G.ProMdlMdlnameGet(mdl, nameBuf));
        Check.Eval(nameof(G.ProMdlTypeGet), G.ProMdlTypeGet(mdl, out var ptype));
        return new ModelIdentity(FromProName(nameBuf), FromProMdlType((int)ptype));
    }

    /// <summary>直绑(退役 Ctk_ModelExists):
    /// ProMdlnameInit 纯会话内存查找,NOT_FOUND → false,其余成功即 true,真错抛。</summary>
    public bool ModelExists(string name, CreoModelType type)
    {
        var rc = G.ProMdlnameInit(ToProName(name, 180),
            (GNS.ProMdlfileType)ToProMdlType(type), out var mdl);
        if (IsNotFound(rc)) return false;
        Check.Eval(nameof(G.ProMdlnameInit), rc);
        return mdl != IntPtr.Zero;
    }

    /// <summary>ProMdlnameRetrieve: 会话优先,miss 后按 search_path 从磁盘加载;找不到返 null。</summary>
    public ModelIdentity? ModelRetrieveByName(string name, CreoModelType type)
    {
        var proType = (GNS.ProMdlfileType)ToProMdlType(type);
        var rc = G.ProMdlnameRetrieve(ToProName(name, 362), proType, out var mdl);
        if (IsNotFound(rc)) return null;
        Check.Eval(nameof(G.ProMdlnameRetrieve), rc);
        return mdl == IntPtr.Zero ? null : new ModelIdentity(name, type);
    }

    // ---- 模型动作（直绑 ProMdlnameInit + 单一 Pro 动作）----
    // ProMdl OHandle 跨 dispatch tick guaranteed valid；保留 Pro 错误透传，
    // 业务侧 Check.Eval 归一 query→null / action→throw。

    public void ModelRegenerate(ModelIdentity model)
    {
        Check.Eval(nameof(G.ProMdlnameInit),
            G.ProMdlnameInit(ToProName(model.Name, 180),
                (GNS.ProMdlfileType)ToProMdlType(model.Type), out var mdl));
        // 非 solid 类型由 ProSolidRegenerate 透传 PRO_TK_INVALID_TYPE(L1 原有 is_solid_type 校验等价)。
        Check.Eval(nameof(G.ProSolidRegenerate),
            G.ProSolidRegenerate(mdl, 0 /* PRO_REGEN_NO_FLAGS */));
    }

    public void ModelSave(ModelIdentity model)
    {
        Check.Eval(nameof(G.ProMdlnameInit),
            G.ProMdlnameInit(ToProName(model.Name, 180),
                (GNS.ProMdlfileType)ToProMdlType(model.Type), out var mdl));
        Check.Eval(nameof(G.ProMdlSave), G.ProMdlSave(mdl));
    }

    public void ModelDisplay(ModelIdentity model)
    {
        Check.Eval(nameof(G.ProMdlnameInit),
            G.ProMdlnameInit(ToProName(model.Name, 180),
                (GNS.ProMdlfileType)ToProMdlType(model.Type), out var mdl));
        Check.Eval(nameof(G.ProMdlDisplay), G.ProMdlDisplay(mdl));
    }

    /// <summary>直绑(退役 Ctk_ModelDependenciesList):
    /// resolve + G.ProMdlDependenciesDataList(deps + types 两个 ProArray)
    /// + Marshal 解码 ProMdlnameShortdata.name(ushort[180]),经 ProArrayScope×2 配对释放;
    /// NOT_FOUND/count=0 → 空列表。count 由 API 直出,不走 ProArraySizeGet。</summary>
    public IReadOnlyList<string> ModelDependenciesList(ModelIdentity model)
    {
        if (!TryResolveMdl(model, out var mdl)) return Array.Empty<string>();
        var rc = G.ProMdlDependenciesDataList(mdl,
            out IntPtr depsArr, out IntPtr typesArr, out int count);
        // using 逆序释放:声明序取释放序(deps → types)的反向。
        using var typesScope = ProArrayScope.OwnedPlain(typesArr);
        using var depsScope = ProArrayScope.OwnedPlain(depsArr);
        if (IsNotFound(rc) || count <= 0) return Array.Empty<string>();
        Check.Eval(nameof(G.ProMdlDependenciesDataList), rc);
        if (depsScope.Handle == IntPtr.Zero) return Array.Empty<string>();
        // 使用 API 直出的 count(非 SizeGet),循环解 struct 内嵌字段 name;
        // 手写保留:helper 泛型读回不提字段,此处只把 free 样板收敛到 scope。
        int sz = Marshal.SizeOf<GNS.ProMdlnameShortdata>();
        var result = new string[count];
        for (int i = 0; i < count; i++)
        {
            var rec = Marshal.PtrToStructure<GNS.ProMdlnameShortdata>(depsScope.Handle + i * sz);
            result[i] = FromProNameUshort(rec.name);
        }
        return result;
    }

    /// <summary>**走纯生成绑定** `ProMdlnameRetrieve` +
    /// `ProMdlModificationVerify`,不走手写 `Ctk_ModelIsModified`,首次证明路径在 production
    /// 业务函数(A 类 in-handle)跑通。
    /// handle 取/用/丢同一方法作用域(ProMdl OHandle 在 Creo 内存里 guaranteed valid;
    /// 非 ProMdl handle 不准抄此模式)。错误归一经 <see cref="Check.Eval(string,
    /// GNS.ProErrors, string?)"/> 复用(数值镜像)。
    /// L1 铁律:ProMdl 由 Creo session 内部管,toolkit 调用方无 free 责任。</summary>
    public bool ModelIsModified(ModelIdentity model)
    {
        // 纯生成绑定,无手写 Ctk_*。
        // 校正:Init 而非 Retrieve——业务"读 modified 状态"假定模型已在会话,
        // 不该有读盘副作用(详见主文件 TryResolveMdl 注释)。
        var initErr = G.ProMdlnameInit(ToProName(model.Name, 180),
            (GNS.ProMdlfileType)ToProMdlType(model.Type),
            out var handle);
        Check.Eval(nameof(G.ProMdlnameInit), initErr);

        var verifyErr = G.ProMdlModificationVerify(handle, out var modBool);
        Check.Eval(nameof(G.ProMdlModificationVerify), verifyErr);
        return modBool != GNS.ProBooleans.PRO_B_FALSE;
    }

    /// <summary>直绑(退役 Ctk_ModelErase):
    /// ProMdlnameInit → 不在会话(NOT_FOUND) → false, 否则 ProMdlErase 透传真错。</summary>
    public bool ModelErase(ModelIdentity model)
    {
        var initRc = G.ProMdlnameInit(ToProName(model.Name, 180),
            (GNS.ProMdlfileType)ToProMdlType(model.Type), out var mdl);
        if (IsNotFound(initRc)) return false;
        Check.Eval(nameof(G.ProMdlnameInit), initRc);
        Check.Eval(nameof(G.ProMdlErase), G.ProMdlErase(mdl));
        return true;
    }

    /// <summary>直绑(退役 Ctk_ModelCopy):
    /// ProMdlnameInit → 源不在会话 → false, 否则 ProMdlnameCopy(新名 wchar[180])。
    /// 新句柄是会话句柄(BORROWED),不 free;名拷入 char[180](ProMdlName 容量)。</summary>
    public bool ModelCopy(ModelIdentity model, string newName)
    {
        var initRc = G.ProMdlnameInit(ToProName(model.Name, 180),
            (GNS.ProMdlfileType)ToProMdlType(model.Type), out var mdl);
        if (IsNotFound(initRc)) return false;
        Check.Eval(nameof(G.ProMdlnameInit), initRc);

        var destBuf = ToProName(newName, 180);
        Check.Eval(nameof(G.ProMdlnameCopy), G.ProMdlnameCopy(mdl, destBuf, out _));
        return true;
    }

    /// <summary>直绑(退役 Ctk_ModelOrigin):
    /// ProMdlnameInit + ProMdlOriginGet(ProPath = char[260] 栈数组,无堆分配);
    /// 无磁盘来源 NOT_FOUND → null,L1 铁律无需 free(栈数组 caller 预分配)。</summary>
    public string? ModelOrigin(ModelIdentity model)
    {
        if (!TryResolveMdl(model, out var mdl)) return null;
        var pathBuf = new char[260];
        var rc = G.ProMdlOriginGet(mdl, pathBuf);
        if (IsNotFound(rc)) return null;
        Check.Eval(nameof(G.ProMdlOriginGet), rc);
        return FromProName(pathBuf);
    }

    /// <summary>走纯生成绑定
    /// <c>ProMdlnameInit</c> + <c>ProMdlCommonnameGet</c> + <c>ProWstringFree</c>,
    /// 不走手写 <c>Ctk_ModelCommonName</c>(已删)。
    /// model 不存在 / 无 common name 都返回 null(保持 query→null 契约,与原 native is_not_found 一刀切对齐)。
    /// L1 铁律:wstring 由 Creo 分配, 经 generated <c>ProWstringFree(IntPtr)</c> 释放; ProMdl OHandle 无 free 责任。
    /// 校正:Init 而非 Retrieve(详见主文件 TryResolveMdl 注释)。</summary>
    public string? ModelCommonName(ModelIdentity model)
    {
        var initErr = G.ProMdlnameInit(ToProName(model.Name, 180),
            (GNS.ProMdlfileType)ToProMdlType(model.Type),
            out var handle);
        if (IsNotFound(initErr)) return null;
        Check.Eval(nameof(G.ProMdlnameInit), initErr);

        var getErr = G.ProMdlCommonnameGet(
            handle, out var commonNamePtr, out var isModifiable);
        if (IsNotFound(getErr)) return null;
        Check.Eval(nameof(G.ProMdlCommonnameGet), getErr);
        _ = isModifiable;  // 当前未用,保留参数完整性

        if (commonNamePtr == IntPtr.Zero) return null;
        try { return Marshal.PtrToStringUni(commonNamePtr); }
        finally { G.ProWstringFree(commonNamePtr); }
    }

    /// <summary>直绑(退役 Ctk_ModelOutlineGet):
    /// resolve + ProSolidOutlineGet。非 solid 类型由 Pro* 透传 PRO_TK_INVALID_TYPE。</summary>
    public CreoBoundingBox ModelOutlineGet(ModelIdentity model)
    {
        Check.Eval(nameof(G.ProMdlnameInit),
            G.ProMdlnameInit(ToProName(model.Name, 180),
                (GNS.ProMdlfileType)ToProMdlType(model.Type), out var mdl));
        var outline = new double[6];   // Pro3dPnt[2] = double[6]
        Check.Eval(nameof(G.ProSolidOutlineGet), G.ProSolidOutlineGet(mdl, outline));
        return new CreoBoundingBox(outline[0], outline[1], outline[2],
                                   outline[3], outline[4], outline[5]);
    }

    /// <summary>直绑(退役 Ctk_ModelMassPropertyGet):
    /// resolve + ProSolidMassPropertyGet(csys=空,等价 native 传 NULL 取默认坐标系)。
    /// 非 solid 类型由 Pro* 透传 PRO_TK_INVALID_TYPE。</summary>
    public CreoMassProperty ModelMassPropertyGet(ModelIdentity model)
        => ModelMassPropertyGet(model, string.Empty);

    /// <summary>直绑 ProSolidMassPropertyGet，按指定坐标系名取质量属性。</summary>
    public CreoMassProperty ModelMassPropertyGet(ModelIdentity model, string coordinateSystemName)
    {
        ThrowUtil.IfNull(coordinateSystemName);
        Check.Eval(nameof(G.ProMdlnameInit),
            G.ProMdlnameInit(ToProName(model.Name, 180),
                (GNS.ProMdlfileType)ToProMdlType(model.Type), out var mdl));
        var mp = default(GNS.pro_mass_property);
        Check.Eval(nameof(G.ProSolidMassPropertyGet),
            G.ProSolidMassPropertyGet(mdl, ToProName(coordinateSystemName, 32), ref mp));
        return new CreoMassProperty(mp.volume, mp.surface_area, mp.density, mp.mass,
            mp.center_of_gravity[0], mp.center_of_gravity[1], mp.center_of_gravity[2]);
    }

    // Ctk_LayerNamesList 退役，走 G.ProMdlLayerVisit + ProModelitemNameGet 直绑。
    // callback 内调 ProModelitemNameGet 取 layer 名(wchar_t[32])，失败时跳过该层。
    public IReadOnlyList<string> LayerNamesList(ModelIdentity model)
    {
        if (!TryResolveMdl(model, out var mdl)) return Array.Empty<string>();
        var names = new List<string>();
        Exception? caught = null;

        ProVisitAction cb = (item, filterStatus, _) =>
        {
            if (caught != null) return GNS.ProErrors.PRO_TK_GENERAL_ERROR;
            try
            {
                var mi = Marshal.PtrToStructure<GNS.pro_model_item>(item);
                var nameBuf = new char[32];
                var rc = G.ProModelitemNameGet(ref mi, nameBuf);
                if (rc == GNS.ProErrors.PRO_TK_NO_ERROR)
                {
                    var s = FromProName(nameBuf);
                    if (!string.IsNullOrEmpty(s)) names.Add(s);
                }
            }
            catch (Exception ex) { caught = ex; return GNS.ProErrors.PRO_TK_GENERAL_ERROR; }
            return GNS.ProErrors.PRO_TK_NO_ERROR;
        };

        var ptr = _visitRegistry.Register(cb);
        try
        {
            var err = G.ProMdlLayerVisit(mdl, ptr, IntPtr.Zero, IntPtr.Zero);
            if (err != GNS.ProErrors.PRO_TK_NO_ERROR
                && err != GNS.ProErrors.PRO_TK_E_NOT_FOUND
                && err != GNS.ProErrors.PRO_TK_NOT_EXIST)
            {
                if (caught != null) throw caught;
                Check.Eval(nameof(G.ProMdlLayerVisit), err);
            }
        }
        finally { _visitRegistry.Unregister(cb); }

        if (caught != null) throw caught;
        return names;
    }

    /// <summary>直绑(退役 Ctk_LayerCreate):
    /// resolve + ProMdlLayerGet 探测 + ProLayerCreate;幂等(已存在 → false)。</summary>
    public bool LayerCreate(ModelIdentity model, string name)
    {
        if (!TryResolveMdl(model, out var mdl))
            throw new InvalidOperationException($"模型 {model.Name} 无法解析");
        var nameBuf = ToProName(name, 32);
        var layer = new GNS.pro_model_item();
        var getRc = G.ProMdlLayerGet(mdl, nameBuf, ref layer);
        if (getRc == GNS.ProErrors.PRO_TK_NO_ERROR) return false; // 已存在 → 幂等
        if (!IsNotFound(getRc)) Check.Eval(nameof(G.ProMdlLayerGet), getRc);
        Check.Eval(nameof(G.ProLayerCreate), G.ProLayerCreate(mdl, nameBuf, ref layer));
        return true;
    }

    /// <summary>直绑(退役 Ctk_LayerDelete):
    /// resolve + ProMdlLayerGet 探测 + ProLayerDelete;幂等(不存在 → false)。</summary>
    public bool LayerDelete(ModelIdentity model, string name)
    {
        if (!TryResolveMdl(model, out var mdl))
            throw new InvalidOperationException($"模型 {model.Name} 无法解析");
        var nameBuf = ToProName(name, 32);
        var layer = new GNS.pro_model_item();
        var getRc = G.ProMdlLayerGet(mdl, nameBuf, ref layer);
        if (IsNotFound(getRc)) return false; // 不存在 → 幂等
        Check.Eval(nameof(G.ProMdlLayerGet), getRc);
        Check.Eval(nameof(G.ProLayerDelete), G.ProLayerDelete(ref layer));
        return true;
    }

    public CreoLayerDisplayStatus? LayerDisplayStatusGet(ModelIdentity model, string name)
    {
        // B3 直绑:ProMdlLayerGet 由名取层句柄,再 ProLayerDisplaystatusGet
        if (!TryResolveMdl(new ItemRef(model, CreoModelItemType.Unknown, 0), out var mdl)) return null;
        var layer = new GNS.pro_model_item();
        int stL = (int)G.ProMdlLayerGet(mdl, ToProName(name), ref layer);
        if (stL != 0) return null;   // 层不存在
        int st = (int)G.ProLayerDisplaystatusGet(ref layer, out var status);
        if (st < 0) return null;
        Ensure(nameof(G.ProLayerDisplaystatusGet), st);
        return ToLayerDisplayStatus((int)status);
    }

    public bool LayerDisplayStatusSet(ModelIdentity model, string name, CreoLayerDisplayStatus status)
    {
        // B3 直绑:ProMdlLayerGet 由名取层句柄,再 ProLayerDisplaystatusSet
        if (!TryResolveMdl(new ItemRef(model, CreoModelItemType.Unknown, 0), out var mdl))
            throw new InvalidOperationException($"模型 {model.Name} 无法解析");
        var layer = new GNS.pro_model_item();
        int stL = (int)G.ProMdlLayerGet(mdl, ToProName(name), ref layer);
        if (stL != 0) return false;   // 层不存在
        Ensure(nameof(G.ProLayerDisplaystatusSet), (int)G.ProLayerDisplaystatusSet(ref layer, (GNS.ProLayerDisplay)(int)status));
        return true;
    }

    /// <summary>直绑(退役 Ctk_LayerFeatureAdd):
    /// resolve + ProMdlLayerGet 探测 + contains 探测(ProLayerItemsPopulate,ProArrayScope 释放)
    /// + ProLayerItemInit + ProLayerItemAdd;幂等(已包含 → false)。</summary>
    public bool LayerFeatureAdd(ModelIdentity model, string name, ItemRef feature)
    {
        if (!TryResolveMdl(model, out var mdl))
            throw new InvalidOperationException($"模型 {model.Name} 无法解析");
        var layer = new GNS.pro_model_item();
        var getRc = G.ProMdlLayerGet(mdl, ToProName(name, 32), ref layer);
        if (IsNotFound(getRc)) return false;        // 层不存在 → 幂等
        Check.Eval(nameof(G.ProMdlLayerGet), getRc);

        if (LayerContainsItemImpl(ref layer, (int)GNS.ProLayerType.PRO_LAYER_FEAT, feature.Id))
            return false;                            // 已包含 → 幂等

        var item = new GNS.ProLayerItem();
        Check.Eval(nameof(G.ProLayerItemInit),
            G.ProLayerItemInit(GNS.ProLayerType.PRO_LAYER_FEAT, feature.Id, mdl, ref item));
        Check.Eval(nameof(G.ProLayerItemAdd), G.ProLayerItemAdd(ref layer, ref item));
        return true;
    }

    /// <summary>直绑(退役 Ctk_LayerFeatureContains):
    /// resolve + ProMdlLayerGet 探测 + ProLayerItemsPopulate ProArray 解码;层不存在 → false。</summary>
    public bool LayerFeatureContains(ModelIdentity model, string name, ItemRef feature)
    {
        if (!TryResolveMdl(model, out var mdl)) return false;
        var layer = new GNS.pro_model_item();
        var getRc = G.ProMdlLayerGet(mdl, ToProName(name, 32), ref layer);
        if (IsNotFound(getRc)) return false;
        Check.Eval(nameof(G.ProMdlLayerGet), getRc);
        return LayerContainsItemImpl(ref layer, (int)GNS.ProLayerType.PRO_LAYER_FEAT, feature.Id);
    }

    /// <summary>ProArray-of-ProLayerItem 解码 helper:Populate 一次,经 ProArrayScope 配对释放。
    /// count 由 API 直出(非 SizeGet)。</summary>
    private static bool LayerContainsItemImpl(ref GNS.pro_model_item layer, int itemType, int itemId)
    {
        var popRc = G.ProLayerItemsPopulate(ref layer, out IntPtr items, out int count);
        if (IsNotFound(popRc)) return false;
        Check.Eval(nameof(G.ProLayerItemsPopulate), popRc);
        using var scope = ProArrayScope.OwnedPlain(items);
        // 使用 API 直出 count(不查 SizeGet);逐元素判 type+id 命中,ReadStructs 需再走 SizeGet,
        // 且早退不需要读完整数组,保留手写循环。
        int sz = Marshal.SizeOf<GNS.ProLayerItem>();
        for (int i = 0; i < count; i++)
        {
            var li = Marshal.PtrToStructure<GNS.ProLayerItem>(scope.Handle + i * sz);
            if ((int)li.type == itemType && li.id == itemId) return true;
        }
        return false;
    }

    // ---- 模型标识 API (DATA) ----

    /// <summary>ProMdlMdlnameGet: 读取模型内部名(直绑栈数组)。无法解析/失败返 null。</summary>
    public string? ModelMdlnameGet(ModelIdentity model)
    {
        if (!TryResolveMdl(model, out var mdl)) return null;
        var buf = new char[180];
        int st = (int)G.ProMdlMdlnameGet(mdl, buf);
        if (st < 0) return null;
        Ensure(nameof(G.ProMdlMdlnameGet), st);
        return FromProName(buf);
    }

    /// <summary>ProMdlDisplaynameGet: 读取模型显示名(直绑,include_ext=PRO_B_TRUE)。无法解析/失败返 null。</summary>
    public string? ModelDisplaynameGet(ModelIdentity model)
    {
        if (!TryResolveMdl(model, out var mdl)) return null;
        var buf = new char[216];
        int st = (int)G.ProMdlDisplaynameGet(mdl, GNS.ProBooleans.PRO_B_TRUE, buf);
        if (st < 0) return null;
        Ensure(nameof(G.ProMdlDisplaynameGet), st);
        return FromProName(buf);
    }

    /// <summary>ProMdlDirectoryPathGet: 读取模型目录路径(直绑栈数组)。无法解析/失败返 null。</summary>
    public string? ModelDirectoryPathGet(ModelIdentity model)
    {
        if (!TryResolveMdl(model, out var mdl)) return null;
        var buf = new char[260];
        int st = (int)G.ProMdlDirectoryPathGet(mdl, buf);
        if (st < 0) return null;
        Ensure(nameof(G.ProMdlDirectoryPathGet), st);
        return FromProName(buf);
    }

    /// <summary>ProMdlSubtypeGet: 读取模型 subtype(直绑)。无法解析/失败返 null。</summary>
    public int? ModelSubtypeGet(ModelIdentity model)
    {
        if (!TryResolveMdl(model, out var mdl)) return null;
        int st = (int)G.ProMdlSubtypeGet(mdl, out var sub);
        if (st < 0) return null;
        Ensure(nameof(G.ProMdlSubtypeGet), st);
        return (int)sub;
    }

    /// <summary>ProMdlFiletypeGet: 读取模型文件类型枚举(直绑)。无法解析/失败返 null。</summary>
    public int? ModelFiletypeGet(ModelIdentity model)
    {
        if (!TryResolveMdl(model, out var mdl)) return null;
        int st = (int)G.ProMdlFiletypeGet(mdl, out var ft);
        if (st < 0) return null;
        Ensure(nameof(G.ProMdlFiletypeGet), st);
        return (int)ft;
    }

    /// <summary>ProMdlIdGet: 读取模型数字 id(直绑)。无法解析/失败返 null。</summary>
    public int? ModelIdGet(ModelIdentity model)
    {
        if (!TryResolveMdl(model, out var mdl)) return null;
        int st = (int)G.ProMdlIdGet(mdl, out var id);
        if (st < 0) return null;
        Ensure(nameof(G.ProMdlIdGet), st);
        return id;
    }

    /// <summary>直绑:
    /// ProMdlnameInit + ProMdlVerstampGet → ProWVerstamp(Creo 分配) → ProVerstampStringGet → ASCII char*
    /// → Marshal.PtrToStringAnsi → 双层 try/finally 保 ProVerstampFree+ProVerstampStringFree。
    /// ProVerstampStringGet/Free 是 char**(ASCII), generator 未识别此子类,手添 P/Invoke 在 GeneratedHelpers.cs。</summary>
    public string? ModelVerstampGet(ModelIdentity model)
    {
        if (!TryResolveMdl(model, out var mdl)) return null;
        var stampRc = G.ProMdlVerstampGet(mdl, out IntPtr stamp);
        if (IsNotFound(stampRc)) return null;
        Check.Eval(nameof(G.ProMdlVerstampGet), stampRc);

        try
        {
            var strRc = G.ProVerstampStringGet(stamp, out IntPtr asciiPtr);
            if (IsNotFound(strRc)) return null;
            Check.Eval(nameof(G.ProVerstampStringGet), strRc);
            try { return asciiPtr == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(asciiPtr); }
            finally { G.ProVerstampStringFree(ref asciiPtr); }
        }
        finally { G.ProVerstampFree(ref stamp); }
    }

    /// <summary>ProMdlObjectdefaultnameGet: 按 objectType 查默认名(直绑,无 mdl 解析)。失败返 null。</summary>
    public string? ObjectDefaultNameGet(CreoModelItemType objectType)
    {
        var buf = new char[81];
        int st = (int)G.ProMdlObjectdefaultnameGet((GNS.pro_obj_types)(int)objectType, buf);
        if (st < 0) return null;
        Ensure(nameof(G.ProMdlObjectdefaultnameGet), st);
        return FromProName(buf);
    }

    // ---- 状态查询 + 生命周期 ----

    // ---- 状态查询(直绑,退役 Ctk_ModelIs*/Lock*/Backup/Rename/Delete/Erase*/WindowGet) ----
    // 论据:原 L1 wrapper 均"resolve+一次 Pro 调用",无业务编码;Generated P/Invoke 全覆盖;
    // 沿用 契约:query 找不到模型 → null,动作型 → 错误透传抛。

    public bool? ModelIsModifiable(ModelIdentity model)
    {
        if (!TryResolveMdl(model, out var mdl)) return null;
        var rc = G.ProMdlIsModifiable(mdl, GNS.ProBooleans.PRO_B_FALSE, out var v);
        if (IsNotFound(rc)) return null;
        Check.Eval(nameof(G.ProMdlIsModifiable), rc);
        return v != GNS.ProBooleans.PRO_B_FALSE;
    }

    public bool? ModelIsSaveAllowed(ModelIdentity model)
    {
        if (!TryResolveMdl(model, out var mdl)) return null;
        var rc = G.ProMdlIsSaveAllowed(mdl, GNS.ProBooleans.PRO_B_FALSE, out var v);
        if (IsNotFound(rc)) return null;
        Check.Eval(nameof(G.ProMdlIsSaveAllowed), rc);
        return v != GNS.ProBooleans.PRO_B_FALSE;
    }

    public bool? ModelLocationIsStandard(ModelIdentity model)
    {
        if (!TryResolveMdl(model, out var mdl)) return null;
        var rc = G.ProMdlLocationIsStandard(mdl, out var v);
        if (IsNotFound(rc)) return null;
        Check.Eval(nameof(G.ProMdlLocationIsStandard), rc);
        return v != GNS.ProBooleans.PRO_B_FALSE;
    }

    public bool? ModelLockGet(ModelIdentity model)
    {
        if (!TryResolveMdl(model, out var mdl)) return null;
        var rc = G.ProMdlLockGet(mdl, out var v);
        if (IsNotFound(rc)) return null;
        Check.Eval(nameof(G.ProMdlLockGet), rc);
        return v != GNS.ProBooleans.PRO_B_FALSE;
    }

    public void ModelLockSet(ModelIdentity model, bool locked)
    {
        Check.Eval(nameof(G.ProMdlnameInit),
            G.ProMdlnameInit(ToProName(model.Name, 180),
                (GNS.ProMdlfileType)ToProMdlType(model.Type), out var mdl));
        Check.Eval(nameof(G.ProMdlLockSet),
            G.ProMdlLockSet(mdl, locked ? GNS.ProBooleans.PRO_B_TRUE : GNS.ProBooleans.PRO_B_FALSE));
    }

    public void ModelBackup(ModelIdentity model, string targetDirectory)
    {
        Check.Eval(nameof(G.ProMdlnameInit),
            G.ProMdlnameInit(ToProName(model.Name, 180),
                (GNS.ProMdlfileType)ToProMdlType(model.Type), out var mdl));
        Check.Eval(nameof(G.ProMdlnameBackup), G.ProMdlnameBackup(mdl, ToProName(targetDirectory, 260)));
    }

    /// <summary>ProMdlnameRename + 可选 ProMdlSave。</summary>
    public void ModelRename(ModelIdentity model, string newName, bool saveAfter)
    {
        Check.Eval(nameof(G.ProMdlnameInit),
            G.ProMdlnameInit(ToProName(model.Name, 180),
                (GNS.ProMdlfileType)ToProMdlType(model.Type), out var mdl));
        Check.Eval(nameof(G.ProMdlnameRename), G.ProMdlnameRename(mdl, ToProName(newName, 180)));
        if (saveAfter)
            Check.Eval(nameof(G.ProMdlSave), G.ProMdlSave(mdl));
    }

    public void ModelDelete(ModelIdentity model)
    {
        Check.Eval(nameof(G.ProMdlnameInit),
            G.ProMdlnameInit(ToProName(model.Name, 180),
                (GNS.ProMdlfileType)ToProMdlType(model.Type), out var mdl));
        Check.Eval(nameof(G.ProMdlDelete), G.ProMdlDelete(mdl));
    }

    /// <summary>ProMdlEraseAll 按句柄擦除:取当前活动模型作为目标(对齐原 native 行为)。</summary>
    public void ModelEraseCurrentWithDependencies()
    {
        Check.Eval(nameof(G.ProMdlCurrentGet), G.ProMdlCurrentGet(out var mdl));
        if (mdl == IntPtr.Zero) return;
        Check.Eval(nameof(G.ProMdlEraseAll), G.ProMdlEraseAll(mdl));
    }

    public void ModelEraseNotDisplayed()
        => Check.Eval(nameof(G.ProMdlEraseNotDisplayed), G.ProMdlEraseNotDisplayed());

    /// <summary>ProMdlWindowGet 直绑:解析失败/无窗口返 null。</summary>
    public int? ModelWindowGet(ModelIdentity model)
    {
        if (!TryResolveMdl(model, out var mdl)) return null;
        var rc = G.ProMdlWindowGet(mdl, out var wid);
        if (IsNotFound(rc)) return null;
        Check.Eval(nameof(G.ProMdlWindowGet), rc);
        return wid;
    }

    /// <summary>直绑(退役 Ctk_ModelDataGet):多步编排在 C# 端复刻。
    /// (1) ProMdlnameInit → mdl handle;
    /// (2) ProMdlDataGet → pro_object_info 取 .name(ushort[80]) + .subclass;
    /// (3) ProFaminstanceGenericGet → 族表 generic mdl(可空,失败降级为空串);
    /// (4) 若 generic mdl 存在,ProMdlMdlnameGet → 取 generic 名(失败降级空串);
    /// pro_object_info 是 POD struct(generator 已产),无堆出参,栈拷出即脱钩。</summary>
    public ModelDataRecord? ModelDataGet(ModelIdentity model)
    {
        if (!TryResolveMdl(model, out var mdl)) return null;

        var info = default(GNS.pro_object_info);
        var dataRc = G.ProMdlDataGet(mdl, ref info);
        if (IsNotFound(dataRc)) return null;
        Check.Eval(nameof(G.ProMdlDataGet), dataRc);

        var fileName = FromProNameUshort(info.name);

        // 族表 generic 名:非族表实例或失败 → 空串降级(对齐原 native 行为)。
        string genericName = string.Empty;
        var genRc = G.ProFaminstanceGenericGet(mdl, 0 /* PRO_B_FALSE: 取顶层 generic */, out var genMdl);
        if (genRc == GNS.ProErrors.PRO_TK_NO_ERROR && genMdl != IntPtr.Zero)
        {
            var genBuf = new char[180];
            var nameRc = G.ProMdlMdlnameGet(genMdl, genBuf);
            if (nameRc == GNS.ProErrors.PRO_TK_NO_ERROR)
                genericName = FromProName(genBuf);
        }

        return new ModelDataRecord(fileName, genericName, FromProMdlType(info.subclass));
    }

    // Ctk_ModelGtolVisit 退役，走 G.ProMdlGtolVisit + PtrToStructure 直绑。
    // ProGtol ≡ pro_model_item { type:int, id:int, owner:IntPtr };
    // 用 Marshal.PtrToStructure<GNS.pro_model_item>(item) 读 type/id，绝不 hardcode offset。
    public IReadOnlyList<ItemRef> ModelGtolList(ModelIdentity model)
    {
        if (!TryResolveMdl(model, out var mdl)) return Array.Empty<ItemRef>();
        return VisitCollect<ItemRef>(G.ProMdlGtolVisit, mdl, (item, _) =>
        {
            var mi = Marshal.PtrToStructure<GNS.pro_model_item>(item);
            return new ItemRef(model, (CreoModelItemType)(int)mi.type, mi.id);
        });
    }

    // ============================================================================================
    // 模型相邻域:窗口 + Load/Init(合并自 CreoNativeBridge.Window.cs / CreoNativeBridge.LoadInit.cs,
    // 原 partial 仅 3+2 个方法,体量过薄,合并后维护成本更低)
    // ============================================================================================

    // ---- 配置项(直绑 ProConfigoptionGet + ProConfigoptSet) ----

    public string? ConfigOptionGet(string option)
    {
        var optBuf = ToProName(option, 32);
        var valBuf = new char[260];
        var rc = G.ProConfigoptionGet(optBuf, valBuf);
        if (IsNotFound(rc)) return null;
        Check.Eval(nameof(G.ProConfigoptionGet), rc);
        return FromProName(valBuf);
    }

    /// <summary>直绑 ProConfigoptArrayGet(多值项如 search_path):
    /// out ProArray-of-ProPath + ProArraySizeGet + 每元素定长 wchar_t[260] 解码,
    /// 经 ProArrayScope 配对释放。ProPath = wchar_t[260]=520 bytes 定长。NOT_FOUND/count=0 → 空列表。</summary>
    public IReadOnlyList<string> ConfigOptionArrayGet(string option)
    {
        var optBuf = ToProName(option, 32);
        var rc = G.ProConfigoptArrayGet(optBuf, out IntPtr valueArr);
        if (IsNotFound(rc)) return Array.Empty<string>();          // 选项未设置 → 良性空
        Check.Eval(nameof(G.ProConfigoptArrayGet), rc);             // 其他错误码 throw(此时无分配无需 free)
        if (valueArr == IntPtr.Zero) return Array.Empty<string>();  // 成功但空的防御
        using var scope = ProArrayScope.OwnedPlain(valueArr);
        // 严格 SizeGet 校验:失败抛(保原语义);已校验 count 直传读回,免二次 SizeGet
        Check.Eval(nameof(G.ProArraySizeGet), G.ProArraySizeGet(scope.Handle, out int count));
        const int proPathByteSize = 260 * 2; // wchar_t[260]
        return ProArrayMarshal.ReadFixedWStrings(scope.Handle, proPathByteSize, count);
    }

    public void ConfigOptionSet(string option, string value)
    {
        var optBuf = ToProName(option, 32);
        var valBuf = ToProName(value, 260);
        Check.Eval(nameof(G.ProConfigoptSet), G.ProConfigoptSet(optBuf, valBuf));
    }

    // ---- 窗口(直绑,退役 Ctk_Window*) ----
    // ProToolkit.h: #define PRO_VALUE_UNUSED (-1) — 当前无窗口/消息区 → null

    private const int ProValueUnused = -1;

    public int? WindowCurrentId()
    {
        Check.Eval(nameof(G.ProWindowCurrentGet), G.ProWindowCurrentGet(out var windowId));
        return windowId == ProValueUnused ? null : windowId;
    }

    public void WindowRepaint(int windowId)
        => Check.Eval(nameof(G.ProWindowRepaint), G.ProWindowRepaint(windowId));

    /// <summary>幂等重绘:BAD_INPUTS(无窗口) → false,真错抛。</summary>
    public bool WindowTryRepaint(int windowId)
    {
        var rc = G.ProWindowRepaint(windowId);
        if (rc == GNS.ProErrors.PRO_TK_BAD_INPUTS) return false;
        Check.Eval(nameof(G.ProWindowRepaint), rc);
        return true;
    }

    /// <summary>ProObjectwindowMdlnameCreate + ProWindowActivate:
    /// 为已在会话中的模型创建或取回窗口并激活,使 ProMdlCurrentGet 返回该模型。</summary>
    public int ModelWindowOpen(ModelIdentity model)
    {
        var nameBuf = ToProName(model.Name, 180);
        var objType = (GNS.pro_obj_types)ToProMdlType(model.Type);
        Check.Eval(nameof(G.ProObjectwindowMdlnameCreate),
            G.ProObjectwindowMdlnameCreate(nameBuf, objType, out var wid));
        Check.Eval(nameof(G.ProWindowActivate), G.ProWindowActivate(wid));
        return wid;
    }

    // ---- Load/Init(直绑,退役 Ctk_ModelLoad/Init) ----

    /// <summary>直绑(退役 Ctk_ModelLoad):
    /// 真"从磁盘按路径加载"——走 ProMdlFiletypeLoad(ProPath 全路径,可含目录+扩展名);
    /// 区别于 ProMdlnameRetrieve(仅按名字+search_path,不认全路径)。
    /// path 含扩展名时 type 传 PRO_MDLFILE_UNUSED;Unknown 亦回落 UNUSED(要求 path 带扩展名)。
    /// 取回的 ProMdl handle 再 ProMdlMdlnameGet + ProMdlTypeGet 拼 ModelIdentity。
    /// NOT_FOUND/路径无效 → null。</summary>
    public ModelIdentity? ModelLoad(string path, CreoModelType fileType, bool askUserAboutReps)
    {
        var mdlType = fileType == CreoModelType.Unknown
            ? GNS.ProMdlfileType.PRO_MDLFILE_UNUSED
            : (GNS.ProMdlfileType)ToProMdlType(fileType);
        var askReps = askUserAboutReps ? GNS.ProBooleans.PRO_B_TRUE : GNS.ProBooleans.PRO_B_FALSE;
        var rc = G.ProMdlFiletypeLoad(ToProName(path, 260), mdlType, askReps, out var mdl);
        if (IsNotFound(rc)) return null;
        Check.Eval(nameof(G.ProMdlFiletypeLoad), rc);
        if (mdl == IntPtr.Zero) return null;

        var nameBuf = new char[180];
        Check.Eval(nameof(G.ProMdlMdlnameGet), G.ProMdlMdlnameGet(mdl, nameBuf));
        Check.Eval(nameof(G.ProMdlTypeGet), G.ProMdlTypeGet(mdl, out var ptype));
        return new ModelIdentity(FromProName(nameBuf), FromProMdlType((int)ptype));
    }

    /// <summary>直绑(退役 Ctk_ModelInit):
    /// ProMdlnameInit 在会话内存初始化句柄(若模型不在会话,Pro* 返 NOT_FOUND,业务可显式 Retrieve)。</summary>
    public void ModelInit(string name, CreoModelType fileType)
        => Check.Eval(nameof(G.ProMdlnameInit),
            G.ProMdlnameInit(ToProName(name, 180),
                (GNS.ProMdlfileType)ToProMdlType(fileType), out _));
}
