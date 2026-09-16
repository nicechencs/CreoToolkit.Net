using CreoToolkit.Sdk.Diagnostics;
using CreoToolkit.Sdk.Errors;
using CreoToolkit.Sdk.Native;

namespace CreoToolkit.Sdk;

/// <summary>
/// 借用 ProMdl 的会话对象: **非 <see cref="IDisposable"/>**(不拥有 native), 但 **session-bound**。
/// <see cref="Name"/>/<see cref="Type"/> 是抽取时即 copy-out 的 DATA, session 关闭后仍可读(纯值);
/// 而 <see cref="Save"/>/<see cref="Parameters"/> 等触达 native 的操作,
/// 关闭后即抛 <see cref="CreoSessionClosedException"/>——绝不让内部 token 静默续用。
/// 非 abstract 基类(对偶 CreoModelItem 规则;Unknown 兜底回落此实例);
/// 实体能力(如 Regenerate)在 <see cref="CreoSolid"/>,2D 在 <see cref="CreoDrawing"/>。
/// 实例化只能经 <see cref="CreoModels"/> 工厂(禁裸 new)。
/// </summary>
public class CreoModel : ICreoModel
{
    private protected readonly CreoSession _session;
    private readonly ModelIdentity _id;

    internal CreoModel(CreoSession session, ModelIdentity id)
    {
        _session = session;
        _id = id;
        Parameters = new CreoParameters(session, id);
        Layers = new CreoLayers(session, id);
    }

    internal ModelIdentity Identity => _id;

    /// <summary>所属会话(builder 等同程序集协作者用)。</summary>
    internal CreoSession Session => _session;

    /// <summary>模型名(DATA: wstring copy-out, 已脱钩 native)。</summary>
    public string Name => _id.Name;

    /// <summary>模型类型。</summary>
    public CreoModelType Type => _id.Type;

    /// <summary>模型文件扩展名(不含点), 由模型类型推导。Unknown 抛 <see cref="NotSupportedException"/>。
    /// 不走 native:头文件 ProMdlExtensionGet 返 wchar_t[32] 字符串与本属性等价,L3 推导更稳。</summary>
    public string Extension => Type switch
    {
        CreoModelType.Part     => "prt",
        CreoModelType.Assembly => "asm",
        CreoModelType.Drawing  => "drw",
        CreoModelType.Layout   => "lay",
        CreoModelType.Format   => "frm",
        CreoModelType.Diagram  => "dgm",
        CreoModelType.Markup   => "mrk",
        CreoModelType.Notebook => "lay",  // Layout/Notebook 同指 PRO_MDL_LAYOUT=.lay (ProMdl.h:48);Creo 无 .nbk 扩展名

#pragma warning disable CS0618 // 保留 Harness 扩展名分流以维持既有行为兼容
        CreoModelType.Harness  => "asm",  // 线束是装配子类,扩展名同 .asm
#pragma warning restore CS0618
        _ => throw new NotSupportedException($"未支持的模型类型: {Type}"),
    };

    /// <summary>模型完整名(<c>Name.Extension</c>),贴近 Creo 文化下的 <c>part.prt</c> 全名约定。
    /// 收敛 sample/客户层重复的 <c>$"{model.Name}.{model.Extension}"</c> 拼接;
    /// 防客户写成 <c>$"{model.Name}-{model.Extension}"</c> 等漂移格式(简洁原则)。</summary>
    public string FullName => $"{Name}.{Extension}";

    /// <summary>参数门面(DATA 即时快照 + 分级查询)。</summary>
    public CreoParameters Parameters { get; }

    /// <summary>Layer 只读门面(DATA 快照)。</summary>
    public CreoLayers Layers { get; }

    /// <summary>按名取已有单位(ProUnitInit, DATA);**不认派生量**(N/m² 等),要 <see cref="GetUnitByExpression"/>。
    /// 单位不存在返 null。是 <see cref="CreoUnit"/> 唯一构造入口(default 值是 guard 哨兵)。</summary>
    public CreoUnit? GetUnit(string unitName)
    {
        ThrowUtil.IfNullOrEmpty(unitName);
        var result = _session.Run(n => n.UnitInit(_id, unitName));
        CreoSdkLog.Trace("unit", "init",
            new { model = _id.Name, unitName, found = result.HasValue });
        return result;
    }

