using System;
using System.Collections.Generic;
using System.Windows.Forms;
using CreoToolkit.App.Catalog;

namespace CreoToolkit.Samples.Shell.WinForms;

// 7 主题 dialog 的差异点 (dialogKey/log module/标题/tab 切分/二次确认 hook) 全打包成 record;
// ShellCommandBrowser sealed 复用, 7 主题各一个 BrowserConfig 静态实例.
public sealed record BrowserConfig(
    string DialogKey,
    string LogModule,
    string FormTitle,
    IReadOnlyList<TabDef> Tabs,
    PreRunHook? PreRun = null);

// 派生 tab: 标题 + 把 entry 归到此 tab 的过滤谓词. Predicate 顺序锁定行序.
public sealed record TabDef(string Title, Func<CommandCatalogEntry, bool> Predicate);

// 二次确认 hook (Red/不可逆命令用). 返回 false 取消执行, 不写 ui.dialog.command.start.
// owner 用于 modal 子窗口 attach; 默认 null = 允许执行 (Green/Yellow 行).
public delegate bool PreRunHook(CommandCatalogEntry entry, IWin32Window owner);
