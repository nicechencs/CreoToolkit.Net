namespace CreoToolkit.Sdk;

/// <summary>
/// 单个参数的 DATA 值快照(名称 + 值 + 修改状态)。<c>readonly record struct</c>: 纯值、非 <see cref="IDisposable"/>,
/// 读出即与 native 生命周期脱钩。集合接口统一走泛型 <see cref="IReadOnlyList{T}"/>，避免在非泛型/LINQ
/// 热路径上装箱。
/// </summary>
/// <param name="Name">参数名。</param>
/// <param name="Value">参数值(联合标量)。</param>
/// <param name="IsModified">是否被 Creo 标记为 modified。</param>
public readonly record struct CreoParameter(string Name, CreoParamValue Value, bool IsModified = false);
