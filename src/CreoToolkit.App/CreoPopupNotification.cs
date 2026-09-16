namespace CreoToolkit.App;

/// <summary>popup notification handler（菜单创建时触发；menuName 已从 char[32] 解码为 string）。</summary>
public delegate void CreoPopupNotificationHandler(string menuName);

/// <summary>
/// popup notification 声明（对应 ProNotificationSet）。NotifyType 见 Pro/Toolkit pro_notify_type enum；
/// 例如 PRO_POPUPMENU_CREATE_POST=72。Host.Dispose 反序反注册（加强项 5）。
/// </summary>
public sealed class CreoPopupNotification
{
    public CreoPopupNotification(CreoPopupNotificationType notifyType, CreoPopupNotificationHandler handler)
    {
        NotifyType = notifyType;
        Handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    [Obsolete("使用 CreoPopupNotificationType typed overload；未知值使用 CreoPopupNotificationType.FromRaw。")]
    public CreoPopupNotification(int notifyType, CreoPopupNotificationHandler handler)
        : this(CreoPopupNotificationType.FromRaw(notifyType), handler) { }

    public CreoPopupNotificationType NotifyType { get; }
    public CreoPopupNotificationHandler Handler { get; }
}
