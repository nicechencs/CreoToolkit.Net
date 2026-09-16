using System.Runtime.InteropServices;
using CreoToolkit.Interop;
using CreoToolkit.Sdk.Errors;
using CreoToolkit.Sdk.Units;
using GNS = CreoToolkit.Interop.Generated;
using G = CreoToolkit.Interop.Generated.NativeMethods;

namespace CreoToolkit.Sdk.Native;

internal sealed partial class CreoNativeBridge
{
    // ---- 参数 (DATA: copy-out-free-in 即时快照) ----
    // ParameterFind/List/Set 退役 Ctk_Parameter* wrapper，走 G.Pro* 直绑。

    // ---- union ↔ CreoParamValue 转换 helper ----

    // s_val union size: 81 ushort = 162 bytes
    private const int SValByteLen = 81 * 2;

    /// <summary>Pro_Param_Value → CreoParamValue?。VOID/NOT_SET/NOTE_ID 等未知类型 → null(query 语义)。</summary>
    private static CreoParamValue? FromProParamValue(in GNS.Pro_Param_Value pv)
    {
        return pv.type switch
        {
            GNS.param_value_types.PRO_PARAM_DOUBLE  => CreoParamValue.OfDouble(pv.value.d_val),
            GNS.param_value_types.PRO_PARAM_STRING  => CreoParamValue.OfString(ReadSVal(in pv)),
            GNS.param_value_types.PRO_PARAM_INTEGER => CreoParamValue.OfInt(pv.value.i_val),
            GNS.param_value_types.PRO_PARAM_BOOLEAN => CreoParamValue.OfBool(pv.value.i_val != 0),
            _                                       => null, // PRO_PARAM_VOID / PRO_PARAM_NOT_SET / NOTE_ID 等
        };
    }

