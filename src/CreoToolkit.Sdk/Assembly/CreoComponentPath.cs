using CreoToolkit.Sdk.Native;

namespace CreoToolkit.Sdk;

/// <summary>装配组件路径(对齐 OTK <c>pfcComponentPath</c>/VB <c>IpfcComponentPath</c>:
/// Root/ComponentIds/GetLeaf/GetTransform 同官方成员名)。
/// ProAsmcomp 本质是 PRO_FEAT_COMPONENT 类型的 feature，本类同时持有 compIdPath 和 feature 视图。
/// session-bound，非 IDisposable（DATA 语义，path 是纯 int 数组）。
/// <para>与 selection 双向可逆:交互选择经 <see cref="CreoSelection.Path"/> 抽出本路径;
/// (item + path) 经 <see cref="CreoSelections.CreateModelItemSelection"/> 重建 selection。</para></summary>
public sealed class CreoComponentPath
{
    private readonly CreoSession _session;
    private readonly CreoAssembly _root;
    private readonly int[] _path;
    private CreoFeature? _feature;

    internal CreoComponentPath(CreoSession session, CreoAssembly root, int[] path)
    {
        _session = session;
        _root = root;
        _path = (int[])path.Clone(); // 构造期防御拷贝：不变量由类型自身固化，不依赖调用方不复用数组
    }

    internal void EnsureBelongsTo(CreoSession session)
    {
        if (!ReferenceEquals(_session, session))
            throw new InvalidOperationException("CreoComponentPath 不属于该会话。");
    }

    /// <summary>本组件的 feature id(path 末尾元素)。</summary>
    public int FeatureId => _path[_path.Length - 1];

    /// <summary>从根装配到本组件的完整 compIdPath（只读快照,对齐官方 GetComponentIds）。
    /// 每级是该组件 feature 在其父装配中的 id。每次返回独立只读视图，内部可变数组不外泄。</summary>
    public IReadOnlyList<int> ComponentIds => Array.AsReadOnly(_path);

    /// <summary>组件路径深度（1 = 顶层直接子件）。</summary>
    public int Depth => _path.Length;

    /// <summary>所属根装配体。</summary>
    public CreoAssembly Root => _root;

    /// <summary>本组件作为 feature 的视图(PRO_FEAT_COMPONENT)。
    /// 可经此调用 GetInfo/GetParentIds/GetChildIds/GetName 等 feature 操作。
    /// <para>feature 的 owner 是本组件的直接父装配体：depth=1 时是 Root；
    /// depth&gt;1 时需通过 GetPathMdl 解析父级路径。父级解析失败则回退到 Root。</para></summary>
    public CreoFeature Feature => _feature ??= CreateFeature();

    private CreoFeature CreateFeature()
    {
        var ownerIdentity = ResolveParentAssemblyIdentity();
        return new CreoFeature(_session, new ItemRef(ownerIdentity, CreoModelItemType.Feature, FeatureId));
    }

    private ModelIdentity ResolveParentAssemblyIdentity()
    {
        if (_path.Length <= 1)
            return _root.Identity;

        var parentPath = new int[_path.Length - 1];
        Array.Copy(_path, parentPath, parentPath.Length);
        var parentModel = _root.GetPathMdl(parentPath);
        return parentModel?.Identity ?? _root.Identity;
    }

    /// <summary>取路径所指组件的模型(对齐官方 GetLeaf);模型未加载或路径无效返 null。</summary>
    public CreoModel? GetLeaf() => _root.GetPathMdl(_path);

    /// <summary>取本组件相对根装配的 4×4 变换矩阵(localToTop=true)；无效路径返 null。</summary>
    public CreoMat4? GetTransform(bool localToTop = true)
        => _root.GetPathTransform(_path, localToTop);

    /// <summary>取本组件下的子级组件列表。
    /// 先取子模型，若为 Assembly 则列出其顶层组件 feature id，拼接到当前 path。
    /// 子模型非 Assembly 或不可读返空列表。</summary>
    public IReadOnlyList<CreoComponentPath> GetChildren()
    {
        _session.EnsureOpen();
        var childModel = GetLeaf();
        if (childModel is not CreoAssembly childAsm)
            return Array.Empty<CreoComponentPath>();

        var childFeatureIds = _session.Run(n => n.AssemblyTopLevelComponents(childAsm.Identity));
        if (childFeatureIds.Count == 0)
            return Array.Empty<CreoComponentPath>();

        var result = new CreoComponentPath[childFeatureIds.Count];
        for (int i = 0; i < childFeatureIds.Count; i++)
        {
            var childPath = new int[_path.Length + 1];
            Array.Copy(_path, childPath, _path.Length);
            childPath[_path.Length] = childFeatureIds[i];
            result[i] = new CreoComponentPath(_session, _root, childPath);
        }
        return result;
    }

    /// <summary>调试友好，格式 "CreoComponentPath(path=[1,3,5]@ROOT)"。</summary>
    public override string ToString()
        => $"CreoComponentPath(path=[{string.Join(",", _path)}]@{_root.Identity.Name})";
}
