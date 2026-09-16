using CreoToolkit.App;
using CreoToolkit.Sdk;

namespace CreoToolkit.Samples.ElemTreeExport;

/// <summary>
/// pt.elemtree.export.xml 诊断命令: 遍历指定目录的所有模型,
/// 提取每个特征的元素树并写入 XML 文件。
/// 输入/输出目录由环境变量 CTK_ELEMTREE_INPUT_DIR / CTK_ELEMTREE_OUTPUT_DIR 控制。
/// </summary>
public static class ElemTreeExportRegistration
{
    private static readonly string[] ScanExtensions = { ".prt", ".asm" };

    public static void Register(CreoAppBuilder app)
    {
        ThrowUtil.IfNull(app);

        app.Command("pt.elemtree.export.xml",
            "PT_ELEMTREE_EXPORT_XML",
            "PT_ELEMTREE_EXPORT_XML_HELP", ctx =>
        {
            if (!ctx.RequireSession(out var session)) return;

            var inputDir = Environment.GetEnvironmentVariable("CTK_ELEMTREE_INPUT_DIR");
            var outputDir = Environment.GetEnvironmentVariable("CTK_ELEMTREE_OUTPUT_DIR");

            if (string.IsNullOrWhiteSpace(inputDir) || !Directory.Exists(inputDir))
            {
                ctx.Messages.Info("elemtree-export: CTK_ELEMTREE_INPUT_DIR 未设置或目录不存在");
                return;
            }
            if (string.IsNullOrWhiteSpace(outputDir))
            {
                ctx.Messages.Info("elemtree-export: CTK_ELEMTREE_OUTPUT_DIR 未设置");
                return;
            }

            Directory.CreateDirectory(outputDir);
            ctx.Messages.Info($"elemtree-export: input={inputDir} output={outputDir}");

            var files = Directory.EnumerateFiles(inputDir)
                .Where(f => ScanExtensions.Contains(
                    Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .OrderBy(f => f)
                .ToList();

            ctx.Messages.Info($"elemtree-export: 发现 {files.Count} 个模型文件");

            int totalFeatures = 0;
            int totalXml = 0;
            int failCount = 0;

            foreach (var filePath in files)
            {
                var fileName = Path.GetFileName(filePath);
                var ext = Path.GetExtension(filePath).ToLowerInvariant();
                var fileType = ext switch
                {
                    ".prt" => CreoModelType.Part,
                    ".asm" => CreoModelType.Assembly,
                    _ => CreoModelType.Unknown,
                };

                CreoModel? model;
                try
                {
                    model = session.Models.Load(filePath, fileType);
                }
                catch (Exception ex)
                {
                    ctx.Messages.Info($"elemtree-export: 加载失败 {fileName} - {ex.Message}");
                    failCount++;
                    continue;
                }
                if (model is null)
                {
                    ctx.Messages.Info($"elemtree-export: 加载返回 null {fileName}");
                    failCount++;
                    continue;
                }

                ctx.Messages.Info($"elemtree-export: 已加载 {model.FullName}");

                IReadOnlyList<CreoFeature> features;
                try
                {
                    features = session.Features.List(model);
                }
                catch (Exception ex)
                {
                    ctx.Messages.Info($"elemtree-export: 列特征失败 {model.FullName} - {ex.Message}");
                    failCount++;
                    continue;
                }

                ctx.Messages.Info($"elemtree-export: {model.FullName} 共 {features.Count} 个特征");
                totalFeatures += features.Count;

                foreach (var feature in features)
                {
                    var xmlName = $"{model.Name}_feat{feature.Id}.xml";
                    var xmlPath = Path.Combine(outputDir, xmlName);

                    try
                    {
                        session.Features.WriteElementTreeXml(feature, xmlPath);
                        ctx.Messages.Info($"elemtree-export: 已导出 {xmlName}");
                        totalXml++;
                    }
                    catch (Exception ex)
                    {
                        ctx.Messages.Info($"elemtree-export: 导出失败 {xmlName} - {ex.Message}");
                        failCount++;
                    }
                }
            }

            ctx.Messages.Info(
                $"elemtree-export complete: models={files.Count} features={totalFeatures} " +
                $"xml={totalXml} fail={failCount}");
        });
    }
}
