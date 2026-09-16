using System.Runtime.InteropServices;
using CreoToolkit.Interop;
using CreoToolkit.Sdk.Diagnostics;
using CreoToolkit.Sdk.Errors;
using GNS = CreoToolkit.Interop.Generated;
using G = CreoToolkit.Interop.Generated.NativeMethods;

namespace CreoToolkit.Sdk.Native;

internal sealed partial class CreoNativeBridge
{
    // ---- 特征 + 元素树 (LIVE: owned 句柄) ----

    // Ctk_FirstFeature 退役，走 G.ProSolidFeatVisit 直绑 + VisitFirst<T>。
    // ProFeature ≡ pro_model_item { type:int(0..3), id:int(4..7), owner:IntPtr(8..15) } x64
    // id offset = 4(不是 8); Generated pro_model_item 已 Sequential 布局,用 PtrToStructure 读最稳。
    public ItemRef? FirstFeature(ModelIdentity model)
    {
        if (!TryResolveMdl(model, out var mdl)) return null;
        return VisitFirst<ItemRef>(
            G.ProSolidFeatVisit, mdl,
            (item, _) =>
            {
                var mi = Marshal.PtrToStructure<GNS.pro_model_item>(item);
                return new ItemRef(model, CreoModelItemType.Feature, mi.id);
            });
    }

    // Ctk_FeatureList 退役，走 G.ProSolidFeatVisit 直绑 + VisitCollect<T>。
    public IReadOnlyList<ItemRef> FeatureList(ModelIdentity model)
    {
        if (!TryResolveMdl(model, out var mdl)) return Array.Empty<ItemRef>();
        return VisitCollect<ItemRef>(
            G.ProSolidFeatVisit, mdl,
            (item, _) =>
            {
                var mi = Marshal.PtrToStructure<GNS.pro_model_item>(item);
                return new ItemRef(model, CreoModelItemType.Feature, mi.id);
            });
    }

    /// <summary>枚举 feature 内指定类型 geomitem(<c>ProFeatureGeomitemVisit</c>,官方
    /// 范式 UgDrawingDimensions.c UsrPointGeomitemsCollect)。datum point 等无
    /// ProSolidXxxVisit 家族的类型经此按 feature 收集。feature 不在会话/无该类项返空。</summary>
    public IReadOnlyList<ItemRef> FeatureGeomitemsList(ItemRef feature, CreoModelItemType itemType)
    {
        if (!TryResolveMdl(feature.Model, out var mdl)) return Array.Empty<ItemRef>();
        var feat = new GNS.pro_model_item
        {
            type = (GNS.pro_obj_types)(int)feature.Type,
            id = feature.Id,
            owner = mdl,
        };
        var items = new List<ItemRef>();
        Exception? caught = null;
        ProVisitAction cb = (item, filterStatus, _) =>
        {
            if (caught != null) return GNS.ProErrors.PRO_TK_GENERAL_ERROR;
            try
            {
                var mi = Marshal.PtrToStructure<GNS.pro_model_item>(item);
                items.Add(new ItemRef(feature.Model, (CreoModelItemType)(int)mi.type, mi.id));
            }
            catch (Exception ex) { caught = ex; return GNS.ProErrors.PRO_TK_GENERAL_ERROR; }
            return GNS.ProErrors.PRO_TK_NO_ERROR;
        };
        var ptr = _visitRegistry.Register(cb);
        try
        {
            var err = G.ProFeatureGeomitemVisit(ref feat,
                (GNS.pro_obj_types)(int)itemType, ptr, IntPtr.Zero, IntPtr.Zero);
            if (err != GNS.ProErrors.PRO_TK_NO_ERROR
                && err != GNS.ProErrors.PRO_TK_E_NOT_FOUND
                && err != GNS.ProErrors.PRO_TK_NOT_EXIST)
            {
                if (caught != null) throw caught;
                Check.Eval(nameof(G.ProFeatureGeomitemVisit), err);
            }
        }
        finally { _visitRegistry.Unregister(cb); }
        if (caught != null) throw caught;
        return items;
    }

