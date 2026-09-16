using System.Runtime.InteropServices;
using CreoToolkit.Sdk.Diagnostics;
using CreoToolkit.Sdk.Errors;
using GNS = CreoToolkit.Interop.Generated;
using G = CreoToolkit.Interop.Generated.NativeMethods;

namespace CreoToolkit.Sdk.Native;

internal sealed partial class CreoNativeBridge
{
    // 单位阵(行主序 16 元素),packaged 装入初始位置。
    private static readonly double[] IdentityMatrix4x4 =
    {
        1, 0, 0, 0,
        0, 1, 0, 0,
        0, 0, 1, 0,
        0, 0, 0, 1,
    };

    /// <summary>装入组件并设置约束。constraints 为空时纯 packaged 装入。
    /// 约束数组是真 ProArray(ProArrayAlloc + ObjectAdd);ConstraintsSet 平传句柄本体
    /// (头文件 INVALID_PTR 校验 ProArray 头,manifest 覆盖生成 IntPtr)。
    /// 内存契约:ConstraintsSet 一经调用,数组与单约束一律不再释放(官方样例形态);
    /// 仅构造中途异常且尚未调用时逐个 ProAsmcompconstraintFree + ProArrayFree。
    /// selection 按「reference Set 后不释放」对齐官方样例。</summary>
    public ItemRef ComponentAssemble(
        ModelIdentity assembly,
        ModelIdentity component,
        IReadOnlyList<AssemblyConstraintSpec> constraints)
    {
        if (!TryResolveMdl(assembly, out var asmMdl))
            throw new InvalidOperationException(
                $"装配 {assembly.Name} 不在会话中(需先 Retrieve/Load)");
        if (!TryResolveMdl(component, out var compMdl))
            throw new InvalidOperationException(
                $"组件 {component.Name} 不在会话中(需先 Retrieve/Load)");

        var feat = default(GNS.pro_model_item);
        var initPos = (double[])IdentityMatrix4x4.Clone();

        // 装入(packaged 态,位置=单位阵)
        Check.Eval(nameof(G.ProAsmcompAssemble),
            G.ProAsmcompAssemble(asmMdl, compMdl, initPos, ref feat));

        if (constraints.Count > 0)
        {
            SetConstraints(asmMdl, compMdl, assembly, component, ref feat, constraints);

            // 保险性整装配再生(样例形态;失败不影响已落约束)
            G.ProSolidRegenerate(asmMdl, ProRegenNoFlags);
        }

        CreoSdkLog.Trace("asmcomp", "assemble.ok",
            new
            {
                assembly = assembly.Name,
                component = component.Name,
                id = feat.id,
                constraintCount = constraints.Count,
            });
        return new ItemRef(assembly, CreoModelItemType.Feature, feat.id);
    }

