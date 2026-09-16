namespace CreoToolkit.Samples.ProtkAppls.Core;

using CreoToolkit.App;
using CreoToolkit.Sdk;

/// <summary>Core/desktop sample 共用的目标模型配置解析，确保 launcher、测试和两种 app 入口语义一致。</summary>
internal readonly record struct SampleTargetModelOptions(string Name, CreoModelType Type)
{
    internal static SampleTargetModelOptions FromEnvironment()
        => Resolve(Environment.GetEnvironmentVariable);

    internal static SampleTargetModelOptions Resolve(Func<string, string?> readEnvironment)
    {
        ThrowUtil.IfNull(readEnvironment);

        var name = readEnvironment(CtkEnv.AppModelName);
        if (string.IsNullOrWhiteSpace(name))
            name = "exercise4";

        var type = (readEnvironment(CtkEnv.AppModelType) ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "assembly" or "asm" => CreoModelType.Assembly,
            "drawing" or "drw" => CreoModelType.Drawing,
            "layout" or "lay" => CreoModelType.Layout,
            "format" or "frm" => CreoModelType.Format,
            "diagram" or "dgm" => CreoModelType.Diagram,
            "markup" or "mrk" => CreoModelType.Markup,
            "notebook" or "nbk" => CreoModelType.Notebook,
#pragma warning disable CS0618 // 保留 harness 字符串路由以维持既有 sample 用户兼容
            "harness" => CreoModelType.Harness,
#pragma warning restore CS0618
            _ => CreoModelType.Part,
        };

        return new SampleTargetModelOptions(name, type);
    }
}
