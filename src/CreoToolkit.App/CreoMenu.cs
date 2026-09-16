namespace CreoToolkit.App;

/// <summary>菜单声明:创建 Creo 顶级菜单栏菜单。</summary>
/// <param name="MenuName">菜单内部名,挂按钮时作为 parent_menu。</param>
/// <param name="MenuLabel">菜单标签 msg key。</param>
/// <param name="Neighbor">邻居菜单名;null 表示加在末尾。</param>
/// <param name="AddAfter">true=在 Neighbor 后,false=在 Neighbor 前。</param>
public sealed record CreoMenu(
    string MenuName,
    string MenuLabel,
    string? Neighbor = null,
    bool AddAfter = true);