    /// <summary>构造约束 ProArray 并调 ConstraintsSet(首参传 owner=asm、table_num=0 顶层路径)。
    /// 失败原子回退:组件回到调用前配置(native 契约),异常携带 rc。
    /// <para>ObjectAdd 按需 realloc 并经 ref 写回 constrs,ProArrayScope 持有的是构造时句柄、
    /// 无法跟随写回,故不作 scope 包装;释放责任在 ConstraintsSet 发起时即转移(setCalled),
    /// rc 失败也不释放,仅构造中途异常且尚未调用时释放 allocated 清单与数组容器。</para></summary>
    private unsafe void SetConstraints(
        IntPtr asmMdl,
        IntPtr compMdl,
        ModelIdentity assembly,
        ModelIdentity component,
        ref GNS.pro_model_item feat,
        IReadOnlyList<AssemblyConstraintSpec> constraints)
    {
        IntPtr constrs = IntPtr.Zero;
        var allocated = new List<IntPtr>(constraints.Count);
        bool setCalled = false;

        try
        {
            // 约束数组必须是 ProArray(元素 = ProAsmcompconstraint 不透明指针)
            Check.Eval(nameof(G.ProArrayAlloc),
                G.ProArrayAlloc(0, IntPtr.Size, 1, out constrs));

            foreach (var spec in constraints)
            {
                Check.Eval(nameof(G.ProAsmcompconstraintAlloc),
                    G.ProAsmcompconstraintAlloc(out var c));
                allocated.Add(c);

                Check.Eval(nameof(G.ProAsmcompconstraintTypeSet),
                    G.ProAsmcompconstraintTypeSet(c,
                        (GNS.pro_asm_constraint_type)(int)spec.Type));

                SetConstraintReference(asmMdl, spec.AsmRef, c, isAsmSide: true, spec.AsmSide, spec.AsmRefCompFeatId);
                SetConstraintReference(compMdl, spec.CompRef, c, isAsmSide: false, spec.CompSide, refCompFeatId: null);

                if (spec.Offset is { } offset)
                    Check.Eval(nameof(G.ProAsmcompconstraintOffsetSet),
                        G.ProAsmcompconstraintOffsetSet(c, offset));

                // ObjectAdd 拷入句柄值(源地址 = 局部变量地址)
                Check.Eval(nameof(G.ProArrayObjectAdd),
                    G.ProArrayObjectAdd(ref constrs, -1, 1, (IntPtr)(&c)));
            }

            // 首参:顶层路径(owner=asm, table_num=0)语义等价样例的 NULL
            var topPath = default(GNS.pro_comp_path);
            topPath.owner = asmMdl;
            topPath.table_num = 0;

            setCalled = true;
            var rc = G.ProAsmcompConstraintsSet(ref topPath, ref feat, constrs);
            if (rc != GNS.ProErrors.PRO_TK_NO_ERROR)
            {
                CreoSdkLog.Trace("asmcomp", "assemble.failed",
                    new { assembly = assembly.Name, component = component.Name, rc = (int)rc });
                Check.Eval(nameof(G.ProAsmcompConstraintsSet), rc,
                    $"约束设置失败(组件已原子回退到调用前配置): assembly={assembly.Name}, component={component.Name}");
            }
        }
        catch when (!setCalled)
        {
            // 构造中途异常,尚未调用 ConstraintsSet:逐个释放约束 + 释放数组容器
            foreach (var ptr in allocated)
            {
                if (ptr != IntPtr.Zero)
                    G.ProAsmcompconstraintFree(ptr);
            }
            if (constrs != IntPtr.Zero)
                G.ProArrayFree(ref constrs);
            throw;
        }
    }

    /// <summary>约束参照写入:临时 ProSelection → reference Set。
    /// refCompFeatId 非空时为件对件形态(参照位于装配内组件,comppath 指向该组件);
    /// 否则 owner mdl 顶层路径。set 成功后 selection 归约束所有(样例契约,不释放);
    /// set 失败由本层释放。</summary>
    private void SetConstraintReference(
        IntPtr mdl, ItemRef item, IntPtr constraint,
        bool isAsmSide, CreoAssemblyConstraintSide side, int? refCompFeatId)
    {
        IntPtr sel;
        var ok = refCompFeatId is { } compFeatId
            ? TryAllocSelectionInComponent(mdl, compFeatId, item, out sel)
            : TryAllocSelection(mdl, item, out sel);
        if (!ok)
            throw new InvalidOperationException(
                $"{(isAsmSide ? "装配" : "组件")}侧参照 (type={item.Type}, id={item.Id}, " +
                $"refCompFeatId={refCompFeatId?.ToString() ?? "null"}) 无法解析为 ProSelection(项不存在?)");

        var nativeSide = (GNS.pro_datum_side)(int)side;
        var rc = isAsmSide
            ? G.ProAsmcompconstraintAsmreferenceSet(constraint, sel, nativeSide)
            : G.ProAsmcompconstraintCompreferenceSet(constraint, sel, nativeSide);
        if (rc != GNS.ProErrors.PRO_TK_NO_ERROR)
        {
            G.ProSelectionFree(ref sel);
            Check.Eval(isAsmSide
                ? nameof(G.ProAsmcompconstraintAsmreferenceSet)
                : nameof(G.ProAsmcompconstraintCompreferenceSet), rc);
        }
    }

