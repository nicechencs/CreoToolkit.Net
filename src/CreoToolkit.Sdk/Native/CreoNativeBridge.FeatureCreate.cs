using System.Runtime.InteropServices;
using System.Text;
using CreoToolkit.Sdk.Diagnostics;
using CreoToolkit.Sdk.Errors;
using GNS = CreoToolkit.Interop.Generated;
using G = CreoToolkit.Interop.Generated.NativeMethods;

namespace CreoToolkit.Sdk.Native;

internal sealed partial class CreoNativeBridge
{
    // ---- 特征元素树写入链 ----
    // spec 树(纯托管)→ 单次调用序列内 Alloc→Set→Add→Create→Free。
    // 自建树用 ProElementFree 释放(caller 自建 caller 释放),与 extractor 树的
    // ProFeatureElemtreeFree(带 feature 上下文)契约分离。
    // 调用序列对照官方样例 UgDatumCreate.c(pt_userguide/ptu_datum)。

    private const int ProRegenNoFlags = 0;  // ProSolid.h PRO_REGEN_NO_FLAGS

    /// <summary>按 spec 树创建特征(ProFeatureWithoptionsCreate)。
    /// 正常路径与异常路径均在 finally 释放全部临时 native 资源(opts 数组/自建树/模型 selection)。
    /// create 失败时收集元素诊断(ProElementDiagnosticsCollect)+ 错误表进异常上下文。</summary>
    public ItemRef FeatureCreateFromElemtree(ModelIdentity model, ElemSpec root)
    {
        if (!TryResolveMdl(model, out var mdl))
            throw new InvalidOperationException($"模型 {model.Name} 不在会话中(ProMdlnameInit 失败,需先 Retrieve/Load)");

        IntPtr tree = IntPtr.Zero, mdlSel = IntPtr.Zero;
        try
        {
            tree = BuildElementSubtree(mdl, root);
            mdlSel = AllocModelSelection(mdl);

            // options = ProArray[1] { PRO_FEAT_CR_NO_OPTS }(样例形态;元素 4 字节 enum;
            // Alloc 失败由 helper 内 Check.Eval 抛)
            Span<int> optsData = stackalloc int[] { (int)GNS.pro_feature_create_options.PRO_FEAT_CR_NO_OPTS };
            using var optsScope = ProArrayMarshal.AllocStructs<int>(optsData);

            var feat = default(GNS.pro_model_item);
            var errs = default(GNS.ProErrorlist);
            var rc = G.ProFeatureWithoptionsCreate(mdlSel, tree, optsScope.Handle, ProRegenNoFlags, ref feat, ref errs);
            if (rc != GNS.ProErrors.PRO_TK_NO_ERROR)
            {
                var detail = CollectCreateFailureDetail(tree, in errs);
                CreoSdkLog.Trace("feature", "create_from_elemtree.failed",
                    new { model = model.Name, rc = (int)rc, detail });
                Check.Eval(nameof(G.ProFeatureWithoptionsCreate), rc, detail);
                // rc 非错误分类(如交互中止)也无有效特征可回——统一按失败抛出
                throw new InvalidOperationException(
                    $"ProFeatureWithoptionsCreate rc={(int)rc} 未产出特征: {detail}");
            }

            CreoSdkLog.Trace("feature", "create_from_elemtree.ok",
                new { model = model.Name, id = feat.id });
            return new ItemRef(model, CreoModelItemType.Feature, feat.id);
        }
        finally
        {
            if (tree != IntPtr.Zero) G.ProElementFree(ref tree);
            if (mdlSel != IntPtr.Zero) G.ProSelectionFree(ref mdlSel);
        }
    }

    /// <summary>深度优先构造 spec 子树 → native ProElement。
    /// 子元素 Add 进父树后归父树所有(随根 ProElementFree 释放);
    /// 构造中途失败时仅需释放尚未挂树的当前节点。</summary>
    private IntPtr BuildElementSubtree(IntPtr mdl, ElemSpec spec)
    {
        Check.Eval(nameof(G.ProElementAlloc), G.ProElementAlloc(spec.ElementId, out var elem));
        try
        {
            SetElementValue(mdl, elem, spec);
            foreach (var childSpec in spec.Children)
            {
                var child = BuildElementSubtree(mdl, childSpec);
                var addRc = G.ProElemtreeElementAdd(elem, IntPtr.Zero, child);
                if (addRc != GNS.ProErrors.PRO_TK_NO_ERROR)
                {
                    // 未挂上的子树须由本层释放,再走统一错误分类
                    G.ProElementFree(ref child);
                    Check.Eval(nameof(G.ProElemtreeElementAdd), addRc);
                    throw new InvalidOperationException(
                        $"ProElemtreeElementAdd rc={(int)addRc} (elem id={childSpec.ElementId})");
                }
            }
            return elem;
        }
        catch
        {
            if (elem != IntPtr.Zero) G.ProElementFree(ref elem);
            throw;
        }
    }

