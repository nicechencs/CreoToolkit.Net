namespace CreoToolkit.Sdk;

/// <summary>drawing view DATA 快照,不持 native view 句柄。</summary>
public readonly record struct CreoDrawingView(
    int ViewId,
    int Sheet,
    double Scale,
    string Name,
    string? SolidName,
    CreoModelType? SolidType,
    long SessionEpoch);

/// <summary>列 view 结果:成功快照 + 因元数据缺失被跳过的 view 计数。</summary>
public readonly record struct CreoDrawingViewListResult(
    IReadOnlyList<CreoDrawingView> Views,
    int SkippedCount);

internal static class CreoDrawingViewGuard
{
    public static void NotUninitialized(CreoDrawingView view, string paramName)
    {
        if (view.Name is null)
            throw new InvalidOperationException(
                $"CreoDrawingView '{paramName}' 是 default 值,只能经 CreoDrawing.ListViews 工厂取得。");
    }

    public static void SameSessionEpoch(CreoDrawingView view, long currentEpoch, string paramName)
    {
        NotUninitialized(view, paramName);
        if (view.SessionEpoch != currentEpoch)
            throw new InvalidOperationException(
                $"CreoDrawingView '{paramName}' 来自 epoch={view.SessionEpoch} 的 CreoSession,当前 session epoch={currentEpoch} — 不能跨 session 使用。");
    }
}
