using System.Collections.Generic;
using System.Linq;
using CreoToolkit.App;
using CreoToolkit.App.Catalog;

namespace CreoToolkit.Samples.ProtkAppls.Core;

/// <summary>
/// 从 SampleCommandMetadata.All 构建 ICommandCatalog（替代手写 Catalog）。
/// </summary>
public static class MetadataCatalogBuilder
{
    /// <summary>按 Dialog key 分组，返回 dialogKey → ICommandCatalog 字典。</summary>
    public static Dictionary<string, ICommandCatalog> BuildAll()
    {
        var result = new Dictionary<string, ICommandCatalog>(StringComparer.Ordinal);

        var groups = SampleCommandMetadata.All
            .Where(m => m.Dialog is not null)
            .GroupBy(m => m.Dialog!, StringComparer.Ordinal);

        foreach (var g in groups)
        {
            var entries = g.Select(m => new CommandCatalogEntry(
                m.Name,
                ResolveModule(m.Name),
                m.Kind,
                m.Danger,
                m.LabelKey,
                m.ResolvedHelpKey,
                g.Key)).ToList();
            result[g.Key] = new InMemoryCommandCatalog(entries);
        }

        return result;
    }

    /// <summary>按 Dialog key + Tab 分组的 tab 定义列表。</summary>
    public static IReadOnlyList<(string TabName, Func<CommandCatalogEntry, bool> Predicate)> BuildTabs(string dialogKey)
    {
        var tabNames = SampleCommandMetadata.All
            .Where(m => m.Dialog == dialogKey && m.Tab is not null)
            .Select(m => m.Tab!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // 收集每个 tab 包含的命令名集合
        return tabNames.Select(tab =>
        {
            var names = SampleCommandMetadata.All
                .Where(m => m.Dialog == dialogKey && m.Tab == tab)
                .Select(m => m.Name)
                .ToHashSet(StringComparer.Ordinal);
            return (tab, (Func<CommandCatalogEntry, bool>)(e => names.Contains(e.Token)));
        }).ToList();
    }

    // 从命令名推导 Module 名（与原 Catalog 一致）
    private static string ResolveModule(string name)
    {
        if (name.StartsWith("pt.basic.")) return "PtBasic";
        if (name.StartsWith("pt.simple_async.")) return "PtSimpleAsync";
        if (name.StartsWith("pt.dbase.")) return "PtDbase";
        if (name.StartsWith("pt.params.")) return "PtParams";
        if (name.StartsWith("pt.feature.")) return "PtFeature";
        if (name.StartsWith("pt.select.")) return "PtSelect";
        if (name.StartsWith("pt.dimension.")) return "PtDimension";
        if (name.StartsWith("pt.gtol.")) return "PtDimension";
        if (name.StartsWith("pt.geom.")) return "PtGeom";
        if (name.StartsWith("pt.asm.")) return "PtAsm";
        if (name.StartsWith("pt.drawing.")) return "PtDrawing";
        if (name.StartsWith("pt.install.")) return "PtInstall";
        if (name.StartsWith("pt.userguide.")) return "PtUserguide";
        if (name.StartsWith("pt.material.")) return "PtPart";
        if (name.StartsWith("pt.agent.")) return "AgentDemo";
        if (name.StartsWith("ctk.probe.")) return "HostProbe";
        if (name.StartsWith("pt.units.")) return "PtParamsUnits";
        if (name.StartsWith("ext.creo-menu.")) return "ExtCreoMenu";
        if (name.StartsWith("pt.udf.")) return "PtUdfDemo";
        if (name.StartsWith("pt.model-inspect.")) return "ModelInspectDemo";
        if (name.StartsWith("pt.element.")) return "ElemTreeWalk";
        if (name.StartsWith("pt.elemtree.")) return "ElemTreeExport";
        if (name.StartsWith("pt.model.")) return "ModelSurvey";
        return "Unknown";
    }
}
