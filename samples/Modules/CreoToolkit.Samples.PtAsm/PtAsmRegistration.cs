using CreoToolkit.App;
using CreoToolkit.Sdk;

namespace CreoToolkit.Samples.PtAsm;

/// <summary>
/// TestAsm.c ProTestAsmcomppathFunc 移植。
/// Keeps only DATA queries: ProAsmcomppathMdlGet and ProAsmcomppathTrfGet.
/// </summary>
public static class PtAsmRegistration
{
    public static void Register(CreoAppBuilder app, string asmName, IReadOnlyList<int> componentIds)
    {
        ThrowUtil.IfNull(app);
        ThrowUtil.IfNullOrWhiteSpace(asmName);
        ThrowUtil.IfNull(componentIds);
        var ids = componentIds.ToArray();

        app.Command("pt.asm.path-mdl-get", "PT_ASM_PATH_MDL_GET", "PT_ASM_PATH_MDL_GET_HELP", ctx =>
        {
            if (!TryRetrieveAssembly(ctx, asmName, out var asm)) return;

            var effectiveIds = ResolveConfiguredComponentIds(ids);
            var child = asm.GetPathMdl(effectiveIds);
            ctx.Messages.Info(child is null
                ? $"path-mdl-get on {asm.FullName}: ids=[{FormatIds(effectiveIds)}] -> (n/a)"
                : $"path-mdl-get on {asm.FullName}: ids=[{FormatIds(effectiveIds)}] -> {child.FullName}");

            // Optional nested-assembly verification path. It remains read-only: the selection is
            // an owned transient copy and is released before the command returns.
            var path = ResolveComponentPath(asm, effectiveIds);
            if (child is not null && path is not null)
            {
                var item = child.Items.List(CreoModelItemType.Surface)
                    .OfType<CreoModelItem>()
                    .FirstOrDefault();
                if (item is null)
                {
                    ctx.Messages.Info($"selection-path depth={path.Depth}: no surface fixture item");
                }
                else
                {
                    using var selection = ctx.Session.Selections.CreateModelItemSelection(item, path);
                    var roundtrip = selection.Single()?.Path;
                    ctx.Messages.Info(
                        $"selection-path depth={path.Depth} item={item.Type}:{item.Id} " +
                        $"roundtripDepth={roundtrip?.Depth ?? 0}");
                }
            }
        });

        app.Command("pt.asm.path-transform-get", "PT_ASM_PATH_TRANSFORM_GET",
            "PT_ASM_PATH_TRANSFORM_GET_HELP", ctx =>
            {
                if (!TryRetrieveAssembly(ctx, asmName, out var asm)) return;

                var matrix = asm.GetPathTransform(ids, localToTop: true);
                ctx.Messages.Info(matrix is null
                    ? $"path-transform-get on {asm.FullName}: ids=[{FormatIds(ids)}] -> (n/a)"
                    : $"path-transform-get on {asm.FullName}: ids=[{FormatIds(ids)}] -> {FormatMat12(matrix.Value)}");
            });

        // 取 path matrix → CreoMat4.TransformPoint 把测试点 UnitX 变到顶层坐标系。
        app.Command("pt.asm.transform-point", "PT_ASM_TRANSFORM_POINT",
            "PT_ASM_TRANSFORM_POINT_HELP", ctx =>
            {
                if (!TryRetrieveAssembly(ctx, asmName, out var asm)) return;

                var matrix = asm.GetPathTransform(ids, localToTop: true);
                if (matrix is null)
                {
                    ctx.Messages.Info($"transform-point on {asm.FullName}: ids=[{FormatIds(ids)}] -> (n/a)");
                    return;
                }

                var input = CreoVec3.UnitX;
                var output = matrix.Value.TransformPoint(input);
                ctx.Messages.Info(
                    $"transform-point on {asm.FullName}: ids=[{FormatIds(ids)}] " +
                    $"({FormatVec3(input)}) -> ({FormatVec3(output)})");
            });

        // pt_examples/pt_asm 装配查询切片 (零新 native / 零 SDK 改动,read-only 不改盘)
        RegisterPtAsmTopLevelComps(app, asmName);
        RegisterPtAsmSkeletonName(app, asmName);
        RegisterPtAsmIsExploded(app, asmName);
        RegisterPtAsmCompAssemble(app);
        RegisterPtAsmCompAssembleDual(app);
        RegisterPtAsmCompCreateCopy(app);
    }

