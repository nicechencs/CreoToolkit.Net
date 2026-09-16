using CreoToolkit.Interop.Diagnostics;
using CreoToolkit.Sdk;

namespace CreoToolkit.App;

/// <summary>
/// Native command callback runtime: token/name dispatch, handler isolation, and message-bar echo.
/// The host remains the startup orchestrator; this class owns only the callback boundary.
/// </summary>
internal sealed class CommandDispatchRuntime
{
    private const string LogModule = "CreoAppHost";

    private readonly Func<IReadOnlyList<CreoCommand>> _getCommands;
    private readonly CreoSession? _session;
    private readonly ICommandDispatcher _dispatcher;
    private readonly Func<string?, CreoMessages> _createMessages;
    private readonly bool _dispatchEcho;

    public CommandDispatchRuntime(
        Func<IReadOnlyList<CreoCommand>> getCommands,
        CreoSession? session,
        ICommandDispatcher dispatcher,
        Func<string?, CreoMessages> createMessages,
        bool dispatchEcho)
    {
        _getCommands = getCommands ?? throw new ArgumentNullException(nameof(getCommands));
        _session = session;
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _createMessages = createMessages ?? throw new ArgumentNullException(nameof(createMessages));
        _dispatchEcho = dispatchEcho;
    }

    public int LastFailedToken { get; private set; } = -1;

    private int _dispatchDepth;

    /// <summary>True while the reverse-P/Invoke callback is executing on this host.</summary>
    internal bool IsDispatching => Volatile.Read(ref _dispatchDepth) != 0;

    internal int InvokeByName(string name)
    {
        ThrowUtil.IfNullOrWhiteSpace(name);
        foreach (var command in GetCommands())
        {
            if (string.Equals(command.Name, name, StringComparison.Ordinal))
                return Dispatch(command.Token);
        }

        LastFailedToken = -1;
        // Keep event name stable: Agent diagnostic dispatch and host diagnostics consume the same event key.
        CreoLog.Warn($"command '{name}' not found",
            @event: "diagnostic.command.not-found", module: LogModule, layer: CreoLogLayer.App,
            props: new { command = name });
        return 2;
    }

    // Single native callback target. Managed exceptions must not cross the native boundary.
    internal int Dispatch(int token)
    {
        Interlocked.Increment(ref _dispatchDepth);
        try
        {
            return DispatchCore(token);
        }
        catch
        {
            // This is the final managed boundary. In particular, logging and
            // error-formatting failures must not escape a reverse P/Invoke.
            LastFailedToken = token;
            return 1;
        }
        finally
        {
            Interlocked.Decrement(ref _dispatchDepth);
        }
    }

    private int DispatchCore(int token)
    {
        var cmd = FindByToken(token);
        if (cmd is null)
        {
            LastFailedToken = token;
            CreoLog.Warn($"dispatch 未知 token={token}",
                @event: "command.dispatch.fail", module: LogModule, layer: CreoLogLayer.App,
                props: new { token });
            return 2;
        }

        CreoLog.Info($"dispatch 开始 cmd={cmd.Name}",
            @event: "command.dispatch.begin", module: LogModule, layer: CreoLogLayer.App,
            props: new { cmd = cmd.Name, token });

        try
        {
            _dispatcher.Invoke(() =>
            {
                var messages = _createMessages(cmd.Name);
                var ctx = new CreoCommandContext(_session, messages);
                cmd.Handler(ctx);

                // Post-command echo wins the single-line Creo message bar after business output.
                if (_dispatchEcho)
                {
                    try { messages.Info($"[ctk] done: {cmd.Name}"); }
                    catch (Exception echoEx)
                    {
                        CreoLog.Warn($"dispatch done echo 失败 cmd={cmd.Name}",
                            @event: "command.dispatch.echo.fail", module: LogModule, layer: CreoLogLayer.App,
                            props: new { cmd = cmd.Name }, err: CreoLog.WithError(echoEx));
                    }
                }
            });

            CreoLog.Info($"dispatch 完成 cmd={cmd.Name}",
                @event: "command.dispatch.ok", module: LogModule, layer: CreoLogLayer.App,
                props: new { cmd = cmd.Name, token });
            return 0;
        }
        catch (Exception ex)
        {
            LastFailedToken = token;
            CreoLog.Error($"dispatch 异常 cmd={cmd.Name}",
                @event: "command.dispatch.fail", module: LogModule, layer: CreoLogLayer.App,
                props: new { cmd = cmd.Name, token },
                err: CreoLog.WithError(ex));

            if (_dispatchEcho)
            {
                try
                {
                    var messages = _createMessages(cmd.Name);
                    messages.Info($"[ctk] command FAILED: {cmd.Name} ({ex.GetType().Name})");
                }
                catch (Exception echoEx)
                {
                    CreoLog.Warn($"dispatch fail echo 失败 cmd={cmd.Name}",
                        @event: "command.dispatch.echo.fail", module: LogModule, layer: CreoLogLayer.App,
                        props: new { cmd = cmd.Name }, err: CreoLog.WithError(echoEx));
                }
            }

            return 1;
        }
    }

    private CreoCommand? FindByToken(int token)
    {
        foreach (var command in GetCommands())
        {
            if (command.Token == token)
                return command;
        }

        return null;
    }

    private IReadOnlyList<CreoCommand> GetCommands() => _getCommands() ?? Array.Empty<CreoCommand>();
}
