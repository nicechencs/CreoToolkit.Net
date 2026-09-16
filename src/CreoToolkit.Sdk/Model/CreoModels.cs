using CreoToolkit.Sdk.Diagnostics;
using CreoToolkit.Sdk.Errors;
using CreoToolkit.Sdk.Native;

namespace CreoToolkit.Sdk;

/// <summary>模型查询/检索门面。session-bound。</summary>
public sealed class CreoModels
{
    private readonly CreoSession _session;

    internal CreoModels(CreoSession session) => _session = session;

    /// <summary>当前活动模型; 无则返回 null。返回具体子类型(<see cref="CreoSolid"/>/<see cref="CreoDrawing"/>),
    /// 调用方可 `is CreoSolid` 模式或 <see cref="CreoModel.AsSolid"/> 收窄。</summary>
    public CreoModel? GetCurrent()
    {
        var id = _session.Run(n => n.ModelCurrent());
        return id is { } v ? CreateModel(_session, v) : null;
    }

    /// <summary>
    /// 按 (名, 类型) 取已在会话中的模型。检索是用户显式点名, 找不到视为错误 → 抛 <see cref="CreoException"/>
    /// (区别于"列举/当前"这类查询型的 null 语义)。
    /// <para>
    /// 本 API 实际只做"绑定 SDK token"(经 <c>ModelExists</c> 仅会话内查找,不触磁盘);
    /// 若需按 <c>search_path</c> 从磁盘检索/加载,请用 <see cref="RetrieveByName"/>;
    /// 若已知全路径,请用 <see cref="Load"/>。
    /// </para>
    /// </summary>
    public CreoModel Retrieve(string name, CreoModelType type) =>
        CreoSdkLog.Run<CreoModel>(
            "model", "retrieve",
            body: () =>
            {
                ThrowUtil.IfNullOrEmpty(name);
                var exists = _session.Run(n => n.ModelExists(name, type));
                if (!exists)
                    throw new CreoException("ProMdlnameInit", ProError.NotFound,
                        $"模型 {name}({type}) 不在会话中(本 API 不读盘;如需从磁盘加载,请用 Load)。");
                return CreateModel(_session, new ModelIdentity(name, type));
            },
            failProps: () => new { name, type = type.ToString() });

    /// <summary>
    /// 按 (名, 类型) 经 Creo <c>search_path</c> 检索/加载模型(<c>ProMdlnameRetrieve</c>)。
    /// 会话中已有则直接绑定;磁盘也找不到时返回 null,不抛。
    /// </summary>
    public CreoModel? RetrieveByName(string name, CreoModelType type) =>
        CreoSdkLog.RunWithWarn<CreoModel?>(
            "model", "retrieve-by-name",
            body: () =>
            {
                ThrowUtil.IfNullOrEmpty(name);
                var identity = _session.Run(n => n.ModelRetrieveByName(name, type));
                return identity is null ? null : CreateModel(_session, identity.Value);
            },
            failProps: () => new { name, type = type.ToString() },
            okProps: result => new { name = result!.Name, type = result.Type.ToString() },
            warnProbe: result => result is null
                ? new WarnSpec("retrieve-by-name.miss", $"返 null: {name}({type})",
                    new { name, type = type.ToString() })
                : null);

    /// <summary>擦除当前活动模型及其递归依赖（<c>ProMdlEraseAll</c>，动作型）。</summary>
    public void EraseCurrentWithDependencies() =>
        CreoSdkLog.Run(
            "model", "erasecurrentwithdependencies",
            body: () => _session.Run(n =>
            {
                n.ModelEraseCurrentWithDependencies();
                return 0;
            }));

    /// <summary>旧命名兼容入口；请改用 <see cref="EraseCurrentWithDependencies"/>。</summary>
    [Obsolete("Use EraseCurrentWithDependencies() instead. 底层 ProMdlEraseAll 语义是擦当前活动模型+其依赖;入参 type 从未生效。", error: false)]
    public void EraseAll(CreoModelType modelType)
    {
        _ = modelType;
        EraseCurrentWithDependencies();
    }