    /// <summary>直绑:resolve + G.ProFeatureDelete
    /// (int[1] feat_ids + enum[1] opts; PRO_FEAT_DELETE_CLIP 对齐原 native:连同子件一起删)。
    /// generator 扩 "in-array of T" 识别后,Generated 直接产正确签名,无需 GeneratedHelpers。</summary>
    public void FeatureDelete(ItemRef feature)
    {
        Check.Eval(nameof(G.ProMdlnameInit),
            G.ProMdlnameInit(ToProName(feature.Model.Name, 180),
                (GNS.ProMdlfileType)ToProMdlType(feature.Model.Type), out var mdl));
        Check.Eval(nameof(G.ProFeatureDelete),
            G.ProFeatureDelete(
                mdl,
                new[] { feature.Id }, 1,
                new[] { GNS.pro_feature_delete_opts.PRO_FEAT_DELETE_CLIP }, 1));
    }

    public CreoFeatureInfo? FeatureInfoGet(ItemRef feature)
    {
        if (!TryResolveMdl(feature.Model, out var mdl))
        {
            CreoSdkLog.Trace("feature", "infoget.owner_unresolved",
                new { model = feature.Model.Name, id = feature.Id });
            return null;
        }
        var mi = new GNS.pro_model_item
        {
            type = (GNS.pro_obj_types)(int)feature.Type,
            id = feature.Id,
            owner = mdl,
        };
        var statusRc = G.ProFeatureStatusGet(ref mi, out var status);
        if (statusRc != GNS.ProErrors.PRO_TK_NO_ERROR)
        {
            CreoSdkLog.Trace("feature", "infoget.status_failed",
                new { model = feature.Model.Name, id = feature.Id, rc = (int)statusRc });
            return null;
        }
        var typeRc = G.ProFeatureTypeGet(ref mi, out int featType);
        if (typeRc != GNS.ProErrors.PRO_TK_NO_ERROR)
        {
            CreoSdkLog.Trace("feature", "infoget.type_failed",
                new { model = feature.Model.Name, id = feature.Id, rc = (int)typeRc });
            return null;
        }
        var statusVal = (int)status;
        if (!Enum.IsDefined(typeof(CreoFeatureStatus), statusVal))
        {
            CreoSdkLog.Trace("feature", "infoget.unknown_status",
                new { model = feature.Model.Name, id = feature.Id, rawStatus = statusVal });
        }
        return new CreoFeatureInfo(feature.Id, featType, (CreoFeatureStatus)statusVal);
    }

    public IReadOnlyList<int> FeatureParentsGet(ItemRef feature)
    {
        if (!TryResolveMdl(feature.Model, out var mdl))
        {
            CreoSdkLog.Trace("feature", "parentsget.owner_unresolved",
                new { model = feature.Model.Name, id = feature.Id });
            return Array.Empty<int>();
        }
        var mi = new GNS.pro_model_item
        {
            type = (GNS.pro_obj_types)(int)feature.Type,
            id = feature.Id,
            owner = mdl,
        };
        var rc = G.ProFeatureParentsGet(ref mi, out var arr, out var count);
        if (rc != GNS.ProErrors.PRO_TK_NO_ERROR || arr == IntPtr.Zero || count <= 0)
            return Array.Empty<int>();
        try
        {
            var ids = new int[count];
            Marshal.Copy(arr, ids, 0, count);
            return ids;
        }
        finally { G.ProArrayFree(ref arr); }
    }

    public IReadOnlyList<int> FeatureChildrenGet(ItemRef feature)
    {
        if (!TryResolveMdl(feature.Model, out var mdl))
        {
            CreoSdkLog.Trace("feature", "childrenget.owner_unresolved",
                new { model = feature.Model.Name, id = feature.Id });
            return Array.Empty<int>();
        }
        var mi = new GNS.pro_model_item
        {
            type = (GNS.pro_obj_types)(int)feature.Type,
            id = feature.Id,
            owner = mdl,
        };
        var rc = G.ProFeatureChildrenGet(ref mi, out var arr, out var count);
        if (rc != GNS.ProErrors.PRO_TK_NO_ERROR || arr == IntPtr.Zero || count <= 0)
            return Array.Empty<int>();
        try
        {
            var ids = new int[count];
            Marshal.Copy(arr, ids, 0, count);
            return ids;
        }
        finally { G.ProArrayFree(ref arr); }
    }

