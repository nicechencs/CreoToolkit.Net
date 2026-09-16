namespace CreoToolkit.App.Catalog;

// 命令风险等级，dialog 用 圆点色标提示 (绿=只读 / 黄=写入可回滚 / 红=不可逆)。
public enum DangerLevel
{
    Green,
    Yellow,
    Red,
}
