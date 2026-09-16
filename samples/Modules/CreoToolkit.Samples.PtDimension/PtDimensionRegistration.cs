using CreoToolkit.App;
using CreoToolkit.Sdk;
using CreoToolkit.Sdk.Errors;
using System.Globalization;

namespace CreoToolkit.Samples.PtDimension;

/// <summary>
/// TestDimension.c read-only dimension collection (sample migration).
///
/// C source:
///   pt_examples/pt_dbase/TestDimension.c ProTestSolidDimensionsCollect (L1542-L1574):
///     ProUtilCollectDimension((ProMdl)solid, is_refdim, pp_dims) + ProArraySizeGet.
///   ProTestDimDisplayAct (L1264-L1289):
///     collect ordinary dimensions, then optionally collect ref dimensions and append.
///
/// .NET implementation uses existing L3 API only: CreoSolid.ListDimensions(includeRefDimensions).
/// </summary>
public static class PtDimensionRegistration
{
    public static void Register(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        ThrowUtil.IfNull(app);
        ThrowUtil.IfNullOrWhiteSpace(modelName);
        RegisterPtDimensionList(app, modelName, modelType);
        RegisterPtDimensionListWithRefdims(app, modelName, modelType);
        RegisterPtDimensionValues(app, modelName, modelType);
        RegisterPtDimensionDrawingList(app, modelName, modelType);
        RegisterPtDimensionAttachments(app, modelName, modelType);
        RegisterPtDrawingDimensionAttachments(app, modelName, modelType);
        RegisterPtDrawingDimensionCreateRoundtrip(app, modelName, modelType);
        RegisterPtDrawingDimensionOrdinateRoundtrip(app, modelName, modelType);
        PtGtolRegistration.Register(app, modelName, modelType);
    }

