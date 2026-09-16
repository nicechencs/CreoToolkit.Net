using CreoToolkit.Sdk.Diagnostics;
using CreoToolkit.Sdk.Native;

namespace CreoToolkit.Sdk;

/// <summary>装配(Assembly):继承 CreoSolid。Assembly 独有能力(组件/骨架)。</summary>
public sealed class CreoAssembly : CreoSolid
{
    private const int ProCompPathMax = 25;

    internal CreoAssembly(CreoSession session, ModelIdentity id) : base(session, id) { }

    /// <summary>顶层组件特征 id 列表(DATA 快照); 无子件返回空列表。</summary>
    public IReadOnlyList<int> ListTopLevelComponentFeatureIds()
        => _session.Run(n => n.AssemblyTopLevelComponents(Identity));

    /// <summary>按名读取 snapshot 的全部组件变换(<c>ProSnapshotTrfsGet</c>)。<br/>
    /// 每 element = (TableNum, ComponentIds, Matrix4x4=展平 row-major 16 double)。<br/>
    /// <b>作用域约束</b>(ProKinDrag.h:native API 无装配入参,只作用于当前活动装配):
    /// 本方法要求 receiver 就是会话 current model,否则直接返 null 不打 native
    /// (多装配打开时防止读到别的装配的 snapshot)。<br/>
    /// snapshot 不存在返 null(注意:真机实测 native 对不存在的 snapshot 可能返
    /// GeneralError 而非 NOT_FOUND,该情形会抛 <see cref="Errors.CreoException"/>);
    /// 空 snapshot 返空列表;snapshotName 空/null 返 null。</summary>
    public IReadOnlyList<SnapshotTransform>? GetSnapshotTransforms(string snapshotName)
    {
        // native 无装配入参 —— 校验 receiver 是 current,防多装配场景读错对象
        var current = _session.Run(n => n.ModelCurrent());
        if (current is null || current.Value != Identity)
        {
            CreoSdkLog.Trace("assembly", "getsnapshottransforms.not_current",
                new { asm = Identity.Name, current = current?.Name });
            return null;
        }
        var result = _session.Run(n => n.SnapshotTrfsGet(snapshotName));
        CreoSdkLog.Trace("assembly", "getsnapshottransforms",
            new { asm = Identity.Name, snapshot = snapshotName, count = result?.Count });
        return result;
    }

    /// <summary>以当前屏幕位置创建顶层装配 snapshot(<c>ProSnapshotCreate</c>,新建者成为
    /// active)。同 <see cref="GetSnapshotTransforms"/> 的作用域约束:receiver 非 current
    /// 时抛 <see cref="InvalidOperationException"/>(写操作,错对象必须硬失败不静默)。</summary>
    public void CreateSnapshot(string snapshotName)
    {
        EnsureCurrent(nameof(CreateSnapshot));
        _session.Run(n => n.SnapshotCreate(snapshotName));
        CreoSdkLog.Trace("assembly", "createsnapshot", new { asm = Identity.Name, snapshotName });
    }

    /// <summary>按名删除顶层装配 snapshot(<c>ProSnapshotDelete</c>);不存在时静默。
    /// receiver 非 current 时抛 <see cref="InvalidOperationException"/>。</summary>
    public void DeleteSnapshot(string snapshotName)
    {
        EnsureCurrent(nameof(DeleteSnapshot));
        _session.Run(n => n.SnapshotDelete(snapshotName));
        CreoSdkLog.Trace("assembly", "deletesnapshot", new { asm = Identity.Name, snapshotName });
    }

    // snapshot 族 native API 无装配入参,只作用于当前活动装配;写操作错对象必须硬失败。
    private void EnsureCurrent(string operation)
    {
        var current = _session.Run(n => n.ModelCurrent());
        if (current is null || current.Value != Identity)
            throw new InvalidOperationException(
                $"{operation} 要求 {Identity.Name} 是当前活动模型(实际: {current?.Name ?? "(none)"});" +
                "snapshot native API 只作用于当前活动装配。");
    }

