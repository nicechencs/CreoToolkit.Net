namespace CreoToolkit.App;

/// <summary>
/// App 层稳定的 popup notification 类型。不暴露 Interop 的 pro_notify_type，
/// 同时保留未知 Creo 版本值的 raw value 以便诊断和前向兼容。
/// </summary>
public readonly record struct CreoPopupNotificationType
{
    private CreoPopupNotificationType(int value) => Value = value;

    /// <summary>Creo 创建 popup menu 后触发，对应 PRO_POPUPMENU_CREATE_POST=72
    /// （值以 ProNotify.h / ProNotify.Enums.g.cs 为真源；11 是 PRO_MDL_ERASE_POST_ALL）。</summary>
    public static CreoPopupNotificationType PopupMenuCreatePost { get; } = new(72);

    public int Value { get; }

    /// <summary>构造 SDK 尚未命名的 notification type。</summary>
    public static CreoPopupNotificationType FromRaw(int value) => new(value);

    public override string ToString()
        => this == PopupMenuCreatePost ? nameof(PopupMenuCreatePost) : $"Unknown({Value})";
}
