using CreoToolkit.App;

namespace CreoToolkit.Host;

/// <summary>把进程环境一次性解析成托管启动选项，避免 Bootstrap 在多个阶段重复读取全局状态。</summary>
internal sealed record HostStartupOptions(
    string MsgFile,
    bool MsgFileFromEnvironment)
{
    internal static HostStartupOptions FromEnvironment()
        => Resolve(Environment.GetEnvironmentVariable);

    internal static HostStartupOptions Resolve(Func<string, string?> readEnvironment)
    {
        ThrowUtil.IfNull(readEnvironment);

        var msgFileValue = readEnvironment(CtkEnv.AppMsgFile);
        var msgFileFromEnvironment = !string.IsNullOrWhiteSpace(msgFileValue);
        var msgFile = msgFileFromEnvironment ? msgFileValue! : "ctkdemo.txt";

        return new HostStartupOptions(msgFile, msgFileFromEnvironment);
    }
}