    /// <summary>
    /// 从 Pro_Param_Value.value.s_val(fixed ushort[81])中读 wchar 串(NUL 终止)。
    /// s_val 与 d_val 共 offset=0(union)。通过 StructureToPtr 到非托管堆后，
    /// 用 Marshal.PtrToStringUni 安全读取，绕开 unsafe struct fixed 限制。
    /// </summary>
    private static string ReadSVal(in GNS.Pro_Param_Value pv)
    {
        // StructureToPtr 将整个 Pro_Param_Value 写到非托管堆
        int size = Marshal.SizeOf<GNS.Pro_Param_Value>();
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(pv, ptr, false);
            // type 字段偏移 = offsetof(Pro_Param_Value, type) = 0(int32)
            // value 字段偏移 = 4(或 8 含 padding)；用 offsetof 获取
            int valueOffset = (int)Marshal.OffsetOf<GNS.Pro_Param_Value>(nameof(GNS.Pro_Param_Value.value));
            // s_val 在 union 的 offset=0
            return Marshal.PtrToStringUni(ptr + valueOffset) ?? string.Empty;
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    /// <summary>CreoParamValue → Pro_Param_Value。
    /// s_val(fixed ushort[81]) 通过非托管堆写入后再 PtrToStructure 拷回，绕开 unsafe struct fixed 限制。</summary>
    private static GNS.Pro_Param_Value ToProParamValue(CreoParamValue v)
    {
        var pv = new GNS.Pro_Param_Value();
        switch (v.Kind)
        {
            case CreoParamValueKind.Double:
                pv.type = GNS.param_value_types.PRO_PARAM_DOUBLE;
                pv.value.d_val = v.AsDouble();
                break;

            case CreoParamValueKind.String:
                pv.type = GNS.param_value_types.PRO_PARAM_STRING;
                var s = v.AsString();
                if (s.Length >= CtkAbiConstants.ProLineSize)
                    throw new ArgumentException(
                        $"字符串参数值超长: {s.Length} 字符, 上限 {CtkAbiConstants.ProLineSize - 1}(含 NUL)。", nameof(v));
                // 写 s_val(union offset=0)：通过非托管堆中转，按 ushort 写 wchar，再拷回
                int pvSize = Marshal.SizeOf<GNS.Pro_Param_Value>();
                var pvPtr = Marshal.AllocHGlobal(pvSize);
                try
                {
                    Marshal.StructureToPtr(pv, pvPtr, false);
                    int valueOff = (int)Marshal.OffsetOf<GNS.Pro_Param_Value>(nameof(GNS.Pro_Param_Value.value));
                    var svalPtr = pvPtr + valueOff; // s_val offset=0 in union
                    for (int i = 0; i < s.Length; i++)
                        Marshal.WriteInt16(svalPtr, i * 2, s[i]);
                    Marshal.WriteInt16(svalPtr, s.Length * 2, 0); // NUL 终止
                    pv = Marshal.PtrToStructure<GNS.Pro_Param_Value>(pvPtr);
                }
                finally
                {
                    Marshal.FreeHGlobal(pvPtr);
                }
                break;

            case CreoParamValueKind.Int:
                pv.type = GNS.param_value_types.PRO_PARAM_INTEGER;
                pv.value.i_val = v.AsInt();
                break;

            case CreoParamValueKind.Bool:
                pv.type = GNS.param_value_types.PRO_PARAM_BOOLEAN;
                pv.value.i_val = v.AsBool() ? 1 : 0;
                break;

            default:
                throw new ArgumentException("不能写入 Unset 的 CreoParamValue。", nameof(v));
        }
        return pv;
    }

    // ---- 构造模型级 owner item helper（与 ParameterDelete/Reset 对齐）----

    private static GNS.pro_model_item MakeModelOwner(IntPtr mdl, CreoModelType type)
        => new GNS.pro_model_item
        {
            type  = (GNS.pro_obj_types)ToProMdlType(type),
            id    = 0,
            owner = mdl,
        };

    // ---- 直绑实现 ----

    /// <summary>直绑(退役 Ctk_ParameterFind):
    /// resolve → MakeModelOwner → ProParameterInit → if NOT_FOUND return null
    /// → ProParameterValueWithUnitsGet → FromProParamValue → new CreoParameter。</summary>
    public CreoParameter? ParameterFind(ModelIdentity model, string name)
    {
        if (!TryResolveMdl(model, out var mdl)) return null;
        var owner = MakeModelOwner(mdl, model.Type);
        var param = new GNS.proparameter();
        var initRc = G.ProParameterInit(ref owner, ToProName(name), ref param);
        if (IsNotFound(initRc)) return null;
        Check.Eval(nameof(G.ProParameterInit), initRc);

        var pv = new GNS.Pro_Param_Value();
        var discardedUnits = default(GNS.ProUnititem);
        Check.Eval(nameof(G.ProParameterValueWithUnitsGet),
            G.ProParameterValueWithUnitsGet(ref param, ref pv, ref discardedUnits));
        var value = FromProParamValue(in pv);
        if (value is null) return null; // VOID/NOT_SET 视为无值

        return new CreoParameter(name, value.Value, IsModified: false);
    }

    /// <summary>直绑(退役 Ctk_ParameterList):
    /// resolve → MakeModelOwner → VisitCollect&lt;CreoParameter&gt; + ProParameterVisit。
    /// callback 内从 proparameter.id(ushort[32]) 读名，ProParameterValueWithUnitsGet 取值。
    /// SDK 端做 prefix/kind 过滤；query.Matches 兜底全量过滤。</summary>
    public IReadOnlyList<CreoParameter> ParameterList(ModelIdentity model, ParameterQuery? query)
    {
        if (!TryResolveMdl(model, out var mdl)) return Array.Empty<CreoParameter>();
        var owner = MakeModelOwner(mdl, model.Type);
        var (prefix, kindFilter) = SplitQuery(query);

        // ProParameterVisit 真签名: (ref pro_model_item owner, IntPtr filter, IntPtr action, IntPtr data)
        // ⚠ VisitCollect 调 visitor 顺序: (handle, action_ptr, filter, data) — action 在第 2 位!
        // 与 ProSolidFeatVisit/ProSolidAxisVisit 等顺序一致(action,filter)
        // 但 ProParameterVisit 反过来(filter,action),lambda 必须把 visitor 第2参(action)
        // 映射到 ProParameterVisit 的 action 位(第3参),第3参(filter)映射到 filter 位。
        var results = VisitCollect<CreoParameter>(
            (_, action, filter, data) => G.ProParameterVisit(ref owner, filter, action, data),
            IntPtr.Zero,
            (item, _) =>
            {
                // item = ProParameter*(opaque 指针，Marshal 读 proparameter struct)
                var param = Marshal.PtrToStructure<GNS.proparameter>(item);

                // 从 proparameter.id(ushort[32]) 读参数名
                var paramName = FromProNameUshort(param.id);

                // prefix 过滤(下推)
                if (prefix != null
                    && !paramName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return (CreoParameter?)null;

                // 读值（需要 ref，拷一份避免 lambda 捕获 in 指针）
                var paramCopy = param;
                var pv = new GNS.Pro_Param_Value();
                var discardedUnits = default(GNS.ProUnititem);
                var rc = G.ProParameterValueWithUnitsGet(ref paramCopy, ref pv, ref discardedUnits);
                if (IsNotFound(rc)) return null;
                if (rc != GNS.ProErrors.PRO_TK_NO_ERROR) return null;

                // kind 过滤(下推)
                if (kindFilter >= 0)
                {
                    int pvKind = pv.type switch
                    {
                        GNS.param_value_types.PRO_PARAM_STRING  => CtkAbiConstants.ParamString,
                        GNS.param_value_types.PRO_PARAM_DOUBLE  => CtkAbiConstants.ParamDouble,
                        GNS.param_value_types.PRO_PARAM_INTEGER => CtkAbiConstants.ParamInt,
                        GNS.param_value_types.PRO_PARAM_BOOLEAN => CtkAbiConstants.ParamBool,
                        _                                       => -1,
                    };
                    if (pvKind != kindFilter) return null;
                }

                var value = FromProParamValue(in pv);
                if (value is null) return null;
                return new CreoParameter(paramName, value.Value, IsModified: false);
            });

        // query.Matches 兜底(含 prefix+kind+额外条件)
        if (query is { } q)
            return results.Where(p => q.Matches(p)).ToArray();
        return results;
    }

    /// <summary>直绑(退役 Ctk_ParameterList 分页路径):
    /// 走全量 ParameterList 后 Skip/Take；大模型场景可后续优化为 Visit 内计数跳过。</summary>
    public IReadOnlyList<CreoParameter> ParameterListPage(ModelIdentity model, int offset, int limit)
    {
        var all = ParameterList(model, null);
        return all.Skip(offset).Take(limit).ToArray();
    }

    /// <summary>直绑(退役 Ctk_ParameterSet):
    /// resolve → ProParameterInit
    /// → if NOT_FOUND: ProParameterWithUnitsCreate(upsert 新建)
    /// → else: ProParameterValueWithUnitsSet(更新)。保留 upsert 语义，与原 native 一致。</summary>
    public void ParameterSet(ModelIdentity model, string name, CreoParamValue value)
    {
        if (name.Length >= CtkAbiConstants.ProNameSize)
            throw new ArgumentException(
                $"参数名超长: {name.Length} 字符, 上限 {CtkAbiConstants.ProNameSize - 1}(含 NUL)。", nameof(name));

        if (!TryResolveMdl(model, out var mdl))
            throw new InvalidOperationException($"模型 {model.Name} 无法解析，无法设置参数。");

        var owner = MakeModelOwner(mdl, model.Type);
        var param = new GNS.proparameter();
        var initRc = G.ProParameterInit(ref owner, ToProName(name), ref param);

        var pv = ToProParamValue(value);
        if (IsNotFound(initRc))
        {
            // 参数不存在 → upsert 新建
            Check.Eval(nameof(G.ProParameterWithUnitsCreate),
                G.ProParameterWithUnitsCreate(ref owner, ToProName(name), ref pv, IntPtr.Zero, ref param));
        }
        else
        {
            Check.Eval(nameof(G.ProParameterInit), initRc);
            Check.Eval(nameof(G.ProParameterValueWithUnitsSet),
                G.ProParameterValueWithUnitsSet(ref param, ref pv, IntPtr.Zero));
        }
    }

    /// <summary>直绑(退役 Ctk_ParameterDelete):
    /// resolve + MakeModelOwner + ProParameterInit + ProParameterDelete;
    /// 参数不存在 → false(幂等)，成功 → true。</summary>
    public bool ParameterDelete(ModelIdentity model, string name)
    {
        if (!TryResolveMdl(model, out var mdl)) return false;
        var ownerItem = MakeModelOwner(mdl, model.Type);
        var param = new GNS.proparameter();
        var initRc = G.ProParameterInit(ref ownerItem, ToProName(name), ref param);
        if (IsNotFound(initRc)) return false;
        Check.Eval(nameof(G.ProParameterInit), initRc);
        Check.Eval(nameof(G.ProParameterDelete), G.ProParameterDelete(ref param));
        return true;
    }

    /// <summary>直绑(退役 Ctk_ParameterReset):
    /// resolve + MakeModelOwner + ProParameterInit + ProParameterValueReset;
    /// 参数不存在 → false(幂等)。</summary>
    public bool ParameterReset(ModelIdentity model, string name)
    {
        if (!TryResolveMdl(model, out var mdl)) return false;
        var ownerItem = MakeModelOwner(mdl, model.Type);
        var param = new GNS.proparameter();
        var initRc = G.ProParameterInit(ref ownerItem, ToProName(name), ref param);
        if (IsNotFound(initRc)) return false;
        Check.Eval(nameof(G.ProParameterInit), initRc);
        Check.Eval(nameof(G.ProParameterValueReset), G.ProParameterValueReset(ref param));
        return true;
    }

    public bool? ParameterIsDesignated(ModelIdentity model, string name)
    {
        // B3 直绑：ProParameterInit 构造句柄，再调 ProParameterDesignationVerify
        if (!TryResolveMdl(new ItemRef(model, CreoModelItemType.Unknown, 0), out var mdl)) return null;
        var ownerItem = new GNS.pro_model_item { type = (GNS.pro_obj_types)ToProMdlType(model.Type), id = 0, owner = mdl };
        var param = new GNS.proparameter();
        int st = (int)G.ProParameterInit(ref ownerItem, ToProName(name), ref param);
        if (st < 0) return null;   // PRO_TK_E_NOT_FOUND 等
        Ensure(nameof(G.ProParameterInit), st);
        int stV = (int)G.ProParameterDesignationVerify(ref param, out var exists);
        if (stV < 0) return null;
        Ensure(nameof(G.ProParameterDesignationVerify), stV);
        return exists != GNS.ProBooleans.PRO_B_FALSE;
    }

    public bool ParameterSetDesignated(ModelIdentity model, string name, bool designated)
    {
        // B3 直绑：ProParameterInit + Add 或 Remove
        if (!TryResolveMdl(new ItemRef(model, CreoModelItemType.Unknown, 0), out var mdl))
            throw new InvalidOperationException($"模型 {model.Name} 无法解析");
        var ownerItem = new GNS.pro_model_item { type = (GNS.pro_obj_types)ToProMdlType(model.Type), id = 0, owner = mdl };
        var param = new GNS.proparameter();
        int stInit = (int)G.ProParameterInit(ref ownerItem, ToProName(name), ref param);
        if (stInit < 0) return false;   // 参数不存在
        Ensure(nameof(G.ProParameterInit), stInit);
        if (designated)
            Ensure(nameof(G.ProParameterDesignationAdd), (int)G.ProParameterDesignationAdd(ref param));
        else
            Ensure(nameof(G.ProParameterDesignationRemove), (int)G.ProParameterDesignationRemove(ref param));
        return true;
    }

    /// <summary>ProParameterDescriptionGet 直绑样板
    /// (wstring out + Marshal.PtrToStringUni + finally ProWstringFree)。
    /// 参数不存在或无 description 一律 null；真错误抛 CreoException。
    /// </summary>
    public string? ParameterDescription(ModelIdentity model, string name)
    {
        if (!TryResolveMdl(new ItemRef(model, CreoModelItemType.Unknown, 0), out var mdl)) return null;
        var ownerItem = new GNS.pro_model_item
        {
            type  = (GNS.pro_obj_types)ToProMdlType(model.Type),
            id    = 0,
            owner = mdl
        };
        var param = new GNS.proparameter();
        int stInit = (int)G.ProParameterInit(ref ownerItem, ToProName(name), ref param);
        if (stInit < 0) return null;     // 参数不存在（PRO_TK_E_NOT_FOUND 等）→ null
        Ensure(nameof(G.ProParameterInit), stInit);

        var rc = (GNS.ProErrors)G.ProParameterDescriptionGet(ref param, out IntPtr descPtr);
        if (IsNotFound(rc)) return null; // query 语义：无 description
        Check.Eval(nameof(G.ProParameterDescriptionGet), rc);

        if (descPtr == IntPtr.Zero) return null;
        try { return Marshal.PtrToStringUni(descPtr); }
        finally { G.ProWstringFree(descPtr); }   // 铁律：Creo 串经 ProWstringFree 释放，失败路径也释放
    }


    public CreoParameterValueWithUnits? ParameterGetWithUnits(ModelIdentity model, string name)
    {
        if (!TryResolveMdl(model, out var mdl)) return null;
        var owner = MakeModelOwner(mdl, model.Type);
        var param = new GNS.proparameter();
        var initRc = G.ProParameterInit(ref owner, ToProName(name), ref param);
        if (IsNotFound(initRc)) return null;
        Check.Eval(nameof(G.ProParameterInit), initRc);

        var pv = new GNS.Pro_Param_Value();
        var units = new GNS.ProUnititem();
        Check.Eval(nameof(G.ProParameterValueWithUnitsGet),
            G.ProParameterValueWithUnitsGet(ref param, ref pv, ref units));

        return new CreoParameterValueWithUnits(
            name,
            pv.value.d_val,
            ToCreoUnit(model, ref units),
            IsModified: false);
    }

    public void ParameterSetWithUnits(ModelIdentity model, string name, double value, CreoUnit? unit)
    {
        if (name.Length >= CtkAbiConstants.ProNameSize)
            throw new ArgumentException(
                $"参数名超长: {name.Length} 字符, 上限 {CtkAbiConstants.ProNameSize - 1}(含 NUL)。", nameof(name));

        if (!TryResolveMdl(model, out var mdl))
            throw new InvalidOperationException($"模型 {model.Name} 无法解析，无法设置参数。");

        var owner = MakeModelOwner(mdl, model.Type);
        var param = new GNS.proparameter();
        var initRc = G.ProParameterInit(ref owner, ToProName(name), ref param);
        var pv = ToProParamValue(CreoParamValue.OfDouble(value));

        if (IsNotFound(initRc))
        {
            if (unit is { } u)
            {
                var unitItem = ResolveUnitForWrite(mdl, u);
                Check.Eval(nameof(G.ProParameterWithUnitsCreate),
                    G.ProParameterWithUnitsCreate(ref owner, ToProName(name), ref pv, ref unitItem, ref param));
            }
            else
            {
                Check.Eval(nameof(G.ProParameterWithUnitsCreate),
                    G.ProParameterWithUnitsCreate(ref owner, ToProName(name), ref pv, IntPtr.Zero, ref param));
            }
            return;
        }

        Check.Eval(nameof(G.ProParameterInit), initRc);
        if (unit is { } existingUnit)
        {
            var unitItem = ResolveUnitForWrite(mdl, existingUnit);
            Check.Eval(nameof(G.ProParameterValueWithUnitsSet),
                G.ProParameterValueWithUnitsSet(ref param, ref pv, ref unitItem));
        }
        else
        {
            Check.Eval(nameof(G.ProParameterValueWithUnitsSet),
                G.ProParameterValueWithUnitsSet(ref param, ref pv, IntPtr.Zero));
        }
    }

    public CreoUnit? ParameterUnitsGet(ModelIdentity model, string name)
    {
        if (!TryResolveMdl(model, out var mdl)) return null;
        var owner = MakeModelOwner(mdl, model.Type);
        var param = new GNS.proparameter();
        var initRc = G.ProParameterInit(ref owner, ToProName(name), ref param);
        if (IsNotFound(initRc)) return null;
        Check.Eval(nameof(G.ProParameterInit), initRc);

        var units = new GNS.ProUnititem();
        var rc = G.ProParameterUnitsGet(ref param, ref units);
        if (IsNotFound(rc)) return null;
        Check.Eval(nameof(G.ProParameterUnitsGet), rc);
        return ToCreoUnit(model, ref units);
    }

    public bool ParameterUnitsAssign(ModelIdentity model, string name, CreoUnit unit)
    {
        if (!TryResolveMdl(model, out var mdl)) return false;
        var owner = MakeModelOwner(mdl, model.Type);
        var param = new GNS.proparameter();
        var initRc = G.ProParameterInit(ref owner, ToProName(name), ref param);
        if (IsNotFound(initRc)) return false;
        Check.Eval(nameof(G.ProParameterInit), initRc);

        var unitItem = ResolveUnitForWrite(mdl, unit);
        var rc = G.ProParameterUnitsAssign(ref param, ref unitItem);
        if (IsNotFound(rc)) return false;
        Check.Eval(nameof(G.ProParameterUnitsAssign), rc);
        return true;
    }

    public CreoUnit? UnitInit(ModelIdentity model, string unitName)
    {
        if (!TryResolveMdl(model, out var mdl)) return null;
        var unit = new GNS.ProUnititem();
        var rc = G.ProUnitInit(mdl, ToProName(unitName), ref unit);
        // Creo 4 真机实测:单位名不存在返 BAD_INPUTS(头文件声称 E_NOT_FOUND,实测不符)。
        // 按 "单位不存在返 null" 契约,二者均归 not-found。
        if (IsNotFound(rc) || rc == GNS.ProErrors.PRO_TK_BAD_INPUTS) return null;
        Check.Eval(nameof(G.ProUnitInit), rc);
        return ToCreoUnit(model, ref unit, unitName);
    }

    public CreoUnit? UnitFromExpression(ModelIdentity model, string expression)
    {
        if (!TryResolveMdl(model, out var mdl)) return null;
        var unit = new GNS.ProUnititem();
        var rc = G.ProUnitInitByExpression(mdl, ToProName(expression, 260), ref unit);
        if (IsNotFound(rc)) return null;
        Check.Eval(nameof(G.ProUnitInitByExpression), rc);
        return ToCreoUnit(model, ref unit);
    }

    public CreoUnitConversion UnitConvert(ModelIdentity model, string fromName, string toName)
    {
        if (!TryResolveMdl(model, out var mdl))
            throw new InvalidOperationException($"模型 {model.Name} 无法解析，无法换算单位。");

        var from = new GNS.ProUnititem();
        Check.Eval(nameof(G.ProUnitInit), G.ProUnitInit(mdl, ToProName(fromName), ref from));
        var to = new GNS.ProUnititem();
        Check.Eval(nameof(G.ProUnitInit), G.ProUnitInit(mdl, ToProName(toName), ref to));

        var conv = new GNS.ProUnitConversion();
        Check.Eval(nameof(G.ProUnitConversionCalculate),
            G.ProUnitConversionCalculate(ref from, ref to, ref conv));
        return new CreoUnitConversion(conv.scale, conv.offset);
    }

    public string UnitExpressionGet(ModelIdentity model, string unitName)
    {
        if (!TryResolveMdl(model, out var mdl)) return string.Empty;

        var unit = new GNS.ProUnititem();
        var initRc = G.ProUnitInit(mdl, ToProName(unitName), ref unit);
        if (IsNotFound(initRc)) return string.Empty;
        Check.Eval(nameof(G.ProUnitInit), initRc);

        var expr = new char[260];
        var rc = G.ProUnitExpressionGet(ref unit, expr);
        if (IsNotFound(rc)) return string.Empty;
        Check.Eval(nameof(G.ProUnitExpressionGet), rc);
        return FromProName(expr);
    }

    public CreoUnitSystem? ModelPrincipalUnitSystemGet(ModelIdentity model)
    {
        if (!TryResolveMdl(model, out var mdl)) return null;

        var system = new GNS.ProUnitsystem();
        var rc = G.ProMdlPrincipalunitsystemGet(mdl, ref system);
        if (rc != GNS.ProErrors.PRO_TK_NO_ERROR) return null;
        return ToCreoUnitSystem(model, ref system);
    }

    public IReadOnlyList<CreoUnitSystem> ModelUnitSystemsList(ModelIdentity model)
    {
        if (!TryResolveMdl(model, out var mdl)) return Array.Empty<CreoUnitSystem>();

        var rc = G.ProMdlUnitsystemsCollect(mdl, out IntPtr arr);
        // scope 先接管再判 rc:头文件未硬约定 NotFound 时 arr 必为 null,早退路径也必须释放
        // (Zero 句柄 Dispose 短路无副作用)。
        using var scope = ProArrayScope.OwnedPlain(arr);
        // 双 defensive guard:正常路径下二者互斥(IsNotFound rc 时 arr 必为 null,反之亦然),
        // 但 Pro Toolkit 头文件未硬约定;保留 `arr == IntPtr.Zero` 兜底防 ProArray 初始化漂移。
        if (IsNotFound(rc)) return Array.Empty<CreoUnitSystem>();
        Check.Eval(nameof(G.ProMdlUnitsystemsCollect), rc);
        if (scope.Handle == IntPtr.Zero) return Array.Empty<CreoUnitSystem>();

        // 严格 SizeGet 校验:失败抛(保原语义)
        Check.Eval(nameof(G.ProArraySizeGet), G.ProArraySizeGet(scope.Handle, out var count));
        var result = new List<CreoUnitSystem>(count);
        int itemSize = Marshal.SizeOf<GNS.ProUnitsystem>();
        for (int i = 0; i < count; i++)
        {
            var system = Marshal.PtrToStructure<GNS.ProUnitsystem>(scope.Handle + i * itemSize);
            result.Add(ToCreoUnitSystem(model, ref system));
        }
        return result;
    }

    private static CreoUnit? ToCreoUnit(ModelIdentity model, ref GNS.ProUnititem unit, string? fallbackName = null)
    {
        var unitName = ReadProName(unit.name);
        if (string.IsNullOrEmpty(unitName))
            unitName = fallbackName ?? string.Empty;
        if (unitName.Length == 0) return null;

        Check.Eval(nameof(G.ProUnitTypeGet), G.ProUnitTypeGet(ref unit, out var type));
        return new CreoUnit(model.Name, model.Type, unitName, type);
    }

    private static CreoUnitSystem ToCreoUnitSystem(ModelIdentity model, ref GNS.ProUnitsystem system)
    {
        var systemName = ReadProName(system.name);
        Check.Eval(nameof(G.ProUnitsystemTypeGet), G.ProUnitsystemTypeGet(ref system, out var type));
        return new CreoUnitSystem(model.Name, model.Type, systemName, type);
    }

    public void ModelPrincipalUnitSystemSet(
        ModelIdentity model, CreoUnitSystem newSystem,
        CreoUnitConversionMode conversionMode, bool ignoreParamUnits,
        int regenerationFlags = -1)
    {
        var convertType = ToProUnitConvertType(conversionMode);

        if (newSystem.IsUninitialized)
            throw new ArgumentException("newSystem 是 default 值，不能用于设置主单位系统。", nameof(newSystem));
        if (newSystem.ModelName != model.Name || newSystem.ModelType != model.Type)
            throw new CrossModelUnitException(newSystem.ModelName, model.Name);

        if (!TryResolveMdl(model, out var mdl))
            throw new InvalidOperationException($"模型 {model.Name} 无法解析。");

        // guard: 读当前系统验类型匹配(Check.Eval 透传不复用吞错路径)
        var current = new GNS.ProUnitsystem();
        Check.Eval("ProMdlPrincipalunitsystemGet (guard)",
            G.ProMdlPrincipalunitsystemGet(mdl, ref current));
        Check.Eval(nameof(G.ProUnitsystemTypeGet),
            G.ProUnitsystemTypeGet(ref current, out var currentType));

        // 用 L3 权威 type(newSystem.Type 由 Get 路径填充 ProUnitsystemTypeGet 出参)比对,
        // 避免对手工构造的空壳 struct 调 Get 拿到无意义 type 值。
        if (currentType != newSystem.Type)
            throw new UnitSystemTypeMismatchException(currentType.ToString(), newSystem.Type.ToString());

        // 从模型的单位系统数组中按名字定位已初始化的 struct(不能手工构造 —— native 校验会拒)。
        // scope 先接管再评估 rc:Check.Eval 抛出瞬间句柄已有释放责任人。
        var collectRc = G.ProMdlUnitsystemsCollect(mdl, out IntPtr arr);
        using var scope = ProArrayScope.OwnedPlain(arr);
        Check.Eval(nameof(G.ProMdlUnitsystemsCollect), collectRc);
        if (scope.Handle == IntPtr.Zero)
            throw new InvalidOperationException($"模型 {model.Name} 未返回单位系统数组。");

        // 严格 SizeGet 校验:失败抛(保原语义)
        Check.Eval(nameof(G.ProArraySizeGet), G.ProArraySizeGet(scope.Handle, out var count));
        int itemSize = Marshal.SizeOf<GNS.ProUnitsystem>();
        for (int i = 0; i < count; i++)
        {
            var candidate = Marshal.PtrToStructure<GNS.ProUnitsystem>(scope.Handle + i * itemSize);
            if (ReadProName(candidate.name) == newSystem.Name)
            {
                Check.Eval(nameof(G.ProMdlPrincipalunitsystemSet),
                    G.ProMdlPrincipalunitsystemSet(mdl, ref candidate, convertType,
                        ignoreParamUnits ? GNS.ProBooleans.PRO_B_TRUE : GNS.ProBooleans.PRO_B_FALSE,
                        regenerationFlags));
                return;
            }
        }
        throw new InvalidOperationException(
            $"模型 {model.Name} 中未找到名为 {newSystem.Name} 的单位系统。");
    }

    private static GNS.ProUnitConvertType ToProUnitConvertType(CreoUnitConversionMode mode) => mode switch
    {
        CreoUnitConversionMode.PreserveDimensionValues => GNS.ProUnitConvertType.PRO_UNITCONVERT_SAME_DIMS,
        CreoUnitConversionMode.PreservePhysicalSize    => GNS.ProUnitConvertType.PRO_UNITCONVERT_SAME_SIZE,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode,
                $"Unknown CreoUnitConversionMode value {mode}"),
    };

    private static string ReadProName(ushort[]? buf)
        => buf is null ? string.Empty : FromProNameUshort(buf);

    private static GNS.ProUnititem ResolveUnitForWrite(IntPtr mdl, CreoUnit unit)
    {
        var unitItem = new GNS.ProUnititem();
        Check.Eval(nameof(G.ProUnitInit), G.ProUnitInit(mdl, ToProName(unit.Name), ref unitItem));
        return unitItem;
    }

    // 可空 units(POD-struct-ref 传 NULL)现由生成器原生支持:ProParameterWithUnitsCreate /
    // ProParameterValueWithUnitsSet 均产出 `IntPtr units` 重载(nullable_params),调用方
    // 直接传 IntPtr.Zero 即可,原手写两条 IntPtr 兜底 DllImport 已退役。
}