    /// <summary>按表达式取单位(ProUnitInitByExpression, DATA);派生量唯一入口。
    /// 表达式无效返 null。</summary>
    public CreoUnit? GetUnitByExpression(string expression)
    {
        ThrowUtil.IfNullOrEmpty(expression);
        var result = _session.Run(n => n.UnitFromExpression(_id, expression));
        CreoSdkLog.Trace("unit", "fromexpression",
            new { model = _id.Name, expression, found = result.HasValue });
        return result;
    }

    /// <summary>当前主单位系统(ProMdlPrincipalunitsystemGet, DATA)。无主单位系统返 null。
    /// <para>模型级单位系统读 API,与 <see cref="GetUnit"/> 同纪律
    /// (零 native code,DATA copy-out,纯只读)。<see cref="ListUnitSystems"/> 列出所有,本方法返当前主。</para></summary>
    public CreoUnitSystem? GetPrincipalUnitSystem()
    {
        var result = _session.Run(n => n.ModelPrincipalUnitSystemGet(_id));
        CreoSdkLog.Trace("unitsystem", "principal-get",
            new { model = _id.Name, found = result.HasValue });
        return result;
    }

    /// <summary>列出模型所有定义的单位系统(ProMdlUnitsystemsCollect → ProArray,
    /// DATA 快照,native LIVE 数组经 ProArrayFree 立即释放,L3 出口纯 DATA)。
    /// 模型未定义任何单位系统返空列表。
    /// <para>native 接缝侧已封装释放细节,L3 出口纯 DATA。</para></summary>
    public IReadOnlyList<CreoUnitSystem> ListUnitSystems()
    {
        var result = _session.Run(n => n.ModelUnitSystemsList(_id));
        CreoSdkLog.Trace("unitsystem", "list",
            new { model = _id.Name, count = result.Count });
        return result;
    }

    /// <summary>保存模型(动作型, 经 dispatcher)。</summary>
    public void Save() =>
        CreoSdkLog.Run(
            "model", "save",
            body: () => _session.Run(n => n.ModelSave(_id)),
            failProps: () => new { name = _id.Name, type = _id.Type.ToString() });

    /// <summary>查询模型是否被 Creo 标记为 modified(即时 native 查询, 不缓存)。</summary>
    public bool IsModified() => _session.Run(n => n.ModelIsModified(_id));

    /// <summary>从会话内存擦除模型(动作型);模型不在会话返回 false。擦除后该 <see cref="CreoModel"/> 不应再用。</summary>
    public bool Erase() =>
        CreoSdkLog.Run(
            "model", "erase",
            body: () => _session.Run(n => n.ModelErase(_id)),
            failProps: () => new { name = _id.Name, type = _id.Type.ToString() },
            okProps: erased => new { name = _id.Name, type = _id.Type.ToString(), erased });

    /// <summary>复制为新名的会话内存副本;源不在会话返回 null。副本类型与本模型相同(故同走工厂分流)。</summary>
    public CreoModel? CopyTo(string newName) =>
        CreoSdkLog.Run(
            "model", "copyto",
            body: () =>
            {
                ThrowUtil.IfNullOrEmpty(newName);
                var copied = _session.Run(n => n.ModelCopy(_id, newName));
                return copied ? CreoModels.CreateModel(_session, new ModelIdentity(newName, _id.Type)) : null;
            },
            failProps: () => new { from = _id.Name, to = newName, type = _id.Type.ToString() },
            okProps: result => new { from = _id.Name, to = newName, type = _id.Type.ToString(), copied = result != null });

    /// <summary>收窄便利:若是 <see cref="CreoSolid"/>(Part/Assembly)返回其引用,否则 null。
    /// 等价 `model as CreoSolid`,只是用作显式收窄意图。</summary>
    public CreoSolid? AsSolid() => this as CreoSolid;