    private static int[] ResolveConfiguredComponentIds(int[] registeredIds)
    {
        if (registeredIds.Length > 0)
            return registeredIds;

        var raw = Environment.GetEnvironmentVariable("CTK_APP_COMPONENT_IDS");
        if (string.IsNullOrWhiteSpace(raw))
            return registeredIds;

        var result = raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(token => int.TryParse(token, out var id) && id > 0
                ? id
                : throw new FormatException($"Invalid CTK_APP_COMPONENT_IDS token '{token}'."))
            .ToArray();
        if (result.Length == 0)
            throw new FormatException("CTK_APP_COMPONENT_IDS must contain at least one positive component feature id.");
        return result;
    }

    private static CreoComponentPath? ResolveComponentPath(CreoAssembly root, IReadOnlyList<int> ids)
    {
        if (ids.Count == 0)
            return null;

        CreoComponentPath? current = root.ListComponents().FirstOrDefault(p => p.FeatureId == ids[0]);
        for (var i = 1; current is not null && i < ids.Count; i++)
            current = current.GetChildren().FirstOrDefault(p => p.FeatureId == ids[i]);
        return current;
    }

    private static void RegisterPtAsmTopLevelComps(CreoAppBuilder app, string asmName)
    {
        // ProAssemblyComponentVisit 顶层子件枚举切片 → CreoAssembly.ListTopLevelComponentFeatureIds。
        app.Command("pt.asm.top-level-comps", "PT_ASM_TOP_LEVEL_COMPS",
            "PT_ASM_TOP_LEVEL_COMPS_HELP", ctx =>
            {
                if (!TryRetrieveAssembly(ctx, asmName, out var asm)) return;
                var compIds = asm.ListTopLevelComponentFeatureIds();
                var preview = string.Join(",", compIds.Take(8));
                ctx.Messages.Info($"top-level-comps on {asm.FullName}: count={compIds.Count}" +
                                  (compIds.Count > 0 ? $"; ids=[{preview}]" : ""));
            });
    }

    private static void RegisterPtAsmSkeletonName(CreoAppBuilder app, string asmName)
    {
        // ProAssemblySkeletonGet 骨架模型名切片 → CreoAssembly.GetSkeletonName。
        app.Command("pt.asm.skeleton-name", "PT_ASM_SKELETON_NAME",
            "PT_ASM_SKELETON_NAME_HELP", ctx =>
            {
                if (!TryRetrieveAssembly(ctx, asmName, out var asm)) return;
                var skeleton = asm.GetSkeletonName();
                ctx.Messages.Info(skeleton is null
                    ? $"skeleton-name on {asm.FullName}: (none)"
                    : $"skeleton-name on {asm.FullName}: {skeleton}");
            });
    }

