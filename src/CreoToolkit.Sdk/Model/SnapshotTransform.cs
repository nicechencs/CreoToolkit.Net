namespace CreoToolkit.Sdk;

/// <summary>Snapshot 单个组件的变换记录(<c>ProSnapshotTrfsGet</c> 返回元素)。
/// <para>
/// TableNum = 有效路径深度(0=顶层 snapshot);<br/>
/// ComponentIds = 该深度内的组件 id 序列(长度=TableNum,自顶层顺序拼路径);<br/>
/// Matrix4x4 = 4×4 变换矩阵展平(row-major,长度 16),即从 snapshot 坐标系到该组件的
/// 累积 transform。
/// </para>
/// </summary>
public sealed record SnapshotTransform(
    int TableNum,
    IReadOnlyList<int> ComponentIds,
    IReadOnlyList<double> Matrix4x4);