    /// <summary>模型磁盘来源路径(即时 native 查询);无来源(如内存新建未存)返回 null。
    /// (对标 VB API `IpfcModel.Origin`;.NET 准则:带 Creo 往返的取值器用 <c>Get</c> 前缀方法。)</summary>
    public string? GetOrigin() => _session.Run(n => n.ModelOrigin(_id));

    /// <summary>模型 common name(PDM 标识名,即时 native 查询);无则返回 null。</summary>
    public string? GetCommonName() => _session.Run(n => n.ModelCommonName(_id));

    /// <summary>模型内部名(<c>ProMdlMdlnameGet</c>);不可读返回 null。</summary>
    public string? GetMdlName() => _session.Run(n => n.ModelMdlnameGet(_id));

    /// <summary>模型显示名(<c>ProMdlDisplaynameGet</c>);不可读返回 null。</summary>
    public string? GetDisplayName() => _session.Run(n => n.ModelDisplaynameGet(_id));

    /// <summary>模型目录路径(<c>ProMdlDirectoryPathGet</c>);不可读返回 null。</summary>
    public string? GetDirectoryPath() => _session.Run(n => n.ModelDirectoryPathGet(_id));

    /// <summary>模型 subtype int(<c>ProMdlSubtypeGet</c>);不可读返回 null。</summary>
    public int? GetSubtype() => _session.Run(n => n.ModelSubtypeGet(_id));

    /// <summary>模型 filetype int(<c>ProMdlFiletypeGet</c>);不可读返回 null。</summary>
    public int? GetFiletype() => _session.Run(n => n.ModelFiletypeGet(_id));

    /// <summary>模型数字 id(<c>ProMdlIdGet</c>);不可读返回 null。</summary>
    public int? GetId() => _session.Run(n => n.ModelIdGet(_id));

    /// <summary>模型版本戳字符串(<c>ProVerstampStringGet</c> 序列化);不可读返回 null。</summary>
    public string? GetVerstamp() => _session.Run(n => n.ModelVerstampGet(_id));

    /// <summary>按对象类型查默认名(<c>ProMdlObjectDefaultnameGet</c>);不可读返回 null。</summary>
    public string? GetObjectDefaultName(CreoModelItemType objectType)
        => _session.Run(n => n.ObjectDefaultNameGet(objectType));

    /// <summary>模型首层依赖列表(DATA 快照,只 name;type/extension 留 future)。
    /// 对应 `ProMdlDependenciesDataList`,B 类 ProArray-of-struct(L1 经 ProArrayFree 释放 Creo 内存)。
    /// 无依赖返回空列表。</summary>
    public IReadOnlyList<string> ListDependencies() => _session.Run(n => n.ModelDependenciesList(_id));

    // ---- 基类新增方法 ----

    /// <summary>查询模型是否为骨架模型（Ctk_ModelIsSkeleton）；不可读返回 null。</summary>
    public bool? IsSkeleton() => _session.Run(n => n.ModelIsSkeleton(_id));

    /// <summary>清理模型依赖（Ctk_ModelDependenciesCleanup，动作型）。</summary>
    public void CleanupDependencies() =>
        CreoSdkLog.Run(
            "model", "cleanupdeps",
            body: () => _session.Run(n => n.ModelDependenciesCleanup(_id)),
            failProps: () => new { name = _id.Name, type = _id.Type.ToString() });

    internal void EnsureBelongsTo(CreoSession session)
    {
        if (!ReferenceEquals(_session, session))
            throw new InvalidOperationException("CreoModel does not belong to this session.");
    }

    // ---- 状态查询（ProMdl bool? 三态） ----

    /// <summary>查询模型是否可修改（<c>ProMdlModifiableGet</c>）；不可读返回 null。</summary>
    public bool? IsModifiable() => _session.Run(n => n.ModelIsModifiable(_id));