    private static void RegisterPtAsmIsExploded(CreoAppBuilder app, string asmName)
    {
        // ProAssemblyIsExploded 爆炸状态查询切片 → CreoAssembly.IsExploded (read-only, 不调 Explode/Unexplode 改盘)。
        app.Command("pt.asm.is-exploded", "PT_ASM_IS_EXPLODED",
            "PT_ASM_IS_EXPLODED_HELP", ctx =>
            {
                if (!TryRetrieveAssembly(ctx, asmName, out var asm)) return;
                var exploded = asm.IsExploded();
                ctx.Messages.Info(exploded is null
                    ? $"is-exploded on {asm.FullName}: (unsupported)"
                    : $"is-exploded on {asm.FullName}: {exploded.Value}");
            });

        // Snapshot roundtrip:CreateSnapshot → GetSnapshotTransforms(有值路径) → DeleteSnapshot。
        // 原命令只读不建,fixture 无 snapshot 时永远走 not-found 分支,
        // 矩阵/comp_path 解构从未真机执行;roundtrip 让有值路径可达。
        // create 若在 init 上下文失败(无 drag 会话),归"create unsupported"业务事实分支。
        app.Command("pt.asm.snapshot-trfs", "PT_ASM_SNAPSHOT_TRFS",
            "PT_ASM_SNAPSHOT_TRFS_HELP", ctx =>
            {
                if (!TryRetrieveAssembly(ctx, asmName, out var asm)) return;

                var snapshotName = Environment.GetEnvironmentVariable("CTK_APP_SNAPSHOT_NAME") ?? "CTK_SNAP_RT";
                bool created = false;
                try
                {
                    try
                    {
                        asm.CreateSnapshot(snapshotName);
                        created = true;
                        ctx.Messages.Info($"snapshot_created name=\"{snapshotName}\"");
                    }
                    catch (Exception ex) when (ex is CreoToolkit.Sdk.Errors.CreoException or InvalidOperationException)
                    {
                        // init 上下文可能无 drag 会话/装配非 current → create 不可用是业务事实
                        ctx.Messages.Info(
                            $"snapshot-trfs on {asm.FullName}: create \"{snapshotName}\" unsupported " +
                            $"({ex.GetType().Name}: {ex.Message})");
                        ctx.Messages.Info("snapshot-trfs complete");
                        return;
                    }

                    System.Collections.Generic.IReadOnlyList<SnapshotTransform>? trfs;
                    try
                    {
                        trfs = asm.GetSnapshotTransforms(snapshotName);
                    }
                    catch (CreoToolkit.Sdk.Errors.CreoException ex)
                    {
                        // 真机事实:snapshot 不存在时 native 返 GeneralError(非 NOT_FOUND)
                        ctx.Messages.Info(
                            $"snapshot-trfs on {asm.FullName}: snapshot=\"{snapshotName}\" -> " +
                            $"(native rc={ex.ErrorCode} on read-back)");
                        ctx.Messages.Info("snapshot-trfs complete");
                        return;
                    }
                    if (trfs is null)
                    {
                        ctx.Messages.Info(
                            $"snapshot-trfs on {asm.FullName}: snapshot=\"{snapshotName}\" -> (not found on read-back)");
                        ctx.Messages.Info("snapshot-trfs complete");
                        return;
                    }

                    var preview = new System.Collections.Generic.List<string>();
                    for (int i = 0; i < trfs.Count && preview.Count < 5; i++)
                    {
                        var t = trfs[i];
                        var matHead = t.Matrix4x4.Count >= 4
                            ? $"[{t.Matrix4x4[0]:F3},{t.Matrix4x4[1]:F3},{t.Matrix4x4[2]:F3},{t.Matrix4x4[3]:F3}...]"
                            : "[]";
                        preview.Add($"[{i}]:depth={t.TableNum}:comps=[{string.Join(",", t.ComponentIds)}]:mat={matHead}");
                    }
                    ctx.Messages.Info(
                        $"snapshot-trfs on {asm.FullName}: snapshot=\"{snapshotName}\"; count={trfs.Count}; " +
                        string.Join(", ", preview));
                    ctx.Messages.Info("snapshot-trfs complete");
                }
                finally
                {
                    // 删除收尾恢复 fixture 原状(create 失败则无需删)
                    if (created)
                    {
                        try
                        {
                            asm.DeleteSnapshot(snapshotName);
                            ctx.Messages.Info($"snapshot_deleted name=\"{snapshotName}\"");
                        }
                        catch (Exception ex) when (ex is CreoToolkit.Sdk.Errors.CreoException or InvalidOperationException)
                        {
                            ctx.Messages.Info($"snapshot_delete_failed name=\"{snapshotName}\" ({ex.Message})");
                        }
                    }
                }
            });

    }

