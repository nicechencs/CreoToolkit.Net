using System.Runtime.InteropServices;
using System.Reflection;
using CreoToolkit.App;
using CreoToolkit.Host;
using CreoToolkit.Interop;
using CreoToolkit.Sdk;
using CreoToolkit.Sdk.Native;
using CreoToolkit.Sdk.Session;
using Xunit;

namespace CreoToolkit.Host.Tests;

public sealed class CommandLifecycleTests
{
    [Fact]
    public void Repeated_run_fails_before_reinitializing_application_or_bridge()
    {
        var bridge = new CapacityBridge();
        var app = new OneCommandApp(_ => { });
        using var host = CreoAppHost.ForTest(app, bridge, new InlineDispatcher(), "msg.txt");
        host.Run();

        Assert.Throws<InvalidOperationException>(host.Run);
        Assert.Equal(1, app.InitializeCalls);
        Assert.Equal(2, bridge.Events.Count);
    }

    [Fact]
    public void Failed_bridge_dispose_throws_and_retains_dispatch_until_retry()
    {
        var bridge = new CapacityBridge { FailDispose = true };
        var calls = 0;
        var host = CreoAppHost.ForTest(new OneCommandApp(_ => calls++), bridge, new InlineDispatcher(), "msg.txt");
        try
        {
            host.Run();
            Assert.Throws<InvalidOperationException>(host.Dispose);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            var dispatch = Marshal.GetDelegateForFunctionPointer<CtkManagedCommandDispatch>(bridge.DispatchPointer);
            Assert.Equal(0, dispatch(0));
            Assert.Equal(1, calls);
        }
        finally
        {
            bridge.FailDispose = false;
            host.Dispose();
        }
        Assert.Equal(2, bridge.DisposeCalls);
    }

    [Fact]
    public void Bootstrap_failed_terminate_retains_host_and_open_session()
    {
        var hostField = typeof(Bootstrap).GetField("_appHost", BindingFlags.NonPublic | BindingFlags.Static)!;
        var sessionField = typeof(Bootstrap).GetField("_session", BindingFlags.NonPublic | BindingFlags.Static)!;
        var originalHost = hostField.GetValue(null);
        var originalSession = sessionField.GetValue(null);
        var bridge = new CapacityBridge { FailDispose = true };
        var session = new CreoSession(new CreoNativeBridge(), new SynchronousCreoDispatcher());
        var app = new OneCommandApp(_ => { });
        var host = CreoAppHost.ForTest(app, bridge, new InlineDispatcher(), "msg.txt", session);
        try
        {
            host.Run();
            hostField.SetValue(null, host);
            sessionField.SetValue(null, session);

            Assert.Equal(-1, Bootstrap.Terminate(""));
            Assert.Same(host, hostField.GetValue(null));
            Assert.Same(session, sessionField.GetValue(null));
            session.RunOnMainThread(() => { });

            bridge.FailDispose = false;
            Assert.Equal(0, Bootstrap.Terminate(""));
            Assert.Equal(1, app.TerminateCalls);
            Assert.Null(hostField.GetValue(null));
            Assert.Null(sessionField.GetValue(null));
            Assert.Throws<CreoToolkit.Sdk.Errors.CreoSessionClosedException>(() => session.RunOnMainThread(() => { }));
        }
        finally
        {
            bridge.FailDispose = false;
            host.Dispose();
            session.Dispose();
            hostField.SetValue(null, originalHost);
            sessionField.SetValue(null, originalSession);
        }
    }

    [Fact]
    public void Never_initialized_native_bridge_dispose_does_not_call_native_terminate()
    {
        var bridge = new NativeCommandBridge();
        Exception? wrongThreadFailure = null;
        var wrongThread = new Thread(() => wrongThreadFailure = Record.Exception(() => bridge.BridgeInitialize(0)));
        wrongThread.Start();
        Assert.True(wrongThread.Join(TimeSpan.FromSeconds(5)));
        Assert.IsType<InvalidOperationException>(wrongThreadFailure);
        bridge.Dispose();
        bridge.Dispose();
        Assert.Throws<ObjectDisposedException>(() => bridge.BridgeInitialize(0));
    }

    [Fact]
    public void Run_reserves_complete_capacity_before_any_command_registration()
    {
        var bridge = new CapacityBridge();
        using var host = CreoAppHost.ForTest(new OneCommandApp(_ => { }), bridge, new InlineDispatcher(), "msg.txt");

        host.Run();

        Assert.Equal(1, bridge.ReservedCount);
        Assert.Equal(new[] { "reserve:1", "add:command.one" }, bridge.Events);
    }

