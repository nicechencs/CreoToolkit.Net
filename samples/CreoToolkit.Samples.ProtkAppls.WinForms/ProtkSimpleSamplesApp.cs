namespace CreoToolkit.Samples.ProtkAppls;

using System.Collections.Generic;
using CreoToolkit.App;
using CreoToolkit.App.Catalog;
using CreoToolkit.Interop.Dialogs;
using CreoToolkit.Sdk;
using CreoToolkit.Samples.ProtkAppls.Core;
using CreoToolkit.Samples.AgentDemo;
using CreoToolkit.Samples.Shell.WinForms;
// Catalogs 已由 MetadataCatalogBuilder 从元数据驱动构建
using CreoToolkit.Samples.WpfShell;

/// <summary>
/// PTC protk_appls simple 示例的双 UI 宿主 (WinForms + WPF 共存)。
/// Module 注册委托 ProtkSamplesCoreRegistration;
/// 末尾追加 7 主题 dialog launcher + CreoToolkit 父菜单接线。
/// 7 个 dialog 按 key 路由: 4 个走 WinForms (Fluent 自绘), 3 个走 WPF (WPF-UI Fluent)。
/// </summary>
public sealed class ProtkSimpleSamplesApp : ICreoApplication
{
    // WPF 路由白名单: 这 3 个 dialogKey 走 WpfDialogBridge, 其余走 WinFormsDialogBridge。
    private static readonly HashSet<string> WpfDialogKeys = new(System.StringComparer.Ordinal)
    {
        DialogKeys.ModelDatabase,
        DialogKeys.FeatureGeom,
        DialogKeys.AsmSelect,
    };

    private readonly string _modelName;
    private readonly CreoModelType _modelType;
    private readonly string _parameterName;
    private WinFormsDialogBridge? _winFormsBridge;
    private WpfDialogBridge? _wpfBridge;
    private System.Func<string, int>? _commandInvoker;
    private AgentDemoRuntime? _agentRuntime;

    public ProtkSimpleSamplesApp()
        : this(SampleTargetModelOptions.FromEnvironment(), "CTK_SAMPLE_PARAM")
    {
    }

    private ProtkSimpleSamplesApp(SampleTargetModelOptions target, string parameterName)
        : this(target.Name, target.Type, parameterName)
    {
    }

    public ProtkSimpleSamplesApp(string modelName, CreoModelType modelType, string parameterName)
    {
        ThrowUtil.IfNullOrWhiteSpace(modelName);
        ThrowUtil.IfNullOrWhiteSpace(parameterName);
        _modelName = modelName;
        _modelType = modelType;
        _parameterName = parameterName;
    }