    /// <summary>件对件参照的 ProSelection 分配:comppath 指向装配内组件
    /// (owner=asm, comp_id_table[0]=compFeatId, table_num=1),
    /// modelitem owner 经 ProAsmcomppathMdlGet 解析为组件模型句柄。
    /// 路径无效/项不存在 → false,真错抛。</summary>
    private static unsafe bool TryAllocSelectionInComponent(
        IntPtr asmMdl, int compFeatId, ItemRef item, out IntPtr sel)
    {
        sel = IntPtr.Zero;

        var path = default(GNS.pro_comp_path);
        path.owner = asmMdl;
        path.comp_id_table[0] = compFeatId;
        path.table_num = 1;

        var mdlRc = G.ProAsmcomppathMdlGet(ref path, out var compMdl);
        if (IsAsmcompPathMiss(mdlRc) || compMdl == IntPtr.Zero) return false;
        Check.Eval(nameof(G.ProAsmcomppathMdlGet), mdlRc);

        var mi = new GNS.pro_model_item
        {
            type = (GNS.pro_obj_types)(int)item.Type,
            id = item.Id,
            owner = compMdl,
        };
        var rc = G.ProSelectionAlloc(ref path, ref mi, out sel);
        if (rc == GNS.ProErrors.PRO_TK_NO_ERROR) return true;
        if (IsNotFound(rc)) { sel = IntPtr.Zero; return false; }
        Check.Eval(nameof(G.ProSelectionAlloc), rc);
        sel = IntPtr.Zero;
        return false;
    }