    public ElemtreeExtractResult ElemtreeExtract(ItemRef feature)
    {
        if (!TryResolveMdl(feature.Model, out var mdl))
            throw new InvalidOperationException($"模型 {feature.Model.Name} 不在会话中(ProMdlnameInit 失败,需先 Retrieve/Load)");

        var feat = new GNS.pro_model_item
        {
            type = (GNS.pro_obj_types)(int)feature.Type,
            id = feature.Id,
            owner = mdl,
        };
        Check.Eval(nameof(G.ProFeatureInit), G.ProFeatureInit(mdl, feature.Id, ref feat));
        var extractRc = G.ProFeatureElemtreeExtract(
            ref feat,
            IntPtr.Zero,
            GNS.pro_feat_elemtree_extract_opts.PRO_FEAT_EXTRACT_NO_OPTS,
            out var tree);

        // 部分内部特征(如 PRO_FEAT_PATTERN_HEAD)无可抽取元素树,ProFeatureElemtreeExtract 返错。
        // 与 FeatureParentsGet/ChildrenGet 等读操作一致按"查无"回落空树 + trace,不抛断整轮遍历。
        if (extractRc != GNS.ProErrors.PRO_TK_NO_ERROR || tree == IntPtr.Zero)
        {
            if (extractRc != GNS.ProErrors.PRO_TK_NO_ERROR)
                CreoSdkLog.Trace("feature", "elemtree_extract.no_tree",
                    new { id = feature.Id, rc = (int)extractRc });
            var emptyHandle = new CreoElemtreeHandle();
            return new ElemtreeExtractResult(new NativeHandleResource(emptyHandle), Array.Empty<CreoElementNode>());
        }

        var handle = new CreoElemtreeHandle();
        handle.AssignFromExtractor(tree, in feat);

        IReadOnlyList<CreoElementNode> nodes;
        try
        {
            nodes = VisitElemtree<CreoElementNode>(tree, (elem, path) =>
            {
                if (G.ProElementIdGet(elem, out var id) != GNS.ProErrors.PRO_TK_NO_ERROR)
                    return null;

                int level = 0;
                if (path != IntPtr.Zero)
                    G.ProElempathSizeGet(path, out level);

                return new CreoElementNode(id, level);
            });
        }
        catch
        {
            // visit 失败时也经 SafeHandle/DirectReleaser 释放 blob。
            handle.Dispose();
            throw;
        }

        var resource = new NativeHandleResource(handle);
        return new ElemtreeExtractResult(resource, nodes);
    }

    public void ElemtreeWriteXml(ItemRef feature, string xmlPath)
    {
        if (!TryResolveMdl(feature.Model, out var mdl))
            throw new InvalidOperationException($"模型 {feature.Model.Name} 不在会话中");

        var feat = new GNS.pro_model_item
        {
            type = (GNS.pro_obj_types)(int)feature.Type,
            id = feature.Id,
            owner = mdl,
        };
        Check.Eval(nameof(G.ProFeatureInit), G.ProFeatureInit(mdl, feature.Id, ref feat));
        Check.Eval(
            nameof(G.ProFeatureElemtreeExtract),
            G.ProFeatureElemtreeExtract(
                ref feat,
                IntPtr.Zero,
                GNS.pro_feat_elemtree_extract_opts.PRO_FEAT_EXTRACT_NO_OPTS,
                out var tree));

        if (tree == IntPtr.Zero) return;

        try
        {
            Check.Eval(nameof(G.ProElemtreeWrite),
                G.ProElemtreeWrite(tree, GNS.pro_elemtree_type.PRO_ELEMTREE_XML, ToProName(xmlPath, 260)));
        }
        finally
        {
            G.ProFeatureElemtreeFree(ref feat, tree);
        }
    }