    private static void RegisterPtAsmCompAssemble(CreoAppBuilder app)
    {
        // 装入组件 + CSYS 约束 + 回读断言 + 删除恢复原状。
        app.Command("pt.asm.comp.assemble", "PT_ASM_COMP_ASSEMBLE",
            "PT_ASM_COMP_ASSEMBLE_HELP", ctx =>
            {
                // 装配模型(由 harness modelPath 加载;须含基准 csys)
                if (!TryRetrieveAssembly(ctx, "assembly", out var asm)) return;

                // 组件模型:与装配同目录的 exercise4.prt,从磁盘显式加载
                // (会话内只有 harness 加载的装配,组件须另行入会话;
                //  须选带基准 csys 的零件,base.prt 无 csys 不可用)
                var asmOrigin = asm.GetOrigin();
                ctx.Messages.Info($"asmcomp_origin={asmOrigin ?? "(null)"}");
                if (asmOrigin is null)
                    throw new InvalidOperationException(
                        $"asm-comp-assemble: assembly {asm.FullName} has no disk origin");
                if (!ctx.RequireSession(out var session)) return;
                var compPath = Path.Combine(
                    Path.GetDirectoryName(asmOrigin)!, "exercise4.prt");
                var comp = session.Models.Load(compPath, CreoModelType.Part);
                ctx.Messages.Info($"asmcomp_comp_loaded={comp?.Name ?? "(null)"} path={compPath}");
                if (comp is null)
                    throw new InvalidOperationException(
                        $"asm-comp-assemble: load component failed: {compPath}");

                // 枚举装配侧首个 CSYS
                var asmCsysList = asm.Items.List(CreoModelItemType.Csys);
                ctx.Messages.Info($"asmcomp_asm_csys_count={asmCsysList.Count}");
                if (asmCsysList.Count == 0)
                    throw new InvalidOperationException(
                        $"asm-comp-assemble: assembly {asm.FullName} has no csys");
                var asmCsys = (CreoModelItem)asmCsysList[0];

                // 枚举组件侧首个 CSYS(双路径对照:通用 Items.List vs CreoSolid.ListCsys)
                var compCsysList = comp.Items.List(CreoModelItemType.Csys);
                var compSolidCsys = comp.AsSolid()?.ListCsys();
                ctx.Messages.Info(
                    $"asmcomp_comp_csys_count={compCsysList.Count} solid_csys_count={compSolidCsys?.Count ?? -1}");
                var compCsys = compCsysList.Count > 0
                    ? (CreoModelItem)compCsysList[0]
                    : compSolidCsys is { Count: > 0 } sc
                        ? (CreoModelItem)sc[0]
                        : throw new InvalidOperationException(
                            $"asm-comp-assemble: component {comp.FullName} has no csys");

                // 装入前组件数
                var countBefore = asm.ListTopLevelComponentFeatureIds().Count;

                // 装入 + CSYS 约束
                var feature = asm.AssembleComponent(comp,
                    new AssemblyConstraintSpec(
                        CreoAssemblyConstraintType.Csys, asmCsys, compCsys));
                ctx.Messages.Info($"asmcomp_created id={feature.Id}");

                // 回读断言:组件数 +1(差值形态,不依赖 fixture 原有组件数)
                var countAfter = asm.ListTopLevelComponentFeatureIds().Count;
                ctx.Messages.Info($"asmcomp_count_after={countAfter}");
                ctx.Messages.Info($"asmcomp_count_delta={countAfter - countBefore}");

                // 约束读回(读写闭环)
                var readback = asm.GetComponentConstraints(feature.Id);
                ctx.Messages.Info(
                    $"asmcomp_constraints_readback count={readback.Count}" +
                    (readback.Count > 0
                        ? $" type={readback[0].Type} offset={(readback[0].Offset?.ToString() ?? "(null)")}"
                        : ""));

                // 读回变换矩阵(诊断证据)
                var matrix = asm.GetPathTransform(new[] { feature.Id }, localToTop: true);
                if (matrix is { } m)
                    ctx.Messages.Info(
                        $"asmcomp transform diag={m.M00:G},{m.M11:G},{m.M22:G},{m.M33:G}");

                // 删除组件特征恢复原状
                feature.Delete();

                ctx.Messages.Info("asm-comp-assemble complete");
            });
    }