    /// <summary>读回组件约束(ProAsmcompConstraintsWithComppathGet → 逐条 TypeGet/OffsetGet/参照)。
    /// Get 单条失败:该字段置 null/默认继续(尽力语义);数组级失败抛。
    /// 整数组 ProArrayFree(官方读取样例形态);
    /// selection 是 borrowed 不释放。</summary>
    public IReadOnlyList<AssemblyConstraintInfo> ComponentConstraintsRead(
        ModelIdentity assembly, int compFeatId)
    {
        if (!TryResolveMdl(assembly, out var asmMdl))
            throw new InvalidOperationException(
                $"装配 {assembly.Name} 不在会话中(需先 Retrieve/Load)");

        // ProFeatureInit 拿组件特征
        var feat = default(GNS.pro_model_item);
        Check.Eval(nameof(G.ProFeatureInit),
            G.ProFeatureInit(asmMdl, compFeatId, ref feat));

        // 首参:顶层路径(owner=asm, table_num=0)——对齐 ConstraintsSet 首参处理先例
        var topPath = default(GNS.pro_comp_path);
        topPath.owner = asmMdl;
        topPath.table_num = 0;

        var rc = G.ProAsmcompConstraintsWithComppathGet(ref feat, ref topPath, out var constrs);
        Check.Eval(nameof(G.ProAsmcompConstraintsWithComppathGet), rc,
            $"约束读回失败: assembly={assembly.Name}, compFeatId={compFeatId}");
        // 整数组 ProArrayFree(官方读取样例形态);selection 是 borrowed 不释放
        using var scope = ProArrayScope.OwnedPlain(constrs);
        if (scope.Handle == IntPtr.Zero)
            return Array.Empty<AssemblyConstraintInfo>();

        // ProArraySizeGet 容错:失败/空 → 空列表(原语义,与写路径的严格 Check.Eval 不同)
        var pointers = ProArrayMarshal.ReadPointers(scope.Handle);
        if (pointers.Length == 0)
            return Array.Empty<AssemblyConstraintInfo>();

        var result = new AssemblyConstraintInfo[pointers.Length];
        for (int i = 0; i < pointers.Length; i++)
        {
            var cPtr = pointers[i];

            // TypeGet(必读)
            int rawType = 0;
            var trc = G.ProAsmcompconstraintTypeGet(cPtr, out var nativeType);
            if (trc == GNS.ProErrors.PRO_TK_NO_ERROR)
                rawType = (int)nativeType;

            // OffsetGet(失败/无意义时 null)
            double? offset = null;
            var orc = G.ProAsmcompconstraintOffsetGet(cPtr, out var off);
            if (orc == GNS.ProErrors.PRO_TK_NO_ERROR)
                offset = off;

            // AsmreferenceGet → ProSelectionModelitemGet 转 item type+id
            CreoModelItemType? asmRefType = null;
            int? asmRefId = null;
            var arc = G.ProAsmcompconstraintAsmreferenceGet(cPtr, out var asmSel, out _);
            if (arc == GNS.ProErrors.PRO_TK_NO_ERROR && asmSel != IntPtr.Zero)
            {
                var mi = default(GNS.pro_model_item);
                if (G.ProSelectionModelitemGet(asmSel, ref mi) == GNS.ProErrors.PRO_TK_NO_ERROR)
                {
                    asmRefType = (CreoModelItemType)(int)mi.type;
                    asmRefId = mi.id;
                }
                // selection 是 borrowed 不释放
            }

            // CompreferenceGet → ProSelectionModelitemGet 转 item type+id
            CreoModelItemType? compRefType = null;
            int? compRefId = null;
            var crc = G.ProAsmcompconstraintCompreferenceGet(cPtr, out var compSel, out _);
            if (crc == GNS.ProErrors.PRO_TK_NO_ERROR && compSel != IntPtr.Zero)
            {
                var mi = default(GNS.pro_model_item);
                if (G.ProSelectionModelitemGet(compSel, ref mi) == GNS.ProErrors.PRO_TK_NO_ERROR)
                {
                    compRefType = (CreoModelItemType)(int)mi.type;
                    compRefId = mi.id;
                }
            }

            result[i] = new AssemblyConstraintInfo(
                rawType, offset, asmRefType, asmRefId, compRefType, compRefId);
        }

        CreoSdkLog.Trace("asmcomp", "constraints_read.ok",
            new { assembly = assembly.Name, compFeatId, count = pointers.Length });
        return result;
    }

