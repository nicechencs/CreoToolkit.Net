using CreoToolkit.App;
using CreoToolkit.Host;
using CreoToolkit.NetFxProbeApp;
using Xunit;

namespace CreoToolkit.Host.Tests;

public sealed class BootstrapEntryTests
{
    [Fact]
    public void Initialize_with_string_argument_returns_zero()
    {
        var rc = Bootstrap.Initialize("");
        Assert.Equal(0, rc);
    }

    [Fact]
    public void Terminate_with_string_argument_returns_zero()
    {
        var rc = Bootstrap.Terminate("");
        Assert.Equal(0, rc);
    }

    [Fact]
    public void Loader_LoadFrom_uses_default_domain_ICreoApplication_identity()
    {
        var assemblyPath = typeof(ProbeApp).Assembly.Location;
        Environment.SetEnvironmentVariable(
            CtkEnv.AppAllowedRoots,
            Path.GetDirectoryName(assemblyPath));
        var loaded = CreoApplicationLoader.CreateLoaded(
            assemblyPath,
            typeof(ProbeApp).FullName);
        Assert.IsAssignableFrom<ICreoApplication>(loaded.App);
        Assert.IsType<ProbeApp>(loaded.App);
        Assert.Same(
            typeof(ICreoApplication).Assembly,
            loaded.App.GetType().GetInterface(nameof(ICreoApplication))!.Assembly);
        Assert.Contains(typeof(ProbeApp).Assembly, AppDomain.CurrentDomain.GetAssemblies());
    }

    [Fact]
    public void Loader_unset_env_uses_NoOpApp()
    {
        var prevAssembly = Environment.GetEnvironmentVariable(CtkEnv.AppAssembly);
        var prevType = Environment.GetEnvironmentVariable(CtkEnv.AppType);
        try
        {
            Environment.SetEnvironmentVariable(CtkEnv.AppAssembly, null);
            Environment.SetEnvironmentVariable(CtkEnv.AppType, null);
            var loaded = CreoApplicationLoader.CreateLoadedFromEnvironment();
            Assert.IsType<NoOpApp>(loaded.App);
        }
        finally
        {
            Environment.SetEnvironmentVariable(CtkEnv.AppAssembly, prevAssembly);
            Environment.SetEnvironmentVariable(CtkEnv.AppType, prevType);
        }
    }

    [Fact]
    public void Loader_env_assembly_and_type_load_ProbeApp_in_default_domain()
    {
        var assemblyPath = typeof(ProbeApp).Assembly.Location;
        var prevAssembly = Environment.GetEnvironmentVariable(CtkEnv.AppAssembly);
        var prevType = Environment.GetEnvironmentVariable(CtkEnv.AppType);
        var prevRoots = Environment.GetEnvironmentVariable(CtkEnv.AppAllowedRoots);
        try
        {
            Environment.SetEnvironmentVariable(CtkEnv.AppAssembly, assemblyPath);
            Environment.SetEnvironmentVariable(CtkEnv.AppType, typeof(ProbeApp).FullName);
            Environment.SetEnvironmentVariable(
                CtkEnv.AppAllowedRoots,
                Path.GetDirectoryName(assemblyPath));
            var loaded = CreoApplicationLoader.CreateLoadedFromEnvironment();
            Assert.IsType<ProbeApp>(loaded.App);
            Assert.Same(
                typeof(ICreoApplication).Assembly,
                loaded.App.GetType().GetInterface(nameof(ICreoApplication))!.Assembly);
            Assert.Contains(typeof(ProbeApp).Assembly, AppDomain.CurrentDomain.GetAssemblies());
        }
        finally
        {
            Environment.SetEnvironmentVariable(CtkEnv.AppAssembly, prevAssembly);
            Environment.SetEnvironmentVariable(CtkEnv.AppType, prevType);
            Environment.SetEnvironmentVariable(CtkEnv.AppAllowedRoots, prevRoots);
        }
    }

    [Fact]
    public void Diagnostics_not_requested_without_enable_or_legacy_flags()
    {
        var activation = DiagnosticsActivation.Resolve(_ => null);
        Assert.False(activation.Requested);
        Assert.False(activation.Required);
    }

    [Fact]
    public void Diagnostics_requested_when_load_model_path_set()
    {
        var activation = DiagnosticsActivation.Resolve(name =>
            name == CtkEnv.AppLoadModelPath ? @"C:\work\exercise4.prt" : null);
        Assert.True(activation.Requested);
        Assert.False(activation.Required);
    }
}
