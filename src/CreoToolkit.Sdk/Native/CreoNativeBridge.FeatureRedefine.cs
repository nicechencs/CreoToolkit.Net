using System.Runtime.InteropServices;
using System.Text;
using CreoToolkit.Sdk.Diagnostics;
using CreoToolkit.Sdk.Errors;
using GNS = CreoToolkit.Interop.Generated;
using G = CreoToolkit.Interop.Generated.NativeMethods;

namespace CreoToolkit.Sdk.Native;

internal sealed partial class CreoNativeBridge
{
    // ---- 特征元素树编辑(extract 现有树 + 定点改值 + Redefine) ----
    // 官方模式: extract → elempath 定位 → 改值 → WithoptionsRedefine。
    // extract 树用 ProFeatureElemtreeFree(带 feature 上下文)释放,禁 ProElementFree。

    /// <summary>提取特征元素树,按 elemIdPath 定位元素并改 double 值,经 Redefine 写回。
    /// 单次调用序列内完成 extract/定位/改值/Redefine/释放,不暴露半成品树。</summary>
    public void FeatureRedefineElemDouble(ModelIdentity model, int featureId, int[] elemIdPath, double value)
    {
        if (elemIdPath is null || elemIdPath.Length == 0)
            throw new ArgumentException("elemIdPath 不能为空", nameof(elemIdPath));

        if (!TryResolveMdl(model, out var mdl))
            throw new InvalidOperationException(
                $"模型 {model.Name} 不在会话中(ProMdlnameInit 失败,需先 Retrieve/Load)");

        var feat = new GNS.pro_model_item
        {
            type = (GNS.pro_obj_types)(int)CreoModelItemType.Feature,
            id = featureId,
            owner = mdl,
        };
        Check.Eval(nameof(G.ProFeatureInit), G.ProFeatureInit(mdl, featureId, ref feat));

        IntPtr tree = IntPtr.Zero, epath = IntPtr.Zero;
        ProArrayScope? optsScope = null;
        try
        {
            // 1. 提取现有元素树
            var extractRc = G.ProFeatureElemtreeExtract(
                ref feat,
                IntPtr.Zero,
                GNS.pro_feat_elemtree_extract_opts.PRO_FEAT_EXTRACT_NO_OPTS,
                out tree);
            if (extractRc != GNS.ProErrors.PRO_TK_NO_ERROR || tree == IntPtr.Zero)
            {
                var msg = extractRc == GNS.ProErrors.PRO_TK_INVALID_TYPE
                    ? "该特征不支持元素树抽取(INVALID_TYPE)"
                    : $"ProFeatureElemtreeExtract 失败 rc={(int)extractRc}";
                throw new InvalidOperationException(msg);
            }

            // 2. 构造 elempath 定位目标元素
            Check.Eval(nameof(G.ProElempathAlloc), G.ProElempathAlloc(out epath));

            var items = new GNS.path[elemIdPath.Length];
            for (int i = 0; i < elemIdPath.Length; i++)
            {
                items[i].type = GNS.ProElempathItemtype.PRO_ELEM_PATH_ITEM_TYPE_ID;
                items[i].path_item = new GNS.AnonUnion_ProElempath_26_4 { elem_id = elemIdPath[i] };
            }
            Check.Eval(nameof(G.ProElempathDataSet),
                G.ProElempathDataSet(epath, ref items[0], items.Length));

            // 3. 定位并改值
            Check.Eval(nameof(G.ProElemtreeElementGet),
                G.ProElemtreeElementGet(tree, epath, out var elem));
            Check.Eval(nameof(G.ProElementDoubleSet),
                G.ProElementDoubleSet(elem, value));

            // 4. options = ProArray[1] { PRO_FEAT_CR_NO_OPTS }(Alloc 失败由 helper 内 Check.Eval 抛)
            Span<int> optsData = stackalloc int[] { (int)GNS.pro_feature_create_options.PRO_FEAT_CR_NO_OPTS };
            optsScope = ProArrayMarshal.AllocStructs<int>(optsData);

            // 5. Redefine(comp_path=NULL, Part 模式)
            var errs = default(GNS.ProErrorlist);
            var rc = G.ProFeatureWithoptionsRedefine(
                IntPtr.Zero, ref feat, tree, optsScope.Handle, ProRegenNoFlags, ref errs);
            if (rc != GNS.ProErrors.PRO_TK_NO_ERROR)
            {
                var detail = CollectCreateFailureDetail(tree, in errs);
                CreoSdkLog.Trace("feature", "redefine.failed",
                    new { model = model.Name, featureId, rc = (int)rc, detail });
                Check.Eval(nameof(G.ProFeatureWithoptionsRedefine), rc, detail);
                throw new InvalidOperationException(
                    $"ProFeatureWithoptionsRedefine rc={(int)rc}: {detail}");
            }

            CreoSdkLog.Trace("feature", "redefine.ok",
                new { model = model.Name, featureId, value });
        }
        finally
        {
            // 显式 finally 保序:epath → tree → opts(opts 跨层次,using 表达不了此顺序)。
            if (epath != IntPtr.Zero) G.ProElempathFree(ref epath);
            if (tree != IntPtr.Zero) G.ProFeatureElemtreeFree(ref feat, tree);
            optsScope?.Dispose();
        }
    }
}