    private static void RegisterPtDrawingDimensionCreateRoundtrip(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        // CreoDrawing.CreateDimension → ProDrawingDimensionCreate 写路径 roundtrip:
        // datum point→csys 建竖直尺寸(官方范式 UgDrawingDimensions.c)→ GetDimensionAttachments
        // 读回(create 出的尺寸天然 attached,解锁读 API 有值路径)→ DeleteDimension 收尾还原 fixture。
        app.Command("pt.dimension.drawing-create-roundtrip", "PT_DIMENSION_DRAWING_CREATE_ROUNDTRIP",
            "PT_DIMENSION_DRAWING_CREATE_ROUNDTRIP_HELP", ctx =>
            {
                if (!ctx.RequireSession(out var session)) return;
                if (!ctx.TryRetrieveModel(modelName, modelType, out var model)) return;
                if (model is not CreoDrawing drawing)
                {
                    ctx.Messages.Info($"model {model.Name} is not a drawing (type={model.Type}), cannot create dimension");
                    ctx.Messages.Info("drawing-create-roundtrip complete");
                    return;
                }

                try
                {
                    // 1. 定位 view 与关联 solid
                    var viewIds = drawing.ListViewIds();
                    var solid = drawing.GetCurrentSolid();
                    if (viewIds.Count == 0 || solid is null)
                    {
                        ctx.Messages.Info($"drawing-create-roundtrip on {drawing.FullName}: views={viewIds.Count}; solid={(solid is null ? "none" : solid.Name)} (fixture unsupported)");
                        ctx.Messages.Info("drawing-create-roundtrip complete");
                        return;
                    }

                    // 2. attach 定位:solid 内首个 datum point + 首个 csys(官方示例同款配对)
                    var point = session.Features.List(solid)
                        .SelectMany(f => f.ListGeomItems(CreoModelItemType.Point))
                        .OfType<CreoPoint>()
                        .FirstOrDefault();
                    var csys = solid.ListCsys().OfType<CreoCsys>().FirstOrDefault();
                    if (point is null || csys is null)
                    {
                        ctx.Messages.Info($"drawing-create-roundtrip on {drawing.FullName}: point={(point is null ? "none" : $"#{point.Id}")}; csys={(csys is null ? "none" : $"#{csys.Id}")} (fixture unsupported)");
                        ctx.Messages.Info("drawing-create-roundtrip complete");
                        return;
                    }

                    // 3. create:竖直尺寸(sense=PNT/CENTER;orient VERT=1/NONE=0,枚举值核自
                    //    ProDimension.Enums.g.cs + ProPoint.Enums.g.cs)
                    var senses = new[]
                    {
                        new DimSense(Type: 1, Sense: 3, OrientHint: 1), // point: PNT/CENTER/VERT
                        new DimSense(Type: 1, Sense: 3, OrientHint: 0), // csys:  PNT/CENTER/NONE
                    };
                    var created = drawing.CreateDimension(
                        viewIds[0], new CreoModelItem[] { point, csys }, senses,
                        orientHint: 1, location: new CreoVec3(100.0, 100.0, 0.0));
                    if (created is null)
                    {
                        ctx.Messages.Info($"drawing-create-roundtrip on {drawing.FullName}: create returned null (view #{viewIds[0]} unresolved)");
                        ctx.Messages.Info("drawing-create-roundtrip complete");
                        return;
                    }

                    // 4. 读回:create 出的尺寸天然 attached → 有值路径
                    var readBack = created is CreoDimension dim ? drawing.GetDimensionAttachments(dim) : null;
                    var readTag = readBack is null
                        ? "readback=null"
                        : $"readback pairs={readBack.AttachmentCount}; senses={readBack.Senses.Count}; orient={readBack.OrientHint}";

                    // 5. delete 收尾还原 fixture(单独容错:失败不吞 create/readback 成果)
                    string deleteTag;
                    try
                    {
                        drawing.DeleteDimension(created);
                        deleteTag = "deleted";
                    }
                    catch (CreoException ex)
                    {
                        deleteTag = $"deleteFailed rc={ex.ErrorCode}";
                    }

                    ctx.Messages.Info(
                        $"drawing-create-roundtrip on {drawing.FullName}: created dim#{created.Id} type={created.Type} " +
                        $"owner={created.OwnerModelName} attach=point#{point.Id}+csys#{csys.Id} view#{viewIds[0]}; {readTag}; {deleteTag}");
                    ctx.Messages.Info("drawing-create-roundtrip complete");
                }
                catch (CreoException ex)
                {
                    ctx.Messages.Info($"drawing-create-roundtrip failed on {drawing.FullName}: rc={ex.ErrorCode} {ex.Message}");
                }
            });
    }

    private static void RegisterPtDrawingDimensionOrdinateRoundtrip(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        // ConvertToOrdinateBaseline + ConvertToOrdinate + GetOrdinateInfo:
        // UgDrawingDimensions.c 后半段范式。建两条竖直线性尺寸(不同 location) →
        // dim1 转 ordinate + baseline(nearAttachPoint 用 csys 原点,3D solid 坐标,
        // 基线端=csys,与官方例数据点表全部以坐标系为零点的语义一致)→
        // dim2 用 baseline 转 ordinate → 读回 dim2 自证 baseline id 匹配。
        // baseline 语义(tkuse 59528 用 "identify"、Creo 4 头文件无
        // ProDrawingOrdbaselineDelete):baseline 是 dim1 承担 baseline 角色的引用句柄,
        // 不是独立 dim entity。ConvertToLinear(dim1) 消解 baseline 概念,baseline handle
        // 随之悬空。清尾只需删 dim1/dim2 两个真 entity,baseline 由 handle 生命周期
        // 自然结束,不需(也不能)显式删除。
        // 探路 tag(baselineToLinear):对 baseline handle 试调 ConvertToLinear 采事实,
        // 不影响清尾。
        // 清理走 finally:创建即登记(仅 dim1/dim2),转换/读回抛异常也不留污染;逐项 try/catch 打印结果。
        app.Command("pt.dimension.drawing-ordinate-roundtrip", "PT_DIMENSION_DRAWING_ORDINATE_ROUNDTRIP",
            "PT_DIMENSION_DRAWING_ORDINATE_ROUNDTRIP_HELP", ctx =>
            {
                if (!ctx.RequireSession(out var session)) return;
                if (!ctx.TryRetrieveModel(modelName, modelType, out var model)) return;
                if (model is not CreoDrawing drawing)
                {
                    ctx.Messages.Info($"model {model.Name} is not a drawing (type={model.Type}), cannot convert to ordinate");
                    ctx.Messages.Info("drawing-ordinate-roundtrip complete");
                    return;
                }

                // 创建即登记(仅 dim1/dim2 真 entity;baseline handle 不进清尾链,见头注释);
                // finally 逆序清理(dim2→dim1)
                var created = new List<(string Name, CreoModelItem Item)>();
                try
                {
                    var viewIds = drawing.ListViewIds();
                    var solid = drawing.GetCurrentSolid();
                    if (viewIds.Count == 0 || solid is null)
                    {
                        ctx.Messages.Info($"drawing-ordinate-roundtrip on {drawing.FullName}: views={viewIds.Count}; solid={(solid is null ? "none" : solid.Name)} (fixture unsupported)");
                        return;
                    }

                    var point = session.Features.List(solid)
                        .SelectMany(f => f.ListGeomItems(CreoModelItemType.Point))
                        .OfType<CreoPoint>()
                        .FirstOrDefault();
                    var csys = solid.ListCsys().OfType<CreoCsys>().FirstOrDefault();
                    if (point is null || csys is null)
                    {
                        ctx.Messages.Info($"drawing-ordinate-roundtrip on {drawing.FullName}: point={(point is null ? "none" : $"#{point.Id}")}; csys={(csys is null ? "none" : $"#{csys.Id}")} (fixture unsupported)");
                        return;
                    }

                    // nearAttachPoint = csys 原点(3D solid 坐标,矩阵第 4 行平移分量):
                    // 选 csys 端作 baseline,不许硬编码
                    var matrix = csys.GetMatrix();
                    if (matrix is null)
                    {
                        ctx.Messages.Info($"drawing-ordinate-roundtrip on {drawing.FullName}: csys#{csys.Id} matrix unreadable (fixture unsupported)");
                        return;
                    }
                    var csysOrigin = new CreoVec3(matrix.Value.M30, matrix.Value.M31, matrix.Value.M32);

                    // 两条竖直线性尺寸,同 attach 换 location(fixture 只有一个 datum point,与前置
                    // create-roundtrip 一致的 sense/orient 组合)
                    var senses = new[]
                    {
                        new DimSense(Type: 1, Sense: 3, OrientHint: 1),
                        new DimSense(Type: 1, Sense: 3, OrientHint: 0),
                    };
                    var attach = new CreoModelItem[] { point, csys };

                    var dim1 = drawing.CreateDimension(
                        viewIds[0], attach, senses, orientHint: 1,
                        location: new CreoVec3(100.0, 100.0, 0.0));
                    if (dim1 is null)
                    {
                        ctx.Messages.Info($"drawing-ordinate-roundtrip on {drawing.FullName}: create dim1 returned null (view #{viewIds[0]} unresolved)");
                        return;
                    }
                    created.Add(("dim1", dim1));
                    var dim2 = drawing.CreateDimension(
                        viewIds[0], attach, senses, orientHint: 1,
                        location: new CreoVec3(150.0, 100.0, 0.0));
                    if (dim2 is null)
                    {
                        ctx.Messages.Info($"drawing-ordinate-roundtrip on {drawing.FullName}: create dim2 returned null");
                        return; // dim1 由 finally 清理
                    }
                    created.Add(("dim2", dim2));

                    // 1. dim1 就地转 ordinate + baseline(基线端=csys);baseline handle 不进 created
                    // (它只是标识 dim1 承担 baseline 角色的引用,不是独立 entity)
                    var baseline = drawing.ConvertToOrdinateBaseline(dim1, csysOrigin);
                    var baselineTag = baseline is null ? "baseline=null" : $"baseline dim#{baseline.Id}";
                    var baselineIsDim1 = baseline is not null && baseline.Equals(dim1);

                    // 2. dim2 用 baseline 转 ordinate + 3. 读回自证(baseline id 必须与创建返回一致)
                    string ordinateTag = "ordinate=n/a", matchTag = "baselineMatch=False";
                    string linearRestoredTag = "linearRestored=n/a";
                    string linearRestoredDim1Tag = "linearRestoredDim1=n/a";
                    if (baseline is not null)
                    {
                        drawing.ConvertToOrdinate(dim2, baseline);
                        var info = drawing.GetOrdinateInfo(dim2);
                        if (info is null)
                            ordinateTag = "ordinate=null";
                        else
                        {
                            ordinateTag = $"ordinate={info.Value.IsOrdinate} baseline#{info.Value.Baseline?.Id.ToString() ?? "none"}";
                            if (info.Value.IsOrdinate && info.Value.Baseline is not null
                                && info.Value.Baseline.Id == baseline.Id)
                            {
                                matchTag = "baselineMatch=True";
                                // 4. ConvertToLinear 反转 dim2 → 读回 (false, null) 自证
                                //    (tkuse 59537:converts an existing ordinate dimension to linear)
                                try
                                {
                                    drawing.ConvertToLinear(dim2);
                                    var linearInfo = drawing.GetOrdinateInfo(dim2);
                                    var restored = linearInfo is not null
                                        && !linearInfo.Value.IsOrdinate
                                        && linearInfo.Value.Baseline is null;
                                    linearRestoredTag = $"linearRestored={restored}";
                                }
                                catch (CreoException ex)
                                {
                                    linearRestoredTag = $"linearRestored=rc:{ex.ErrorCode}";
                                }
                                // 5. 反转 dim1(它承担 baseline 角色,§9.14):ConvertToLinear
                                //    消解 baseline 概念,dim1 变回普通线性尺寸,baseline handle 悬空。
                                try
                                {
                                    drawing.ConvertToLinear(dim1);
                                    var linearInfoDim1 = drawing.GetOrdinateInfo(dim1);
                                    var restoredDim1 = linearInfoDim1 is not null
                                        && !linearInfoDim1.Value.IsOrdinate
                                        && linearInfoDim1.Value.Baseline is null;
                                    linearRestoredDim1Tag = $"linearRestoredDim1={restoredDim1}";
                                }
                                catch (CreoException ex)
                                {
                                    linearRestoredDim1Tag = $"linearRestoredDim1=rc:{ex.ErrorCode}";
                                }
                            }
                        }
                    }

                    // 探路 tag:baseline handle 是否接受写路径调用;不影响清尾
                    string baselineToLinearTag = "baselineToLinear=n/a";
                    if (baseline is not null)
                    {
                        try
                        {
                            drawing.ConvertToLinear(baseline);
                            baselineToLinearTag = "baselineToLinear=True";
                        }
                        catch (CreoException ex)
                        {
                            baselineToLinearTag = $"baselineToLinear=rc:{ex.ErrorCode}";
                        }
                    }

                    ctx.Messages.Info(
                        $"drawing-ordinate-roundtrip on {drawing.FullName}: dim1#{dim1.Id} dim2#{dim2.Id}; " +
                        $"{baselineTag}; baselineIsDim1={baselineIsDim1}; {ordinateTag}; {matchTag}; " +
                        $"{linearRestoredTag}; {linearRestoredDim1Tag}; {baselineToLinearTag}");
                }
                catch (CreoException ex)
                {
                    ctx.Messages.Info($"drawing-ordinate-roundtrip failed on {drawing.FullName}: rc={ex.ErrorCode} {ex.Message}");
                }
                finally
                {
                    if (created.Count > 0)
                    {
                        // 逆序清理 dim2/dim1(baseline handle 不在 created,见头注释);
                        // 保留 (owner,type,id) 去重作防御性收敛;
                        // 逐项容错,删除结果全部打印(真机事实收集,失败不算命令失败)
                        var tags = new List<string>();
                        var seen = new HashSet<(string, CreoModelItemType, int)>();
                        for (int i = created.Count - 1; i >= 0; i--)
                        {
                            var (name, item) = created[i];
                            if (!seen.Add((item.OwnerModelName, item.Type, item.Id)))
                            {
                                tags.Add($"{name}=skip(dup)");
                                continue;
                            }
                            try
                            {
                                drawing.DeleteDimension(item);
                                tags.Add($"{name}=ok");
                            }
                            catch (CreoException ex)
                            {
                                tags.Add($"{name}=rc:{ex.ErrorCode}");
                            }
                        }
                        ctx.Messages.Info($"drawing-ordinate-roundtrip deleted {string.Join(" ", tags)}; cleanup done");
                    }
                    ctx.Messages.Info("drawing-ordinate-roundtrip complete");
                }
            });
    }

    private static void RegisterPtDrawingDimensionAttachments(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        // CreoDrawing.GetDimensionAttachments → ProDrawingDimAttachpointsGet:
        // 读回图纸内每个尺寸的 attach pair 数 + senses。图纸尺寸该 API 无 annotation plane
        // 出参,故 wrapper 的 AnnotationPlane 总为 null。
        app.Command("pt.dimension.drawing-attachments", "PT_DIMENSION_DRAWING_ATTACHMENTS",
            "PT_DIMENSION_DRAWING_ATTACHMENTS_HELP", ctx =>
            {
                // 不用 Current():drawing load 后 Current 仍可能 (none)(memory
                // mdlcurrentget-requires-open-window),显式 retrieve 名字对齐的模型。
                if (!ctx.TryRetrieveModel(modelName, modelType, out var model)) return;
                if (model is not CreoDrawing drawing)
                {
                    ctx.Messages.Info($"model {model.Name} is not a drawing (type={model.Type}), cannot dump drawing dimension attachments");
                    return;
                }

                try
                {
                    var dims = drawing.ListDimensions().OfType<CreoDimension>().ToList();
                    if (dims.Count == 0)
                    {
                        ctx.Messages.Info($"drawing-dimension-attachments on {drawing.FullName}: dims=0");
                        ctx.Messages.Info("drawing-dimension-attachments complete");
                        return;
                    }

                    int empty = 0, badInputs = 0, totalPairs = 0;
                    var preview = new System.Collections.Generic.List<string>();
                    foreach (var dim in dims)
                    {
                        DimensionAttachments? info;
                        try
                        {
                            info = drawing.GetDimensionAttachments(dim);
                        }
                        catch (CreoException)
                        {
                            badInputs++;
                            continue;
                        }
                        if (info is null)
                        {
                            empty++;
                            continue;
                        }
                        totalPairs += info.AttachmentCount;
                        if (preview.Count < 10)
                            preview.Add($"dim#{dim.Id}:pairs={info.AttachmentCount}:senses={info.Senses.Count}");
                    }

                    ctx.Messages.Info(
                        $"drawing-dimension-attachments on {drawing.FullName}: dims={dims.Count}; " +
                        $"empty={empty}; badInputs={badInputs}; totalPairs={totalPairs}; " +
                        string.Join(", ", preview));
                    ctx.Messages.Info("drawing-dimension-attachments complete");
                }
                catch (CreoException ex)
                {
                    ctx.Messages.Info($"drawing-dimension-attachments failed on {drawing.FullName}: rc={ex.ErrorCode} {ex.Message}");
                }
            });
    }

    private static void RegisterPtDimensionAttachments(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        // CreoDimension.GetAttachments → ProDimensionAttachmentsGet:读回每个 solid dimension 的
        // annotation plane / attach pair 数 / senses,验证 heap-out ProArray 家族真机路径。
        app.Command("pt.dimension.attachments", "PT_DIMENSION_ATTACHMENTS",
            "PT_DIMENSION_ATTACHMENTS_HELP", ctx =>
            {
                if (!ctx.TryRetrieveModel(modelName, modelType, out var model)) return;
                if (model is not CreoSolid solid)
                {
                    ctx.Messages.Info($"model {modelName} is not a solid, cannot dump dimension attachments");
                    return;
                }

                try
                {
                    var dims = solid.ListDimensions(includeRefDimensions: false).OfType<CreoDimension>().ToList();
                    if (dims.Count == 0)
                    {
                        ctx.Messages.Info($"no dimensions on {model.FullName}");
                        return;
                    }

                    // driving(未 attach)尺寸不适用此 API,Bridge 已把 INVALID_ITEM/BAD_INPUTS
                    // 归 null——notApplicable 计数;errors 只剩真异常。
                    int withPlane = 0, notApplicable = 0, errors = 0, totalPairs = 0;
                    var preview = new System.Collections.Generic.List<string>();
                    foreach (var dim in dims)
                    {
                        DimensionAttachments? info;
                        try
                        {
                            info = dim.GetAttachments();
                        }
                        catch (CreoException)
                        {
                            errors++;
                            continue;
                        }
                        if (info is null)
                        {
                            notApplicable++;
                            continue;
                        }
                        totalPairs += info.AttachmentCount;
                        if (info.AnnotationPlane is not null) withPlane++;
                        if (preview.Count < 10)
                        {
                            var planeTag = info.AnnotationPlane is { } p ? $"{p.Type}#{p.Id}" : "none";
                            preview.Add($"dim#{dim.Id}:pairs={info.AttachmentCount}:senses={info.Senses.Count}:plane={planeTag}");
                        }
                    }

                    ctx.Messages.Info(
                        $"dimension-attachments on {model.FullName}: dims={dims.Count}; " +
                        $"withPlane={withPlane}; notApplicable={notApplicable}; errors={errors}; totalPairs={totalPairs}; " +
                        string.Join(", ", preview));
                    ctx.Messages.Info("dimension-attachments complete");
                }
                catch (CreoException ex)
                {
                    ctx.Messages.Info($"dimension-attachments failed on {model.FullName}: rc={ex.ErrorCode} {ex.Message}");
                }
            });
    }

    private static void RegisterPtDimensionList(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        // TestDimension.c L1561: ProUtilCollectDimension((ProMdl)solid, PRO_B_FALSE, pp_dims).
        app.Command("pt.dimension.list", "PT_DIMENSION_LIST", "PT_DIMENSION_LIST_HELP", ctx =>
        {
            if (!ctx.TryRetrieveModel(modelName, modelType, out var model)) return;
            if (model is not CreoSolid solid)
            {
                ctx.Messages.Info($"model {modelName} is not a solid, cannot list dimensions");
                return;
            }

            var dimensions = solid.ListDimensions();
            ctx.Messages.Info(
                $"list-dimensions on {model.FullName}: count={dimensions.Count}" +
                FormatDimensionPreview(dimensions));
        });
    }

    private static void RegisterPtDimensionListWithRefdims(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        // TestDimension.c L1277-L1289: ordinary dimensions + ref dimensions merged for display.
        app.Command("pt.dimension.list-with-refdims", "PT_DIMENSION_LIST_WITH_REFDIMS",
            "PT_DIMENSION_LIST_WITH_REFDIMS_HELP", ctx =>
            {
                if (!ctx.TryRetrieveModel(modelName, modelType, out var model)) return;
                if (model is not CreoSolid solid)
                {
                    ctx.Messages.Info($"model {modelName} is not a solid, cannot list dimensions");
                    return;
                }

                var dimensions = solid.ListDimensions(includeRefDimensions: true);
                var normalCount = dimensions.OfType<CreoDimension>().Count();
                var refCount = dimensions.OfType<CreoRefDimension>().Count();

                ctx.Messages.Info(
                    $"list-dimensions-with-refdims on {model.FullName}: " +
                    $"count={dimensions.Count}; dimensions={normalCount}; refdims={refCount}" +
                    FormatDimensionPreview(dimensions));
            });
    }

    private static void RegisterPtDimensionValues(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        app.Command("pt.dimension.values", "PT_DIMENSION_VALUES", "PT_DIMENSION_VALUES_HELP", ctx =>
        {
            if (!ctx.TryRetrieveModel(modelName, modelType, out var model)) return;
            if (model is not CreoSolid solid)
            {
                ctx.Messages.Info($"model {modelName} is not a solid, cannot list dimension values");
                return;
            }

            try
            {
                var dimensions = solid.ListDimensions(includeRefDimensions: false);
                if (dimensions.Count == 0)
                {
                    ctx.Messages.Info($"no dimensions on {model.Name}");
                    return;
                }

                var preview = dimensions
                    .Take(20)
                    .Select(FormatDimensionValueItem)
                    .ToList();
                ctx.Messages.Info(
                    $"dimension values on {model.FullName}: count={dimensions.Count}; " +
                    string.Join(", ", preview));
            }
            catch (CreoException ex)
            {
                ctx.Messages.Info($"dimension values failed on {model.FullName}: rc={ex.ErrorCode} {ex.Message}");
            }
        });
    }

    private static void RegisterPtDimensionDrawingList(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        // TestDimension.c L1318-L1329 handles selected drawing dimensions; this is the read-only list equivalent.
        app.Command("pt.dimension.drawing-list", "PT_DIMENSION_DRAWING_LIST",
            "PT_DIMENSION_DRAWING_LIST_HELP", ctx =>
            {
                if (!ctx.RequireSession(out var session)) return;

                var model = session.Models.GetCurrent();
                if (model is not CreoDrawing drawing)
                {
                    ctx.Messages.Info($"model {model?.Name ?? modelName} is not a drawing, cannot list dimensions");
                    return;
                }

                var dimensions = drawing.ListDimensions();
                ctx.Messages.Info(
                    $"list-drawing-dimensions on {model.FullName}: count={dimensions.Count}" +
                    FormatDimensionPreview(dimensions));
            });

    }

    private static string FormatDimensionPreview(IReadOnlyList<ICreoModelItem> dimensions)
    {
        var preview = dimensions
            .Take(10)
            .Select(FormatDimensionItem)
            .ToList();

        return preview.Count > 0 ? "; " + string.Join(", ", preview) : "";
    }

    private static string FormatDimensionItem(ICreoModelItem item)
    {
        var name = item.GetName() ?? "(no-name)";
        return item switch
        {
            CreoDimension dim => $"dim#{dim.Id}:{name}",
            CreoRefDimension refDim => $"refdim#{refDim.Id}:{name}",
            _ => $"{item.Type}:{name}",
        };
    }

    private static string FormatDimensionValueItem(ICreoModelItem item)
    {
        static string F(double? value) => value is { } v ? v.ToString("F4", CultureInfo.InvariantCulture) : "null";
        static string Q(string? value) => value is null ? "null" : $"\"{value}\"";

        return item switch
        {
            CreoDimension dim => $"dim#{dim.Id}:value={F(dim.GetValue())}:typeCode={dim.GetTypeCode()}:text={Q(dim.GetDisplayText())}",
            CreoRefDimension refDim => $"refdim#{refDim.Id}:value={F(refDim.GetValue())}:typeCode={refDim.GetTypeCode()}:text={Q(refDim.GetDisplayText())}",
            _ => $"{item.Type}:value=null:typeCode=null:text=null",
        };
    }
}