    /// <summary>擦除所有未显示的模型（<c>ProMdlEraseNotDisplayed</c>，动作型）。</summary>
    public void EraseNotDisplayed() =>
        CreoSdkLog.Run(
            "model", "erasenotdisplayed",
            body: () => _session.Run(n => n.ModelEraseNotDisplayed()));

    /// <summary>按名字检索简化表示（Ctk_AssemblySimprepRetrieve，静态行为不依赖已打开实例）。</summary>
    public void RetrieveSimprep(string assemName, CreoModelType fileType, string simpRepName) =>
        CreoSdkLog.Run(
            "model", "retrievesimprep",
            body: () =>
            {
                ThrowUtil.IfNullOrEmpty(assemName);
                ThrowUtil.IfNullOrEmpty(simpRepName);
                _session.Run(n => { n.AssemblySimprepRetrieve(assemName, fileType, simpRepName); return 0; });
            },
            failProps: () => new { name = assemName, type = fileType.ToString(), simprep = simpRepName });

    /// <summary>P7:从磁盘路径打开模型(Ctk_ModelLoad)；返回已绑定的 <see cref="CreoModel"/>（按类型分流），
    /// 失败或路径无效返 null。</summary>
    public CreoModel? Load(string fullPath, CreoModelType fileType = CreoModelType.Unknown, bool askUserAboutReps = false) =>
        CreoSdkLog.RunWithWarn<CreoModel?>(
            "model", "load",
            body: () =>
            {
                ThrowUtil.IfNullOrEmpty(fullPath);
                var id = _session.Run(n => n.ModelLoad(fullPath, fileType, askUserAboutReps));
                return id is { } v ? CreateModel(_session, v) : null;
            },
            failProps: () => new { path = fullPath, type = fileType.ToString() },
            okProps: result => new { name = result!.Name, type = result.Type.ToString(), path = fullPath },
            warnProbe: result => result is null
                ? new WarnSpec("load.miss", $"返 null: {fullPath}",
                    new { path = fullPath, type = fileType.ToString() })
                : null);

    /// <summary>P7:在会话中新建空模型(Ctk_ModelInit，动作型)。</summary>
    public void Init(string name, CreoModelType fileType) =>
        CreoSdkLog.Run(
            "model", "init",
            body: () =>
            {
                ThrowUtil.IfNullOrEmpty(name);
                _session.Run(n => { n.ModelInit(name, fileType); return 0; });
            },
            failProps: () => new { name, type = fileType.ToString() });

    /// <summary>按 type 分流到具体子类;Unknown 兜底回落基类不抛(对偶 ModelItem 规则)。</summary>
    /// <remarks>产出 BORROWED 态:session-bound、非 IDisposable;native token 由 session 持有,关闭后再触达 native 即抛 <see cref="Errors.CreoSessionClosedException"/>。</remarks>
    internal static CreoModel CreateModel(CreoSession session, ModelIdentity id) => id.Type switch
    {
        CreoModelType.Part     => new CreoPart(session, id),
        CreoModelType.Assembly => new CreoAssembly(session, id),
        CreoModelType.Drawing  => new CreoDrawing(session, id),
        CreoModelType.Layout   => new CreoLayout(session, id),
        CreoModelType.Format   => new CreoFormat(session, id),
        CreoModelType.Diagram  => new CreoDiagram(session, id),
        CreoModelType.Markup   => new CreoMarkup(session, id),
        CreoModelType.Notebook => new CreoNotebook(session, id),
#pragma warning disable CS0618 // 保留 Harness 分流以维持既有行为兼容
        CreoModelType.Harness  => new CreoHarness(session, id),
#pragma warning restore CS0618
        _                      => new CreoModel(session, id),  // Unknown 兜底回落基类
    };
}
