using System;
using System.Threading;
using System.Windows.Forms;
using CreoToolkit.App;
using CreoToolkit.Samples.AgentDemo;
using CreoToolkit.Samples.ProtkAppls.Core;
using CreoToolkit.Samples.Shell.WinForms;
using Xunit;

namespace CreoToolkit.Samples.ProtkAppls.WinForms.Tests;

/// <summary>
/// Focused lifecycle and terminal modeless-window contracts.
/// </summary>
public sealed class AgentLifecycleTests
{
    [Fact]
    public void RunSampleByName_without_host_invoker_fails_without_direct_handler_fallback()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ProtkSamplesCoreRegistration.RunSampleByName(null, "pt.agent.demo"));

        Assert.Contains("Host command invoker is unavailable", ex.Message);
    }

    [Fact]
    public void RunSampleByName_uses_the_supplied_host_invoker()
    {
        string? dispatched = null;

        ProtkSamplesCoreRegistration.RunSampleByName(commandName =>
        {
            dispatched = commandName;
            return 0;
        }, "pt.agent.demo");

        Assert.Equal("pt.agent.demo", dispatched);
    }

    [Fact]
    public void Agent_module_registration_uses_the_runtime_owned_by_the_caller()
    {
        var builder = new CreoAppBuilder();
        var runtime = new AgentDemoRuntime();

        AgentDemoRegistration.Register(builder, runtime);

        Assert.Contains(builder.Build(), command => command.Name == "pt.agent.pipe-server");
        runtime.Dispose();
    }

    [Fact]
    public void Core_registration_failure_rolls_back_without_poisoning_a_later_explicit_runtime()
    {
        var failingBuilder = new CreoAppBuilder();
        failingBuilder.Command("pt.agent.demo", "DUP", "DUP_HELP", _ => { });

        Assert.Throws<InvalidOperationException>(() =>
            ProtkSamplesCoreRegistration.Register(failingBuilder, "demo", CreoToolkit.Sdk.CreoModelType.Part, "CTK_TEST"));

        var recoveryBuilder = new CreoAppBuilder();
        var runtime = ProtkSamplesCoreRegistration.Register(
            recoveryBuilder, "demo", CreoToolkit.Sdk.CreoModelType.Part, "CTK_TEST");
        try
        {
            Assert.Contains(recoveryBuilder.Build(), command => command.Name == "pt.agent.pipe-server");
        }
        finally
        {
            runtime.Dispose();
        }
    }

    [Fact]
    public void WinForms_close_is_terminal_even_when_form_closing_is_canceled()
    {
        RunSta(() =>
        {
            var factory = new CancelingFormFactory();
            var bridge = new WinFormsDialogBridge(factory);
            bridge.Show("test", null);

            bridge.Close("test");

            Assert.NotNull(factory.Created);
            Assert.True(factory.Created!.IsDisposed);
            Assert.False(bridge.IsOpen("test"));
        });
    }

    [Fact]
    public void WinForms_bridge_dispose_is_terminal_and_rejects_future_show()
    {
        RunSta(() =>
        {
            var factory = new CancelingFormFactory();
            var bridge = new WinFormsDialogBridge(factory);
            bridge.Show("test", null);

            bridge.Dispose();
            bridge.Dispose();

            Assert.True(factory.Created!.IsDisposed);
            Assert.Throws<ObjectDisposedException>(() => bridge.Show("other", null));
        });
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "STA lifecycle test did not complete.");
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    private sealed class CancelingFormFactory : IFormFactory
    {
        public Form? Created { get; private set; }

        public Form Create(string dialogKey)
        {
            var form = new Form();
            form.FormClosing += (_, e) => e.Cancel = true;
            Created = form;
            return form;
        }
    }
}