    /// <summary>列出顶层组件对象(每个持有单元素 compIdPath)；无子件返空列表。</summary>
    public IReadOnlyList<CreoComponentPath> ListComponents()
    {
        var ids = _session.Run(n => n.AssemblyTopLevelComponents(Identity));
        if (ids.Count == 0) return Array.Empty<CreoComponentPath>();
        var result = new CreoComponentPath[ids.Count];
        for (int i = 0; i < ids.Count; i++)
            result[i] = new CreoComponentPath(_session, this, new[] { ids[i] });
        return result;
    }

    /// <summary>按 compIdPath 构造组件对象（不校验路径存在性，调 GetModel 时才验证）。</summary>
    public CreoComponentPath GetComponent(params int[] compIdPath)
    {
        ValidateComponentPath(compIdPath);
        // 空数组只在这里拒绝：CreoComponentPath 语义=至少一个组件；
        // GetPathMdl/GetPathTransform 的空路径=装配自身，是官方合法语义，不下沉此校验
        if (compIdPath.Length == 0)
            throw new ArgumentException("组件路径至少需要一个组件特征 id。", nameof(compIdPath));
        return new CreoComponentPath(_session, this, (int[])compIdPath.Clone());
    }

    /// <summary>骨架模型名; 无骨架返回 null。</summary>
    public string? GetSkeletonName()
        => _session.Run(n => n.AssemblySkeleton(Identity));

    /// <summary>取骨架模型对象; 无骨架返回 null。骨架始终是 Part。</summary>
    public CreoPart? GetSkeleton()
    {
        var name = _session.Run(n => n.AssemblySkeleton(Identity));
        if (name is null) return null;
        var id = new ModelIdentity(name, CreoModelType.Part);
        return CreoModels.CreateModel(_session, id) as CreoPart;
    }

    /// <summary>按组件 id 路径读取子模型；无效路径或模型不在会话中返回 null。</summary>
    public CreoModel? GetPathMdl(int[] compIdPath)
    {
        ValidateComponentPath(compIdPath);
        var child = _session.Run(n => n.AssemblyPathMdlGet(Identity, compIdPath));
        return child is { } id ? CreoModels.CreateModel(_session, id) : null;
    }

    /// <summary>按组件 id 路径读取 4×4 变换矩阵;无效路径返回 null。
    /// 返 <see cref="CreoMat4"/> 行优先(<see cref="CreoMat4.FromArray"/> 内部已校验 16 长度,
    /// 长度非法抛 <see cref="ArgumentException"/> — Bridge 返非 16-double 即视为契约破裂)。</summary>
    public CreoMat4? GetPathTransform(int[] compIdPath, bool localToTop)
    {
        ValidateComponentPath(compIdPath);
        var matrix = _session.Run(n => n.AssemblyPathTransformGet(Identity, compIdPath, localToTop));
        CreoSdkLog.Trace("assembly", "getpathtransform",
            new
            {
                assembly = Identity.Name,
                pathLen = compIdPath.Length,
                localToTop,
                found = matrix is not null,
            });
        return matrix is null ? null : CreoMat4.FromArray(matrix);
    }

    // 组件路径公共校验（GetComponent/GetPathMdl/GetPathTransform 三入口一致）：
    // null/超长/非正 id 一律 fail-fast；空数组不在此拒绝（path 读取的空路径=装配自身合法语义）
    private static void ValidateComponentPath(int[] compIdPath)
    {
        ThrowUtil.IfNull(compIdPath);
        if (compIdPath.Length > ProCompPathMax)
            throw new ArgumentException($"组件路径长度必须 <= {ProCompPathMax}。", nameof(compIdPath));
        if (compIdPath.Any(id => id <= 0))
            throw new ArgumentException("组件特征 id 必须为正。", nameof(compIdPath));
    }

    // ---- 装配写入 ----

