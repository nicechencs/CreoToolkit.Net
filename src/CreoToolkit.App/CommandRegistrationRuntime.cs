using CreoToolkit.App.Errors;
using CreoToolkit.Interop;
using CreoToolkit.Interop.Diagnostics;

namespace CreoToolkit.App;

/// <summary>命令注册的硬失败阶段；与可降级的菜单外观注册隔离。</summary>
internal static class CommandRegistrationRuntime
{
    internal static Dictionary<string, nint> Register(
        IReadOnlyList<CreoCommand> commands,
        ICommandBridge bridge,
        string msgFile)
    {
        ThrowUtil.IfNull(commands);
        ThrowUtil.IfNull(bridge);

        var commandIds = new Dictionary<string, nint>(commands.Count, StringComparer.Ordinal);
        foreach (var command in commands)
        {
            var rc = bridge.CommandActionAdd(
                command.Name,
                command.Token,
                priority: 5,
                allowInNonActiveWindow: true,
                allowInAccessoryWindow: false,
                out var commandId);
            CreoLog.Info($"CommandActionAdd 尝试 cmd={command.Name}",
                @event: "command.action.add.try", module: "CreoAppHost", layer: CreoLogLayer.App,
                props: new { cmd = command.Name, token = command.Token, rc, cmdId = (long)commandId });
            Check(rc, $"CommandActionAdd[{command.Name}]");
            if (commandId == 0)
                throw new CreoBridgeException($"CommandActionAdd[{command.Name}] returned rc=0 but cmdId=0");

            commandIds[command.Name] = commandId;
            CreoLog.Info($"CommandDesignate 尝试 cmd={command.Name} label={command.LabelKey} msgFile={msgFile}",
                @event: "command.designate.try", module: "CreoAppHost", layer: CreoLogLayer.App,
                props: new
                {
                    cmd = command.Name,
                    labelKey = command.LabelKey,
                    helpKey = command.HelpKey,
                    descKey = command.LabelKey,
                    msgFile,
                    cmdId = (long)commandId,
                });
            var designateRc = bridge.CommandDesignate(
                commandId, command.LabelKey, command.HelpKey, command.LabelKey, msgFile);
            Check(designateRc,
                $"CommandDesignate[{command.Name}] labelKey={command.LabelKey} helpKey={command.HelpKey} msgFile={msgFile}");
            CreoLog.Info($"CommandDesignate cmd={command.Name}",
                @event: "command.designate.ok", module: "CreoAppHost", layer: CreoLogLayer.App,
                props: new { cmd = command.Name, label = command.LabelKey, rc = designateRc });
        }

        return commandIds;
    }

    private static void Check(int rc, string operation)
    {
        if (rc != 0)
            throw new CreoBridgeException($"{operation} failed: rc={rc}");
    }
}
