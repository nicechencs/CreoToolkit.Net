using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using CreoToolkit.Agent.Commands;
using CreoToolkit.Agent.Policy;
using CreoToolkit.Agent.Transport;
using CreoToolkit.Samples.WpfShell;
using CreoToolkit.Sdk;
using CreoToolkit.Sdk.Native;
using CreoToolkit.Sdk.Session;
using Xunit;

namespace CreoToolkit.Samples.ProtkAppls.WinForms.Tests;

public sealed class PumpAndWpfLifecycleTests
{
    [Fact]
    public void Executor_preserves_original_public_method_signatures()
    {
        Assert.NotNull(typeof(MainThreadAgentExecutor).GetMethod("Pump", Type.EmptyTypes));
        Assert.NotNull(typeof(MainThreadAgentExecutor).GetMethod("RunPumpLoop", new[] { typeof(CancellationToken) }));
    }

    [Fact]
    public void Pump_batch_is_bounded_and_cancellation_prevents_the_next_command()
    {
        using var session = new CreoSession(new CreoNativeBridge(), new SynchronousCreoDispatcher());
        using var router = new CreoCommandRouter(session, new ReadOnlyAllowPolicy());
        using var executor = new MainThreadAgentExecutor(router);
        var enqueue = typeof(MainThreadAgentExecutor).GetMethod("EnqueueAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        // The unknown verb fails inside the router without a single native DLL call.
        var first = (Task<AgentResult>)enqueue.Invoke(executor, new object[] { AgentCommand.Of("test.unknown"), CancellationToken.None })!;
        var second = (Task<AgentResult>)enqueue.Invoke(executor, new object[] { AgentCommand.Of("test.unknown"), CancellationToken.None })!;

        Assert.Equal(1, executor.Pump(1));
        Assert.True(first.IsCompleted);
        Assert.False(second.IsCompleted);
        Assert.Equal(0, executor.Pump(1, new CancellationToken(canceled: true)));
        Assert.False(second.IsCompleted);
        Assert.Equal(1, executor.Pump(1));
        Assert.True(second.IsCompleted);
    }

    [Fact]
    public void Pump_loop_observes_stop_without_waiting_for_a_client()
    {
        using var session = new CreoSession(new CreoNativeBridge(), new SynchronousCreoDispatcher());
        using var router = new CreoCommandRouter(session, new ReadOnlyAllowPolicy());
        using var executor = new MainThreadAgentExecutor(router);
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var elapsed = System.Diagnostics.Stopwatch.StartNew();

        executor.RunPumpLoop(stop.Token, 1);

        Assert.False(executor.IsPumpActive);
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void Wpf_terminal_close_overrides_cancellation_and_wrong_thread_does_not_block()
    {
        Exception? failure = null;
        var sta = new Thread(() =>
        {
            WpfDialogBridge? bridge = null;
            try
            {
                var factory = new CancelingWindowFactory();
                bridge = new WpfDialogBridge(factory);
                bridge.Show("test", null);
                var window = factory.Created!;
                var closed = false;
                window.Closed += (_, _) => closed = true;
                Exception? wrongThreadFailure = null;
                var wrongThread = new Thread(() =>
                {
                    try { bridge.Show("other", null); }
                    catch (Exception ex) { wrongThreadFailure = ex; }
                });
                wrongThread.Start();
                Assert.True(wrongThread.Join(TimeSpan.FromSeconds(3)));
                Assert.IsType<InvalidOperationException>(wrongThreadFailure);

                bridge.Dispose();
                bridge.Dispose();

                Assert.True(closed);
                Assert.False(bridge.IsOpen("test"));
                Assert.Throws<ObjectDisposedException>(() => bridge.Show("other", null));

                // Simulate Show failing after the factory/dictionary step, before
                // a presentation source exists. The standard Window is still owned.
                var partialBridge = new WpfDialogBridge(factory);
                var unshown = factory.Create("partial");
                var windows = (System.Collections.Concurrent.ConcurrentDictionary<string, System.Windows.Window>)
                    typeof(WpfDialogBridge).GetField("_windows", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(partialBridge)!;
                var unshownClosed = false;
                unshown.Closed += (_, _) => { unshownClosed = true; windows.TryRemove("partial", out _); };
                windows["partial"] = unshown;
                partialBridge.Dispose();
                Assert.True(unshownClosed, "An owned Window must be closed even when Show never completed.");
            }
            catch (Exception ex) { failure = ex; }
            finally
            {
                bridge?.Dispose();
                System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        sta.SetApartmentState(ApartmentState.STA);
        sta.Start();
        Assert.True(sta.Join(TimeSpan.FromSeconds(10)), "WPF lifecycle test did not complete.");
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class CancelingWindowFactory : IWpfWindowFactory
    {
        public System.Windows.Window? Created { get; private set; }

        public System.Windows.Window Create(string dialogKey)
        {
            var window = new System.Windows.Window();
            window.Closing += (_, e) => e.Cancel = true;
            return Created = window;
        }
    }
}
