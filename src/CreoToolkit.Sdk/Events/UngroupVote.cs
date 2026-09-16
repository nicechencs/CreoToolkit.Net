namespace CreoToolkit.Sdk.Events;

/// <summary>ungroup 拦截投票:
/// <see cref="Allow"/>=0 放行 / <see cref="Block"/>=1 拦截。
/// handler 异常时默认 Allow(避免误拦截阻断 UI 操作)。</summary>
public enum UngroupVote
{
    Allow = 0,
    Block = 1,
}