    /// <summary>查询模型是否允许保存（<c>ProMdlSaveAllowed</c>）；不可读返回 null。</summary>
    public bool? IsSaveAllowed() => _session.Run(n => n.ModelIsSaveAllowed(_id));

    /// <summary>查询模型位置是否为标准路径（<c>ProMdlLocationIsStandard</c>）；不可读返回 null。</summary>
    public bool? IsLocationStandard() => _session.Run(n => n.ModelLocationIsStandard(_id));

    /// <summary>查询模型是否被锁定（<c>ProMdlLockGet</c>）；不可读返回 null。</summary>
    public bool? IsLocked() => _session.Run(n => n.ModelLockGet(_id));

    /// <summary>旧命名兼容入口；请改用 <see cref="IsLocked"/>。</summary>
    [Obsolete("Use IsLocked() instead. Boolean queries should use an IsXxx prefix.", error: false)]
    public bool? GetLock() => IsLocked();

    // ---- 生命周期（动作型） ----

    /// <summary>设置模型锁定状态（<c>ProMdlLockSet</c>，动作型）。</summary>
    public void SetLock(bool locked) =>
        CreoSdkLog.Run(
            "model", "setlock",
            body: () => _session.Run(n => n.ModelLockSet(_id, locked)),
            failProps: () => new { name = _id.Name, type = _id.Type.ToString(), locked });

    /// <summary>备份模型到指定目录（<c>ProMdlSave with dir</c>，动作型）。</summary>
    public void Backup(string targetDirectory) =>
        CreoSdkLog.Run(
            "model", "backup",
            body: () =>
            {
                ThrowUtil.IfNullOrEmpty(targetDirectory);
                _session.Run(n => n.ModelBackup(_id, targetDirectory));
            },
            failProps: () => new { name = _id.Name, type = _id.Type.ToString(), dir = targetDirectory });

    /// <summary>重命名模型（<c>ProMdlnameRename</c>，动作型）;返回指向新名的新 <see cref="CreoModel"/>。
    /// <para><b>失效语义</b>:重命名后<b>本实例即失效</b>——其内部标识仍指向旧名,会话内已无此模型,
    /// 后续 <see cref="Save"/>/<see cref="Parameters"/> 等触达 native 的操作会失败（落 NotFound）。
    /// 与 <see cref="CopyTo"/> 对齐:一律改用返回的新实例继续操作。</para></summary>
    public CreoModel Rename(string newName, bool saveAfter) =>
        CreoSdkLog.Run(
            "model", "rename",
            body: () =>
            {
                ThrowUtil.IfNullOrEmpty(newName);
                _session.Run(n => n.ModelRename(_id, newName, saveAfter));
                return CreoModels.CreateModel(_session, new ModelIdentity(newName, _id.Type));
            },
            failProps: () => new { from = _id.Name, to = newName, type = _id.Type.ToString(), saveAfter },
            okProps: _ => new { from = _id.Name, to = newName, type = _id.Type.ToString(), saveAfter });

    /// <summary>删除模型文件（<c>ProMdlDelete</c>，动作型）。</summary>
    public void Delete() =>
        CreoSdkLog.Run(
            "model", "delete",
            body: () => _session.Run(n => n.ModelDelete(_id)),
            failProps: () => new { name = _id.Name, type = _id.Type.ToString() });

    /// <summary>获取模型当前窗口 id（<c>ProMdlWindowGet</c>）；无窗口返回 null。</summary>
    public int? GetWindowId() => _session.Run(n => n.ModelWindowGet(_id));

    /// <summary>读取模型数据三元组（<c>Ctk_ModelDataGet</c>，DATA 快照）；不可读返回 null。</summary>
    public ModelDataRecord? GetData() => _session.Run(n => n.ModelDataGet(_id));

    /// <summary>列出本模型的几何公差(GTol)条目(ProMdlGtolVisit 枚举，DATA 快照)。
    /// 返回的 <see cref="ICreoModelItem"/> 由内部工厂从 ItemRef 实例化(类型按 ProType 分流)；
    /// PRO_GTOL(type=32)不在 12 衍生中，降级为基类 <see cref="CreoModelItem"/> 实例。
    /// 无 GTol 返空列表。</summary>
    public IReadOnlyList<ICreoModelItem> ListGeometricTolerances()
        => Materialize(_session.Run(n => n.ModelGtolList(_id)));

