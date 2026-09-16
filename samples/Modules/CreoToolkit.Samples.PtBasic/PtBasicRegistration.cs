using CreoToolkit.App;

namespace CreoToolkit.Samples.PtBasic;

public static class PtBasicRegistration
{
    public static void Register(CreoAppBuilder app)
    {
        ThrowUtil.IfNull(app);
        // pt_basic/basic.c: ProMdlCurrentGet + name/extension + ProMessageDisplay。
        app.Command("pt.basic.status", "PT_BASIC_STATUS", "PT_BASIC_STATUS_HELP", ctx =>
        {
            var model = ctx.Session?.Models.GetCurrent();
            ctx.Messages.Info(model is null
                ? CreoCommandContextExtensions.NoCurrentModel
                : $"Current object: {model.FullName}; modified={model.IsModified()}");
        });

        // 自定义顶级菜单先创建;系统菜单可直接挂按钮。
    }
}