    /// <summary>按 spec 值种类写元素值。Double 且带 Decimals 时先 DecimalsSet 后 DoubleSet(顺序约束);
    /// Reference 经临时 ProSelection→ProReference,set 成功后 reference 归树所有(样例契约),
    /// selection 用毕即释;set 失败时 reference 由本层释放。</summary>
    private void SetElementValue(IntPtr mdl, IntPtr elem, ElemSpec spec)
    {
        switch (spec.ValueKind)
        {
            case ElemSpecValueKind.None:
                return;
            case ElemSpecValueKind.Integer:
                Check.Eval(nameof(G.ProElementIntegerSet), G.ProElementIntegerSet(elem, spec.IntValue));
                return;
            case ElemSpecValueKind.Double:
                if (spec.Decimals is { } decimals)
                    Check.Eval(nameof(G.ProElementDecimalsSet), G.ProElementDecimalsSet(elem, decimals));
                Check.Eval(nameof(G.ProElementDoubleSet), G.ProElementDoubleSet(elem, spec.DoubleValue));
                return;
            case ElemSpecValueKind.Reference:
                SetElementReference(mdl, elem, spec.ItemReference!.Value);
                return;
            default:
                throw new NotSupportedException($"ElemSpec 值种类 {spec.ValueKind} 未支持(elem id={spec.ElementId})");
        }
    }

    private void SetElementReference(IntPtr mdl, IntPtr elem, ItemRef item)
    {
        IntPtr sel = IntPtr.Zero;
        try
        {
            if (!TryAllocSelection(mdl, item, out sel))
                throw new InvalidOperationException(
                    $"参照 (type={item.Type}, id={item.Id}) 无法解析为 ProSelection(项不存在?)");
            Check.Eval(nameof(G.ProSelectionToReference), G.ProSelectionToReference(sel, out var reference));
            var rc = G.ProElementReferenceSet(elem, reference);
            if (rc != GNS.ProErrors.PRO_TK_NO_ERROR)
            {
                G.ProReferenceFree(reference);
                Check.Eval(nameof(G.ProElementReferenceSet), rc);
                throw new InvalidOperationException($"ProElementReferenceSet rc={(int)rc}");
            }
        }
        finally
        {
            if (sel != IntPtr.Zero) G.ProSelectionFree(ref sel);
        }
    }

    /// <summary>整模型 ProSelection(ProMdlToModelitem + ProSelectionAlloc;顶层零件 comp path 空表)。</summary>
    private static unsafe IntPtr AllocModelSelection(IntPtr mdl)
    {
        var mi = default(GNS.pro_model_item);
        Check.Eval(nameof(G.ProMdlToModelitem), G.ProMdlToModelitem(mdl, ref mi));
        var path = default(GNS.pro_comp_path);
        path.owner = mdl;
        path.table_num = 0;
        Check.Eval(nameof(G.ProSelectionAlloc), G.ProSelectionAlloc(ref path, ref mi, out var sel));
        return sel;
    }

    /// <summary>create 失败诊断:错误表(ProErrorlist)+ 元素诊断(ProElementDiagnosticsCollect;
    /// 数组用 ProElemdiagnosticProarrayFree 按值释放)。尽力收集,自身不抛。</summary>
    private static string CollectCreateFailureDetail(IntPtr tree, in GNS.ProErrorlist errs)
    {
        var sb = new StringBuilder();
        sb.Append("errors=").Append(errs.error_number);
        if (errs.error_list != IntPtr.Zero && errs.error_number > 0)
        {
            int size = Marshal.SizeOf<GNS.ProItemerror>();
            int n = Math.Min(errs.error_number, 16);
            for (int i = 0; i < n; i++)
            {
                var e = Marshal.PtrToStructure<GNS.ProItemerror>(errs.error_list + i * size);
                sb.Append($" [item={e.err_item_id} type={(int)e.err_item_type} err={(int)e.error}]");
            }
        }
        try
        {
            var rc = G.ProElementDiagnosticsCollect(tree, out var diags);
            if (rc == GNS.ProErrors.PRO_TK_NO_ERROR && diags != IntPtr.Zero)
            {
                try
                {
                    if (G.ProArraySizeGet(diags, out var count) == GNS.ProErrors.PRO_TK_NO_ERROR)
                    {
                        sb.Append(" diagnostics=").Append(count);
                        for (int i = 0; i < Math.Min(count, 8); i++)
                        {
                            var d = Marshal.ReadIntPtr(diags, i * IntPtr.Size);
                            if (d == IntPtr.Zero) continue;
                            G.ProElemdiagnosticSeverityGet(d, out var severity);
                            var msg = new char[256];
                            if (G.ProElemdiagnosticMessageGet(d, msg) == GNS.ProErrors.PRO_TK_NO_ERROR)
                            {
                                var text = new string(msg).TrimEnd('\0');
                                sb.Append($" [{(int)severity}:{text}]");
                            }
                        }
                    }
                }
                finally { G.ProElemdiagnosticProarrayFree(diags); }
            }
        }
        catch
        {
            // 诊断收集为尽力而为,失败不掩盖原始 create 错误
        }
        return sb.ToString();
    }
}
