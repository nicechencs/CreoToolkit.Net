using CreoToolkit.App;

namespace CreoToolkit.Samples.PtInstall;

public static class PtInstallRegistration
{
    public static void Register(CreoAppBuilder app)
    {
        ThrowUtil.IfNull(app);
        // pt_install_test/TestInstall.c: 命令/消息与当前窗口检查。
        app.Command("pt.install.check", "PT_INSTALL_CHECK", "PT_INSTALL_CHECK_HELP", ctx =>
        {
            string status;
            if (ctx.Session is null)
            {
                status = "command bridge and message file are available";
            }
            else
            {
                var currentWindow = ctx.Session.Windows.CurrentId();
                status = currentWindow is { } id
                    ? $"command bridge, message file, Creo session, and current window {id} are available"
                    : "command bridge, message file, and Creo session are available; no current window id";
            }

            ctx.Messages.Info($"Install check OK: {status}");
        });

    }
}