    /// <summary>读回组件约束(Type/Offset/参照)。compFeatId 为组件特征 id。</summary>
    public IReadOnlyList<AssemblyConstraintInfo> GetComponentConstraints(int compFeatId)
        => _session.Run(n => n.ComponentConstraintsRead(Identity, compFeatId));

    /// <summary>装入组件并设置约束。无约束时纯 packaged 装入(位置=单位阵)。
    /// 返回组件特征对象(可 Delete 恢复原状)。</summary>
    public CreoFeature AssembleComponent(CreoModel component, params AssemblyConstraintSpec[] constraints)
    {
        ThrowUtil.IfNull(component);
        component.EnsureBelongsTo(_session);
        int fid = 0;
        return CreoSdkLog.Run(
            "assembly", "assemble_component",
            body: () =>
            {
                var fr = _session.Run(n => n.ComponentAssemble(Identity, component.Identity, constraints));
                fid = fr.Id;
                return new CreoFeature(_session, fr);
            },
            failProps: () => new { assembly = Identity.Name, component = component.Identity.Name },
            okProps: _ => new { assembly = Identity.Name, component = component.Identity.Name, id = fid });
    }

    /// <summary>模板造件:按名创建组件副本; template 为 null 时造空零件。
    /// compType 由 template 类型推导(null 时默认 Part);名字 ≤31 字符。
    /// 返回组件特征对象(可 Delete 恢复原状)。</summary>
    public CreoFeature CreateComponentByCopy(string name, CreoModel? template, bool leaveUnplaced = false)
    {
        ThrowUtil.IfNullOrWhiteSpace(name);
        if (name.Length > 31)
            throw new ArgumentException(
                $"组件名 '{name}' 超过 31 字符限制", nameof(name));
        var compType = template?.Type ?? CreoModelType.Part;
        if (compType != CreoModelType.Part && compType != CreoModelType.Assembly)
            throw new ArgumentException(
                $"模板类型仅允许 Part/Assembly,当前 {compType}", nameof(template));
        template?.EnsureBelongsTo(_session);
        int fid = 0;
        return CreoSdkLog.Run(
            "assembly", "create_component_by_copy",
            body: () =>
            {
                var fr = _session.Run(n => n.ComponentCreateByCopy(
                    Identity, name, compType, template?.Identity, leaveUnplaced));
                fid = fr.Id;
                return new CreoFeature(_session, fr);
            },
            failProps: () => new { assembly = Identity.Name, name, template = template?.Name ?? "(null)" },
            okProps: _ => new { assembly = Identity.Name, name, id = fid });
    }

    /// <summary>设置组件位置(PositionSet + Regenerate 原子语义); 矩阵旋转子阵须正交(容差 1e-9)。</summary>
    public void SetComponentPosition(int compFeatId, CreoMat4 position)
    {
        if (!position.IsOrthonormal())
            throw new ArgumentException("矩阵旋转子阵非正交(容差 1e-9)", nameof(position));
        CreoSdkLog.Run(
            "assembly", "set_component_position",
            body: () => _session.Run(n => n.ComponentPositionSet(Identity, compFeatId, position.ToArray())),
            failProps: () => new { assembly = Identity.Name, compFeatId });
    }

    // ---- CreoAssembly 新增方法 ----

    /// <summary>展开爆炸视图（Ctk_AssemblyExplode，动作型）。</summary>
    public void Explode() =>
        CreoSdkLog.Run(
            "assembly", "explode",
            body: () => _session.Run(n => n.AssemblyExplode(Identity)),
            failProps: () => new { name = Identity.Name });

    /// <summary>收拢爆炸视图（Ctk_AssemblyUnexplode，动作型）。</summary>
    public void Unexplode() =>
        CreoSdkLog.Run(
            "assembly", "unexplode",
            body: () => _session.Run(n => n.AssemblyUnexplode(Identity)),
            failProps: () => new { name = Identity.Name });

    /// <summary>查询装配是否处于爆炸状态（Ctk_AssemblyIsExploded）；不可读返回 null。</summary>
    public bool? IsExploded() => _session.Run(n => n.AssemblyIsExploded(Identity));
}