    public ElemtreeExtractDeepResult ElemtreeExtractDeep(ItemRef feature)
    {
        if (!TryResolveMdl(feature.Model, out var mdl))
            throw new InvalidOperationException($"模型 {feature.Model.Name} 不在会话中(ProMdlnameInit 失败,需先 Retrieve/Load)");

        var feat = new GNS.pro_model_item
        {
            type = (GNS.pro_obj_types)(int)feature.Type,
            id = feature.Id,
            owner = mdl,
        };
        Check.Eval(nameof(G.ProFeatureInit), G.ProFeatureInit(mdl, feature.Id, ref feat));
        var extractRc = G.ProFeatureElemtreeExtract(
            ref feat,
            IntPtr.Zero,
            GNS.pro_feat_elemtree_extract_opts.PRO_FEAT_EXTRACT_NO_OPTS,
            out var tree);

        // 部分内部特征(如 PRO_FEAT_PATTERN_HEAD)无可抽取元素树,ProFeatureElemtreeExtract 返错。
        // 与 FeatureParentsGet/ChildrenGet 等读操作一致按"查无"回落空树 + trace,不抛断整轮遍历。
        if (extractRc != GNS.ProErrors.PRO_TK_NO_ERROR || tree == IntPtr.Zero)
        {
            if (extractRc != GNS.ProErrors.PRO_TK_NO_ERROR)
                CreoSdkLog.Trace("feature", "elemtree_extract_deep.no_tree",
                    new { id = feature.Id, rc = (int)extractRc });
            var emptyHandle = new CreoElemtreeHandle();
            return new ElemtreeExtractDeepResult(new NativeHandleResource(emptyHandle), Array.Empty<CreoRichElementNode>());
        }

        var handle = new CreoElemtreeHandle();
        handle.AssignFromExtractor(tree, in feat);

        IReadOnlyList<CreoRichElementNode> nodes;
        try
        {
            nodes = VisitElemtree<CreoRichElementNode>(tree, (elem, path) =>
            {
                if (G.ProElementIdGet(elem, out var id) != GNS.ProErrors.PRO_TK_NO_ERROR)
                    return null;

                int level = 0;
                if (path != IntPtr.Zero)
                    G.ProElempathSizeGet(path, out level);

                var vtRc = G.ProElementValuetypeGet(elem, out var valueType);
                if (vtRc != GNS.ProErrors.PRO_TK_NO_ERROR)
                    return new CreoRichElementNode(id, level, CreoElementValueKind.None, 0, 0, null, false);

                switch (valueType)
                {
                    case GNS.pro_value_data_type.PRO_VALUE_TYPE_INT:
                        if (G.ProElementIntegerGet(elem, IntPtr.Zero, out var intVal) == GNS.ProErrors.PRO_TK_NO_ERROR)
                            return new CreoRichElementNode(id, level, CreoElementValueKind.Integer, intVal, 0, null, false);
                        break;
                    case GNS.pro_value_data_type.PRO_VALUE_TYPE_DOUBLE:
                        if (G.ProElementDoubleGet(elem, IntPtr.Zero, out var dblVal) == GNS.ProErrors.PRO_TK_NO_ERROR)
                            return new CreoRichElementNode(id, level, CreoElementValueKind.Double, 0, dblVal, null, false);
                        break;
                    case GNS.pro_value_data_type.PRO_VALUE_TYPE_WSTRING:
                        if (G.ProElementWstringGet(elem, IntPtr.Zero, out var wstrPtr) == GNS.ProErrors.PRO_TK_NO_ERROR
                            && wstrPtr != IntPtr.Zero)
                        {
                            try
                            {
                                var str = Marshal.PtrToStringUni(wstrPtr);
                                return new CreoRichElementNode(id, level, CreoElementValueKind.String, 0, 0, str, false);
                            }
                            finally { G.ProWstringFree(wstrPtr); }
                        }
                        break;
                    case GNS.pro_value_data_type.PRO_VALUE_TYPE_STRING:
                        if (G.ProElementStringGet(elem, IntPtr.Zero, out var strPtr) == GNS.ProErrors.PRO_TK_NO_ERROR
                            && strPtr != IntPtr.Zero)
                        {
                            try
                            {
                                var str = Marshal.PtrToStringAnsi(strPtr);
                                return new CreoRichElementNode(id, level, CreoElementValueKind.String, 0, 0, str, false);
                            }
                            finally { G.ProStringFree(strPtr); }
                        }
                        break;
                    case GNS.pro_value_data_type.PRO_VALUE_TYPE_BOOLEAN:
                        if (G.ProElementBooleanGet(elem, IntPtr.Zero, out var boolVal) == GNS.ProErrors.PRO_TK_NO_ERROR)
                            return new CreoRichElementNode(id, level, CreoElementValueKind.Boolean, 0, 0, null,
                                boolVal == GNS.ProBooleans.PRO_B_TRUE);
                        break;
                    case GNS.pro_value_data_type.PRO_VALUE_TYPE_SELECTION:
                        return new CreoRichElementNode(id, level, CreoElementValueKind.Reference, 0, 0, null, false);
                    case GNS.pro_value_data_type.PRO_VALUE_TYPE_TRANSFORM:
                        return new CreoRichElementNode(id, level, CreoElementValueKind.Transform, 0, 0, null, false);
                }
                return new CreoRichElementNode(id, level, CreoElementValueKind.None, 0, 0, null, false);
            });
        }
        catch
        {
            handle.Dispose();
            throw;
        }

        var resource = new NativeHandleResource(handle);
        return new ElemtreeExtractDeepResult(resource, nodes);
    }
}