    /// <summary>在模型上创建形位公差。返回新建 GTol 的 modelitem。
    /// 对齐 <see cref="CreoAssembly.AssembleComponent"/> 写方法形态。</summary>
    public ICreoModelItem CreateGtol(CreoGtolType type, string valueString,
        double x = 0, double y = 0, double z = 0)
    {
        ThrowUtil.IfNullOrWhiteSpace(valueString);
        int gtolId = 0;
        return CreoSdkLog.Run(
            "gtol", "create_gtol",
            body: () =>
            {
                var fr = _session.Run(n => n.GtolCreate(_id, type, valueString, x, y, z));
                gtolId = fr.Id;
                return CreoModelItem.CreateFromRef(_session, fr);
            },
            failProps: () => new { model = _id.Name, type = type.ToString(), valueString },
            okProps: _ => new { model = _id.Name, type = type.ToString(), valueString, id = gtolId });
    }

    /// <summary>读回形位公差的类型与值字符串。</summary>
    public GtolInfo ReadGtol(int gtolId)
        => _session.Run(n => n.GtolRead(_id, gtolId));

    /// <summary>预览 gtol 附着的 make-dim 信息(Bridge 内部完成 Alloc → FreeSet → MakeDimGet → Free
    /// 全流程,业务侧无需处理 IntPtr 生命周期)。在真 <see cref="CreoModel.CreateGtol"/> 前
    /// 用来查看:annotation plane wrapper / attach pair 数 / senses / location(xyz)。
    /// 模型不在会话返 null;native 失败抛 <see cref="Errors.CreoException"/>。</summary>
    public DimensionAttachments? PrepareGtolAttachAndReadMakeDim(double x, double y, double z)
    {
        var raw = _session.Run(n => n.PrepareGtolAttachAndReadMakeDim(_id, x, y, z));
        CreoSdkLog.Trace("model", "gtol.attach.makedim.prepare",
            new { model = _id.Name, x, y, z, found = raw is not null });
        return raw is null ? null : CreoDimension.ToPublic(_session, raw);
    }

    /// <summary>删除形位公差。</summary>
    public void DeleteGtol(int gtolId)
    {
        CreoSdkLog.Run(
            "gtol", "delete_gtol",
            body: () => _session.Run(n => n.GtolDelete(_id, gtolId)),
            failProps: () => new { model = _id.Name, gtolId },
            okProps: () => new { model = _id.Name, gtolId });
    }

    /// <summary>统一模型项查询 facade。按类型路由到各 visitor。</summary>
    public CreoModelItems Items => _items ??= new CreoModelItems(_session, this);
    private CreoModelItems? _items;

    /// <summary>将 ItemRef 列表经工厂实例化为 ICreoModelItem 列表(共享辅助)。</summary>
    internal IReadOnlyList<ICreoModelItem> Materialize(IReadOnlyList<ItemRef> refs)
        => Materialize(_session, refs);

    internal static IReadOnlyList<ICreoModelItem> Materialize(CreoSession session, IReadOnlyList<ItemRef> refs)
    {
        if (refs.Count == 0) return Array.Empty<ICreoModelItem>();
        var arr = new ICreoModelItem[refs.Count];
        for (int i = 0; i < refs.Count; i++)
            arr[i] = CreoModelItem.CreateFromRef(session, refs[i]);
        return arr;
    }

    /// <summary>将模型自身转为 modelitem 视图(ProMdlToModelitem)。不支持时返回 null。</summary>
    public ICreoModelItem? ToModelItem()
    {
        var itemRef = _session.Run(n => n.ModelToModelItem(_id));
        if (itemRef is null) return null;
        return CreoModelItem.CreateFromRef(_session, itemRef.Value);
    }
}