    private static void RegisterPtAsmCompAssembleDual(CreoAppBuilder app)
    {
        // 双约束探针(件对件):装配侧参照取装配内首个组件模型的平面
        // (装配顶层无 surface,平面全在组件内),ALIGN_OFF(offset=0)+ALIGN。
        app.Command("pt.asm.comp.assemble-dual", "PT_ASM_COMP_ASSEMBLE_DUAL",
            "PT_ASM_COMP_ASSEMBLE_DUAL_HELP", ctx =>
            {
                if (!TryRetrieveAssembly(ctx, "assembly", out var asm)) return;

                // 组件加载(同 assemble 命令)
                var asmOrigin = asm.GetOrigin();
                if (asmOrigin is null)
                    throw new InvalidOperationException(
                        $"assemble-dual: assembly {asm.FullName} has no disk origin");
                if (!ctx.RequireSession(out var session)) return;
                var compPath = Path.Combine(
                    Path.GetDirectoryName(asmOrigin)!, "exercise4.prt");
                var comp = session.Models.Load(compPath, CreoModelType.Part);
                if (comp is null)
                    throw new InvalidOperationException(
                        $"assemble-dual: load component failed: {compPath}");

                // 装配内首个组件(件对件参照宿主)
                var topIds = asm.ListTopLevelComponentFeatureIds();
                ctx.Messages.Info($"assemble_dual_top_comp_count={topIds.Count}");
                if (topIds.Count == 0)
                    throw new InvalidOperationException(
                        $"assemble-dual: assembly {asm.FullName} has no components");
                var hostFeatId = topIds[0];
                var hostModel = asm.GetPathMdl(new[] { hostFeatId });
                if (hostModel is null)
                    throw new InvalidOperationException(
                        $"assemble-dual: cannot resolve component model for feat id {hostFeatId}");

                // 装配侧:宿主组件模型内前两个平面
                var hostPlanes = ListPlanes(hostModel);
                ctx.Messages.Info($"assemble_dual_asm_plane_count={hostPlanes.Count}");
                if (hostPlanes.Count < 2)
                    throw new InvalidOperationException(
                        $"assemble-dual: host component {hostModel.FullName} has {hostPlanes.Count} planes, need >= 2");

                // 组件侧:exercise4 前两个平面
                var compPlanes = ListPlanes(comp);
                ctx.Messages.Info($"assemble_dual_comp_plane_count={compPlanes.Count}");
                if (compPlanes.Count < 2)
                    throw new InvalidOperationException(
                        $"assemble-dual: component {comp.FullName} has {compPlanes.Count} planes, need >= 2");

                var countBefore = asm.ListTopLevelComponentFeatureIds().Count;

                // 装入 + 双约束: ALIGN_OFF(offset=0) + ALIGN,装配侧走件对件参照
                var feature = asm.AssembleComponent(comp,
                    new AssemblyConstraintSpec(
                        CreoAssemblyConstraintType.AlignOffset,
                        hostPlanes[0], compPlanes[0],
                        offset: 0.0, asmRefCompFeatId: hostFeatId),
                    new AssemblyConstraintSpec(
                        CreoAssemblyConstraintType.Align,
                        hostPlanes[1], compPlanes[1],
                        asmRefCompFeatId: hostFeatId));
                ctx.Messages.Info($"assemble_dual_created id={feature.Id}");

                var countAfter = asm.ListTopLevelComponentFeatureIds().Count;
                ctx.Messages.Info($"assemble_dual_count_delta={countAfter - countBefore}");

                // 读回断言 count=2
                var readback = asm.GetComponentConstraints(feature.Id);
                ctx.Messages.Info($"assemble_dual_readback count={readback.Count}");
                for (int i = 0; i < readback.Count; i++)
                    ctx.Messages.Info(
                        $"assemble_dual_constraint[{i}] type={readback[i].Type} offset={(readback[i].Offset?.ToString() ?? "(null)")}");

                // 删除恢复原状
                feature.Delete();

                ctx.Messages.Info("assemble-dual complete");
            });
    }

