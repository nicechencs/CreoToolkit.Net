using System;
using System.Collections.Generic;
using System.Linq;
using CreoToolkit.Samples.ProtkAppls.Core;

namespace CreoToolkit.Samples.Shell.WinForms;

// 7 主题 BrowserConfig 静态注册表（tab 分组从元数据 Dialog/Tab 驱动）。
internal static class BrowserConfigs
{
    private static readonly Dictionary<string, BrowserConfig> _map = BuildMap();

    private static Dictionary<string, BrowserConfig> BuildMap()
    {
        var titles = new Dictionary<string, (string LogModule, string FormTitle)>(StringComparer.Ordinal)
        {
            [DialogKeys.BasicSession]  = ("shell.basic-session",  "CreoToolkit · 基础与会话"),
            [DialogKeys.ModelDatabase] = ("shell.model-database", "CreoToolkit · 模型与数据库"),
            [DialogKeys.FeatureGeom]   = ("shell.feature-geom",   "CreoToolkit · 特征与几何"),
            [DialogKeys.ParamsUnits]   = ("shell.params-units",   "CreoToolkit · 参数与单位"),
            [DialogKeys.DimDrawing]    = ("shell.dim-drawing",    "CreoToolkit · 标注与工程图"),
            [DialogKeys.AsmSelect]     = ("shell.asm-select",     "CreoToolkit · 装配与选择"),
            [DialogKeys.DiagDemo]      = ("shell.diag-demo",      "CreoToolkit · 诊断与桥层 Demo"),
        };

        var map = new Dictionary<string, BrowserConfig>(StringComparer.Ordinal);
        foreach (var key in DialogKeys.All)
        {
            var (logModule, formTitle) = titles[key];
            var tabDefs = MetadataCatalogBuilder.BuildTabs(key)
                .Select(t => new TabDef(t.TabName, t.Predicate))
                .ToArray();
            map[key] = new BrowserConfig(key, logModule, formTitle, tabDefs, RedRowGuard.ConfirmIfRed);
        }
        return map;
    }

    public static BrowserConfig? GetByKey(string dialogKey)
        => _map.TryGetValue(dialogKey, out var cfg) ? cfg : null;

    public static IEnumerable<string> RegisteredKeys => _map.Keys;
}