    [Fact]
    public void Managed_callback_exception_is_returned_not_thrown_across_function_pointer()
    {
        var bridge = new CapacityBridge();
        using var host = CreoAppHost.ForTest(
            new OneCommandApp(_ => throw new InvalidOperationException("handler failure")),
            bridge, new InlineDispatcher(), "msg.txt");
        host.Run();

        var dispatch = Marshal.GetDelegateForFunctionPointer<CtkManagedCommandDispatch>(bridge.DispatchPointer);
        var exception = Record.Exception(() => Assert.Equal(1, dispatch(0)));

        Assert.Null(exception);
    }

    [Fact]
    public void Dispose_from_command_callback_fails_fast_and_keeps_bridge_root_alive()
    {
        var bridge = new CapacityBridge();
        CreoAppHost? host = null;
        host = CreoAppHost.ForTest(new OneCommandApp(_ => host!.Dispose()), bridge, new InlineDispatcher(), "msg.txt");
        try
        {
            host.Run();
            var dispatch = Marshal.GetDelegateForFunctionPointer<CtkManagedCommandDispatch>(bridge.DispatchPointer);

            Assert.Equal(1, dispatch(0));
            Assert.Equal(0, bridge.DisposeCalls);

            host.Dispose();
            Assert.Equal(1, bridge.DisposeCalls);
        }
        finally
        {
            host.Dispose();
        }
    }

    [Fact]
    public void Wrong_thread_dispose_does_not_mark_host_disposed()
    {
        var bridge = new CapacityBridge();
        using var host = CreoAppHost.ForTest(new OneCommandApp(_ => { }), bridge, new InlineDispatcher(), "msg.txt");
        host.Run();

        Exception? wrongThreadException = null;
        var thread = new Thread(() => wrongThreadException = Record.Exception(host.Dispose));
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));

        Assert.IsType<InvalidOperationException>(wrongThreadException);
        Assert.Equal(0, bridge.DisposeCalls);
        host.Dispose();
        Assert.Equal(1, bridge.DisposeCalls);
    }

    private sealed class OneCommandApp : ICreoApplication
    {
        private readonly CreoCommandHandler _handler;
        public int InitializeCalls { get; private set; }
        public int TerminateCalls { get; private set; }

        public OneCommandApp(CreoCommandHandler handler) => _handler = handler;

        public void Initialize(CreoAppBuilder app)
        {
            InitializeCalls++;
            app.Command("command.one", "COMMAND_ONE", "COMMAND_ONE_HELP", _handler);
        }

        public void OnInitialize(CreoAppContext ctx) { }

        public void OnTerminate(CreoAppContext ctx) => TerminateCalls++;
    }

    private sealed class InlineDispatcher : ICommandDispatcher
    {
        public void Invoke(Action action) => action();
    }

    private sealed class CapacityBridge : ICommandBridge, ICommandBridgeCapacity
    {
        private int _nextCommandId;

        public nint DispatchPointer { get; private set; }
        public int ReservedCount { get; private set; } = -1;
        public int DisposeCalls { get; private set; }
        public bool FailDispose { get; set; }
        public List<string> Events { get; } = new();

        public int BridgeInitialize(nint dispatch)
        {
            DispatchPointer = dispatch;
            return 0;
        }

        public int CommandCapacityReserve(int commandCount)
        {
            ReservedCount = commandCount;
            Events.Add($"reserve:{commandCount}");
            return 0;
        }

        public int BridgeTerminate() => 0;

        public int CommandActionAdd(string actionName, int token, int priority,
            bool allowInNonActiveWindow, bool allowInAccessoryWindow, out nint commandId)
        {
            Events.Add($"add:{actionName}");
            commandId = ++_nextCommandId;
            return 0;
        }

        public int CommandDesignate(nint commandId, string labelKey, string? helpKey,
            string? descriptionKey, string? messageFile) => 0;

        public int MessageDisplay(string messageFile, string text) => 0;

        public int RibbonDefinitionfileLoad(string ribbonFile) => 0;

        public int LastCommandStatusGet(out int token, out int status)
        {
            token = -1;
            status = -1;
            return 0;
        }

        public int MenubarMenuAdd(string menuName, string menuLabel, string? neighbor,
            bool addAfter, string? messageFile) => 0;

        public int MenubarPushbuttonAdd(string parentMenu, string buttonName, string labelKey,
            string? helpKey, string? neighbor, bool addAfter, nint commandId, string? messageFile) => 0;

        public void Dispose()
        {
            DisposeCalls++;
            if (FailDispose)
                throw new InvalidOperationException("injected bridge shutdown failure");
        }
    }
}