    public void Initialize(CreoAppBuilder app)
    {
        ThrowUtil.IfNull(app);

        _agentRuntime = ProtkSamplesCoreRegistration.Register(app, _modelName, _modelType, _parameterName);

        // 7 主题 dialog launcher, 按 Pro/Toolkit 头文件家族切。
        // 命令与 MenuButton 顺序稳定。
        var launchers = new (string Cmd, string Label, string Help, string Key)[]
        {
            ("ctk.dialog.basic-session",  "CTK_DIALOG_BASIC_SESSION",  "CTK_DIALOG_BASIC_SESSION_HELP",  DialogKeys.BasicSession),
            ("ctk.dialog.model-database", "CTK_DIALOG_MODEL_DATABASE", "CTK_DIALOG_MODEL_DATABASE_HELP", DialogKeys.ModelDatabase),
            ("ctk.dialog.feature-geom",   "CTK_DIALOG_FEATURE_GEOM",   "CTK_DIALOG_FEATURE_GEOM_HELP",   DialogKeys.FeatureGeom),
            ("ctk.dialog.params-units",   "CTK_DIALOG_PARAMS_UNITS",   "CTK_DIALOG_PARAMS_UNITS_HELP",   DialogKeys.ParamsUnits),
            ("ctk.dialog.dim-drawing",    "CTK_DIALOG_DIM_DRAWING",    "CTK_DIALOG_DIM_DRAWING_HELP",    DialogKeys.DimDrawing),
            ("ctk.dialog.asm-select",     "CTK_DIALOG_ASM_SELECT",     "CTK_DIALOG_ASM_SELECT_HELP",     DialogKeys.AsmSelect),
            ("ctk.dialog.diag-demo",      "CTK_DIALOG_DIAG_DEMO",      "CTK_DIALOG_DIAG_DEMO_HELP",      DialogKeys.DiagDemo),
        };
        foreach (var l in launchers)
        {
            var dlgKey = l.Key;
            app.Command(l.Cmd, l.Label, l.Help, ctx =>
            {
                // bridge 解析或 Show 内部抛错时不能让异常冒泡到 Creo command dispatcher (会 abort 主进程),
                // 记 JSONL 错误日志后继续, 让用户看到日志而不是直接闪退。
                try
                {
                    ResolveBridge(dlgKey).Show(dlgKey, null);
                }
                catch (System.Exception ex)
                {
                    CreoAppLog.Error("dialog launcher 异常", ex,
                        @event: "ui.dialog.launcher.error", module: "shell.host",
                        props: new { dialogKey = dlgKey, commandToken = l.Cmd });
                }
            });
        }

        var catalogs = MetadataCatalogBuilder.BuildAll();

        // 两套 factory 共用同一 catalog 字典 + run/resolve 回调, 仅 UI 渲染各异。
        var winFormsFactory = new DefaultFormFactory(
            catalogs, this,
            runSampleByName: commandName => ProtkSamplesCoreRegistration.RunSampleByName(_commandInvoker, commandName),
            resolveCommandToken: commandName => ProtkSamplesCoreRegistration.ResolveCommandToken(app, commandName));
        _winFormsBridge = new WinFormsDialogBridge(winFormsFactory);

        var wpfFactory = new DefaultWpfWindowFactory(
            catalogs, this,
            runSampleByName: commandName => ProtkSamplesCoreRegistration.RunSampleByName(_commandInvoker, commandName),
            resolveCommandToken: commandName => ProtkSamplesCoreRegistration.ResolveCommandToken(app, commandName));
        _wpfBridge = new WpfDialogBridge(wpfFactory);

        app.MenuAdd(menuName: "CreoToolkit", menuLabel: "CTK_ROOT_MENU_LABEL",
            neighbor: "Help", addAfter: false);
        var buttonNames = new[] { "BasicSession", "ModelDb", "FeatureGeom", "ParamsUnits", "DimDrawing", "AsmSelect", "DiagDemo" };
        for (int i = 0; i < launchers.Length; i++)
            app.MenuButton(parentMenu: "CreoToolkit", buttonName: buttonNames[i], commandName: launchers[i].Cmd);

        // 全部命令注册完毕，后附元数据（label/help 文本 + kind/danger/dialog/tab）
        ProtkSamplesCoreRegistration.AttachAllMetadata(app);
    }

    /// <summary>绑定 Agent runtime；CTK_AGENT_PIPE=1 只开 listener,泵窗口仍须显式启动。</summary>
    public void OnInitialize(CreoAppContext ctx)
    {
        ThrowUtil.IfNull(ctx);
        _commandInvoker = ctx.InvokeCommand;
        try
        {
            (_agentRuntime ?? throw new System.InvalidOperationException("Agent runtime was not created during registration."))
                .Initialize(ctx);
        }
        catch
        {
            // CreoAppHost does not call OnTerminate when OnInitialize fails.
            // Every app-owned resource must release independently: an exception from one
            // cleanup path cannot retain the command invoker, runtime, or a modeless bridge.
            CleanupAfterInitializationFailure();
            throw;
        }
    }

    public void OnTerminate(CreoAppContext ctx)
    {
        CleanupOwnedResources();
    }

    private void CleanupAfterInitializationFailure() => CleanupOwnedResources();

    private void CleanupOwnedResources()
    {
        var runtime = _agentRuntime;
        var winForms = _winFormsBridge;
        var wpf = _wpfBridge;
        _agentRuntime = null;
        _winFormsBridge = null;
        _wpfBridge = null;
        _commandInvoker = null;

        try { runtime?.Dispose(); }
        catch (System.Exception ex) { SafeCleanupWarn("释放 Agent runtime 失败", ex); }
        try { winForms?.Dispose(); }
        catch (System.Exception ex) { SafeCleanupWarn("释放 WinForms dialogs 失败", ex); }
        try { wpf?.Dispose(); }
        catch (System.Exception ex) { SafeCleanupWarn("释放 WPF dialogs 失败", ex); }
    }

    private static void SafeCleanupWarn(string message, System.Exception ex)
    {
        try
        {
            CreoAppLog.Warn(message, @event: "app.cleanup-failed", module: "shell.host",
                props: new { error = ex.Message });
        }
        catch { /* a diagnostics failure must not stop later cleanup */ }
    }

    private IDialogBridge ResolveBridge(string dialogKey)
    {
        if (WpfDialogKeys.Contains(dialogKey))
            return _wpfBridge ?? throw new System.InvalidOperationException("WPF dialog bridge is not initialized.");
        return _winFormsBridge ?? throw new System.InvalidOperationException("WinForms dialog bridge is not initialized.");
    }
}
