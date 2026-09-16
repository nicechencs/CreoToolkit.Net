using CreoToolkit.Agent.Commands;
using CreoToolkit.Agent.Handles;
using CreoToolkit.Sdk;
using CreoToolkit.Sdk.Errors;

namespace CreoToolkit.Agent.Verbs;

internal static class SelectionPickProgrammaticHandler
{
    public static IReadOnlyDictionary<string, object?> Handle(
        CreoSession session,
        AgentCommand command,
        HandleRegistry registry)
    {
        ThrowUtil.IfNull(session);
        ThrowUtil.IfNull(command);
        ThrowUtil.IfNull(registry);

        // itemRefs 可选：缺省或空数组=选择当前模型；非空=稳定错误（无字符串解析语法）
        var itemRefs = AgentArgs.GetStringArray(command, "itemRefs", required: false);
        if (itemRefs is { Count: > 0 })
            throw new CreoException("Agent.SelectionPick", ProError.BadInputs,
                "itemRefs 解析未实现，请改用交互 verb 或 CreateModelItemSelection");

        var ttlSeconds = AgentArgs.GetIntOrNull(command, "ttlSeconds");
        long? ttlAt = ttlSeconds is int s
            ? DateTimeOffset.UtcNow.AddSeconds(s).ToUnixTimeMilliseconds()
            : null;

        // 本 verb 是程序化契约：只做 model-only selection，绝不进入交互式 ProSelect（不可阻塞 UI）
        var model = session.Models.GetCurrent()
            ?? throw new CreoException("Agent.SelectionPick", ProError.BadContext, "无当前模型，无法创建程序化 selection");
        var set = session.Selections.CreateModelSelection(model);

        try
        {
            var token = registry.Track(set, "Selection", ttlAt);
            var items = new object[set.Count];
            for (var i = 0; i < set.Count; i++)
            {
                var sel = set[i];
                items[i] = new Dictionary<string, object?>
                {
                    ["index"] = i,
                    ["label"] = sel.Label,
                    ["type"] = sel.SelItem?.Type.ToString(),
                };
            }

            return new Dictionary<string, object?>
            {
                ["token"] = token,
                ["count"] = set.Count,
                ["items"] = items,
            };
        }
        catch
        {
            set.Dispose();
            throw;
        }
    }
}
