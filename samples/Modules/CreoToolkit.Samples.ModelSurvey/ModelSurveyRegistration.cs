using System.Text;
using CreoToolkit.App;
using CreoToolkit.Sdk;

namespace CreoToolkit.Samples.ModelSurvey;

/// <summary>
/// pt.model.survey 诊断命令: 递归扫描工作目录中所有模型,输出特征类型分布 CSV。
/// </summary>
public static class ModelSurveyRegistration
{
    private static readonly string[] ScanExtensions = { ".prt", ".asm", ".drw" };

    private static readonly Dictionary<int, string> KnownFeatTypes = new()
    {
        [0] = "FirstFeat", [911] = "Hole", [912] = "Shaft", [913] = "Round",
        [914] = "Chamfer", [916] = "Cut", [917] = "Protrusion", [920] = "Rib",
        [922] = "Dome", [923] = "Datum", [925] = "UDF", [926] = "DatumAxis",
        [927] = "Draft", [928] = "Shell", [931] = "DatumPoint", [942] = "DatumSurf",
        [946] = "DatumQuilt", [949] = "Curve", [979] = "Csys",
        [1000] = "Component", [1010] = "Layout", [2000] = "UserFeat",
    };

    public static void Register(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        ThrowUtil.IfNull(app);

        app.Command("pt.model.survey",
            "PT_MODEL_SURVEY",
            "PT_MODEL_SURVEY_HELP", ctx =>
        {
            if (!ctx.RequireSession(out var session)) return;

            var workDir = Environment.CurrentDirectory;
            var outDir = Path.Combine(workDir, "out");
            Directory.CreateDirectory(outDir);
            var csvPath = Path.Combine(outDir, "model-survey.csv");

            var files = Directory.EnumerateFiles(workDir, "*", SearchOption.AllDirectories)
                .Where(f => ScanExtensions.Contains(
                    Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .OrderBy(f => f)
                .ToList();

            using var writer = new StreamWriter(csvPath, append: false,
                encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            writer.WriteLine("model,model_type,feature_id,feature_type,feature_type_name,status,parent_ids,child_ids");

            foreach (var filePath in files)
            {
                var ext = Path.GetExtension(filePath).ToLowerInvariant();
                var fileType = ext switch
                {
                    ".prt" => CreoModelType.Part,
                    ".asm" => CreoModelType.Assembly,
                    ".drw" => CreoModelType.Drawing,
                    _ => CreoModelType.Unknown,
                };

                CreoModel? model;
                try { model = session.Models.Load(filePath, fileType); }
                catch { continue; }
                if (model is null) continue;

                IReadOnlyList<CreoFeature> features;
                try { features = session.Features.List(model); }
                catch { continue; }

                if (features.Count == 0) continue;

                foreach (var feature in features)
                {
                    string featureType = "unknown", featureTypeName = "unknown", status = "unknown";
                    try
                    {
                        var info = feature.GetInfo();
                        if (info.HasValue)
                        {
                            featureType = info.Value.FeatureType.ToString();
                            featureTypeName = KnownFeatTypes.TryGetValue(info.Value.FeatureType, out var name)
                                ? name : $"Type_{info.Value.FeatureType}";
                            status = info.Value.Status.ToString();
                        }
                    }
                    catch { }

                    IReadOnlyList<int> parentIds = Array.Empty<int>();
                    IReadOnlyList<int> childIds = Array.Empty<int>();
                    try { parentIds = feature.GetParentIds(); } catch { }
                    try { childIds = feature.GetChildIds(); } catch { }

                    writer.WriteLine(string.Join(",",
                        Escape(model.Name), Escape(model.Type.ToString()),
                        feature.Id.ToString(), Escape(featureType), Escape(featureTypeName),
                        Escape(status), Escape(string.Join("|", parentIds)),
                        Escape(string.Join("|", childIds))));
                }
            }

            ctx.Messages.Info($"survey_csv={csvPath}");
            ctx.Messages.Info($"model-survey complete: scanned {files.Count} files");
        });
    }

    private static string Escape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}