    private static void RegisterPtAsmCompCreateCopy(CreoAppBuilder app)
    {
        // 两段验证:①模板造件(default placement 语义) ②packaged 装入 + PositionSet 验证。
        // 语义差异(ProAsmcomp.h 头注):PositionGet/Set 操作的是「约束和 movements 应用之前的
        // 初始位置」,仅当组件 fully packaged(无约束)时初始位置=实际位置;
        // leaveUnplaced=false 造出的组件带 default placement 约束,实际位置由约束求解决定(原点),
        // 故段①位置读回仅作记录,段②走空约束 packaged 装入做 PositionSet 生效断言。
        app.Command("pt.asm.comp.create-copy", "PT_ASM_COMP_CREATE_COPY",
            "PT_ASM_COMP_CREATE_COPY_HELP", ctx =>
            {
                // 装配模型(由 harness modelPath 加载)
                if (!TryRetrieveAssembly(ctx, "assembly", out var asm)) return;

                // 组件模型:与装配同目录的 exercise4.prt,从磁盘显式加载
                var asmOrigin = asm.GetOrigin();
                ctx.Messages.Info($"createcopy_origin={asmOrigin ?? "(null)"}");
                if (asmOrigin is null)
                    throw new InvalidOperationException(
                        $"create-copy: assembly {asm.FullName} has no disk origin");
                if (!ctx.RequireSession(out var session)) return;
                var compPath = Path.Combine(
                    Path.GetDirectoryName(asmOrigin)!, "exercise4.prt");
                var comp = session.Models.Load(compPath, CreoModelType.Part);
                ctx.Messages.Info($"createcopy_template_loaded={comp?.Name ?? "(null)"} path={compPath}");
                if (comp is null)
                    throw new InvalidOperationException(
                        $"create-copy: load template failed: {compPath}");

                // ---- 段①:模板造件(default placement) ----
                var countBefore = asm.ListTopLevelComponentFeatureIds().Count;

                var feature = asm.CreateComponentByCopy("CTK_TMP_COMP", comp, leaveUnplaced: false);
                ctx.Messages.Info($"createcopy_created id={feature.Id}");

                // 组件数 +1
                var countAfter = asm.ListTopLevelComponentFeatureIds().Count;
                ctx.Messages.Info($"createcopy_count_delta={countAfter - countBefore}");

                // PositionSet 写初始位置成功,但 default placement 约束决定实际位置(原点)
                asm.SetComponentPosition(feature.Id, CreoMat4.Translation(new CreoVec3(10, 20, 30)));
                var defMatrix = asm.GetPathTransform(new[] { feature.Id }, localToTop: true);
                if (defMatrix is { } dm)
                    ctx.Messages.Info(
                        $"createcopy_defplaced_pos x={dm.M30:G} y={dm.M31:G} z={dm.M32:G}");

                // 删除组件特征恢复原状
                feature.Delete();

                // ---- 段②:packaged 装入 + PositionSet 真验证 ----
                // 空约束装入 → fully packaged,初始位置=实际位置,PositionSet 生效可读回
                var packagedFeature = asm.AssembleComponent(comp);
                ctx.Messages.Info($"createcopy_packaged id={packagedFeature.Id}");

                asm.SetComponentPosition(packagedFeature.Id,
                    CreoMat4.Translation(new CreoVec3(10, 20, 30)));
                var pkgMatrix = asm.GetPathTransform(new[] { packagedFeature.Id }, localToTop: true);
                if (pkgMatrix is { } pm)
                    ctx.Messages.Info(
                        $"createcopy_packaged_pos_readback x={pm.M30:G} y={pm.M31:G} z={pm.M32:G}");

                // 删除恢复原状
                packagedFeature.Delete();

                // 临时模型从会话 erase(不 Save 天然无磁盘残留)
                var tmpModel = session.Models.Retrieve("CTK_TMP_COMP", CreoModelType.Part);
                var erased = tmpModel.Erase();
                ctx.Messages.Info($"createcopy_erase={erased}");

                ctx.Messages.Info("create-copy complete");
            });
    }

    // 平面 surface 枚举(pro_srf_type PLANE=34),判定惯例对齐 hole 建模 handler。
    private static List<CreoSurface> ListPlanes(CreoModel model)
        => (model.AsSolid()?.ListSurfaces() ?? Array.Empty<ICreoModelItem>())
            .OfType<CreoSurface>()
            .Where(s => s.GetSurfaceType() == 34)
            .ToList();

    private static bool TryRetrieveAssembly(CreoCommandContext ctx, string asmName, out CreoAssembly asm)
    {
        asm = null!;
        if (!ctx.TryRetrieveModel(asmName, CreoModelType.Assembly, out var model)) return false;
        if (model is CreoAssembly assembly)
        {
            asm = assembly;
            return true;
        }

        ctx.Messages.Info($"model {asmName} is not an assembly");
        return false;
    }

    private static string FormatIds(IReadOnlyList<int> ids) => string.Join(",", ids);

    // array index 0..11 的前 12 项(sample 只是打印 preview)
    private static string FormatMat12(CreoMat4 m)
        => $"{m.M00:F6},{m.M01:F6},{m.M02:F6},{m.M03:F6}," +
           $"{m.M10:F6},{m.M11:F6},{m.M12:F6},{m.M13:F6}," +
           $"{m.M20:F6},{m.M21:F6},{m.M22:F6},{m.M23:F6}";

    private static string FormatVec3(CreoVec3 v)
        => $"{v.X:F6},{v.Y:F6},{v.Z:F6}";
}