    /// <summary>模板造件(ProAsmcompMdlnameCreateCopy)。
    /// comp_name 是 [In,Out] char[180] U2 数组(ToProName 惯例);
    /// template 为 null 时传 IntPtr.Zero 造空组件。
    /// 失败 rc 区分异常消息:ABORT=模板外部依赖/UNSUPPORTED=Multi-CAD/NOT_VALID=缺许可。</summary>
    public ItemRef ComponentCreateByCopy(
        ModelIdentity assembly,
        string compName,
        CreoModelType compType,
        ModelIdentity? template,
        bool leaveUnplaced)
    {
        // 入口校验:名字非空 ≤31 字符(头注 Creo3 起 PRO_TK_LINE_TOO_LONG)
        ThrowUtil.IfNullOrWhiteSpace(compName);
        if (compName.Length > 31)
            throw new ArgumentException(
                $"组件名 '{compName}' 超过 31 字符限制(PRO_TK_LINE_TOO_LONG)", nameof(compName));

        // 入口校验:仅 Part/Assembly(头注唯二合法值)
        if (compType != CreoModelType.Part && compType != CreoModelType.Assembly)
            throw new ArgumentException(
                $"组件类型仅允许 Part/Assembly,当前 {compType}", nameof(compType));

        if (!TryResolveMdl(assembly, out var asmMdl))
            throw new InvalidOperationException(
                $"装配 {assembly.Name} 不在会话中(需先 Retrieve/Load)");

        // 模板句柄(null → IntPtr.Zero 造空组件)
        IntPtr templateMdl = IntPtr.Zero;
        if (template is { } t)
        {
            if (!TryResolveMdl(t, out templateMdl))
                throw new InvalidOperationException(
                    $"模板 {t.Name} 不在会话中(需先 Retrieve/Load)");
        }

        var name180 = ToProName(compName, 180);
        var feat = default(GNS.pro_model_item);

        var rc = G.ProAsmcompMdlnameCreateCopy(
            asmMdl, name180,
            (GNS.ProMdlType)ToProMdlType(compType),
            templateMdl,
            leaveUnplaced ? GNS.ProBooleans.PRO_B_TRUE : GNS.ProBooleans.PRO_B_FALSE,
            ref feat);

        if (rc != GNS.ProErrors.PRO_TK_NO_ERROR)
        {
            string detail = rc switch
            {
                GNS.ProErrors.PRO_TK_ABORT =>
                    $"模板含外部依赖不可作模板: assembly={assembly.Name}, template={template?.Name ?? "(null)"}",
                GNS.ProErrors.PRO_TK_UNSUPPORTED =>
                    $"Multi-CAD 不支持: assembly={assembly.Name}",
                GNS.ProErrors.PRO_TK_NOT_VALID =>
                    $"缺许可: assembly={assembly.Name}",
                _ =>
                    $"造件失败: assembly={assembly.Name}, compName={compName}, rc={(int)rc}",
            };
            Check.Eval(nameof(G.ProAsmcompMdlnameCreateCopy), rc, detail);
        }

        CreoSdkLog.Trace("asmcomp", "createcopy.ok",
            new
            {
                assembly = assembly.Name,
                compName,
                compType = compType.ToString(),
                template = template?.Name ?? "(null)",
                id = feat.id,
            });
        return new ItemRef(assembly, CreoModelItemType.Feature, feat.id);
    }

    /// <summary>设置组件位置 + 再生(原子语义)。
    /// PositionSet 只改数据不再生(头注契约),Bridge 封装 set+regen;
    /// comppath 首参传顶层路径(owner=asm, table_num=0)。
    /// 矩阵入口校验旋转子阵正交(CreoMat4.IsOrthonormal, 容差 1e-9)。</summary>
    public void ComponentPositionSet(
        ModelIdentity assembly,
        int compFeatId,
        double[] matrix16)
    {
        ThrowUtil.IfNull(matrix16);
        if (matrix16.Length != 16)
            throw new ArgumentException($"矩阵须 16 元素,实际 {matrix16.Length}", nameof(matrix16));

        // 旋转子阵正交校验
        var mat = CreoMat4.FromArray(matrix16);
        if (!mat.IsOrthonormal())
            throw new ArgumentException("矩阵旋转子阵非正交(容差 1e-9)", nameof(matrix16));

        if (!TryResolveMdl(assembly, out var asmMdl))
            throw new InvalidOperationException(
                $"装配 {assembly.Name} 不在会话中(需先 Retrieve/Load)");

        var feat = default(GNS.pro_model_item);
        Check.Eval(nameof(G.ProFeatureInit),
            G.ProFeatureInit(asmMdl, compFeatId, ref feat));

        // 顶层路径
        var topPath = default(GNS.pro_comp_path);
        topPath.owner = asmMdl;
        topPath.table_num = 0;

        Check.Eval(nameof(G.ProAsmcompPositionSet),
            G.ProAsmcompPositionSet(ref topPath, ref feat, matrix16));

        // 再生(PositionSet 不触发,须显式调; update_soft=PRO_B_FALSE 头注 Reserved)
        Check.Eval(nameof(G.ProAsmcompRegenerate),
            G.ProAsmcompRegenerate(ref feat, GNS.ProBooleans.PRO_B_FALSE));

        CreoSdkLog.Trace("asmcomp", "positionset.ok",
            new { assembly = assembly.Name, compFeatId });
    }
}
