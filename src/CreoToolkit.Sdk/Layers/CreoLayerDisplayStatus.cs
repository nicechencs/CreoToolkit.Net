namespace CreoToolkit.Sdk;

/// <summary>Layer 的临时显示状态，对齐 ProLayerDisplay。</summary>
public enum CreoLayerDisplayStatus
{
    /// <summary>默认显示状态。</summary>
    None = -1,

    /// <summary>显示该 layer。</summary>
    Normal = 1,

    /// <summary>隔离显示该 layer。</summary>
    Display = 2,

    /// <summary>隐藏该 layer。</summary>
    Blank = 3,

    /// <summary>隐藏组件 layer，主要用于组件模式。</summary>
    Hidden = 5,

    /// <summary>保留状态。</summary>
    Skip = 6,
}
