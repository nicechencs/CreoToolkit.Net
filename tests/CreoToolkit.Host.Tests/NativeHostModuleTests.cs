using CreoToolkit.App;
using CreoToolkit.Host;
using System.Reflection;
using System.Runtime.InteropServices;
using Xunit;

namespace CreoToolkit.Host.Tests;

public sealed class NativeHostModuleTests
{
    [Fact]
    public void EnsureLoaded_reuses_existing_module_when_full_path_matches()
    {
        var native = new FakeWin32 { ExistingModule = new IntPtr(1), ExistingPath = @"C:\Creo\native\CreoToolkit.NativeHost.dll" };

        NativeHostModule.EnsureLoaded(@"C:\Creo\native\CreoToolkit.NativeHost.dll", native);

        Assert.Equal(0, native.LoadLibraryCalls);
        Assert.Equal(0, native.LoadLibraryExCalls);
        Assert.Equal(0, native.AddDllDirectoryCalls);
        Assert.Equal(0, native.FreeLibraryCalls);
    }

    [Fact]
    public void EnsureLoaded_rejects_same_name_module_from_another_directory()
    {
        var native = new FakeWin32 { ExistingModule = new IntPtr(1), ExistingPath = @"C:\Other\CreoToolkit.NativeHost.dll" };

        var error = Assert.Throws<InvalidOperationException>(
            () => NativeHostModule.EnsureLoaded(@"C:\Creo\native\CreoToolkit.NativeHost.dll", native));

        Assert.Contains(@"C:\Other\CreoToolkit.NativeHost.dll", error.Message);
        Assert.Equal(0, native.LoadLibraryCalls);
    }

    [Fact]
    public void EnsureLoaded_loads_missing_module_and_configures_search_directory()
    {
        var native = new FakeWin32
        {
            LoadLibraryResult = new IntPtr(2),
            LoadedPath = @"C:\Creo\native\CreoToolkit.NativeHost.dll",
        };

        NativeHostModule.EnsureLoaded(@"C:\Creo\native\CreoToolkit.NativeHost.dll", native);

        Assert.Equal(1, native.AddDllDirectoryCalls);
        Assert.Equal(1, native.LoadLibraryCalls);
        Assert.Equal(0, native.LoadLibraryExCalls);
    }

    [Fact]
    public void EnsureLoadedOnce_repeated_calls_are_process_idempotent()
    {
        var initialized = false;
        var native = new FakeWin32
        {
            LoadLibraryResult = new IntPtr(2),
            LoadedPath = @"C:\Creo\native\CreoToolkit.NativeHost.dll",
        };

        NativeHostModule.EnsureLoadedOnce(ref initialized, @"C:\Creo\native\CreoToolkit.NativeHost.dll", native);
        NativeHostModule.EnsureLoadedOnce(ref initialized, @"C:\Creo\native\CreoToolkit.NativeHost.dll", native);

        Assert.True(initialized);
        Assert.Equal(1, native.LoadLibraryCalls);
        Assert.Equal(1, native.AddDllDirectoryCalls);
    }

    [Fact]
    public void EnsureLoaded_removes_directory_cookie_when_both_load_attempts_fail()
    {
        var native = new FakeWin32();

        Assert.Throws<DllNotFoundException>(
            () => NativeHostModule.EnsureLoaded(@"C:\Creo\native\CreoToolkit.NativeHost.dll", native));

        Assert.Equal(1, native.AddDllDirectoryCalls);
        Assert.Equal(1, native.LoadLibraryCalls);
        Assert.Equal(1, native.LoadLibraryExCalls);
        Assert.Equal(1, native.RemoveDllDirectoryCalls);
        Assert.Equal(0, native.FreeLibraryCalls);
    }

    [Fact]
    public void EnsureLoadedOnce_retries_after_partial_failure_without_leaking_directory_cookie()
    {
        var initialized = false;
        var native = new FakeWin32();

        Assert.Throws<DllNotFoundException>(
            () => NativeHostModule.EnsureLoadedOnce(ref initialized, @"C:\Creo\native\CreoToolkit.NativeHost.dll", native));

        native.LoadLibraryResult = new IntPtr(2);
        native.LoadedPath = @"C:\Creo\native\CreoToolkit.NativeHost.dll";
        NativeHostModule.EnsureLoadedOnce(ref initialized, @"C:\Creo\native\CreoToolkit.NativeHost.dll", native);

        Assert.True(initialized);
        Assert.Equal(2, native.AddDllDirectoryCalls);
        Assert.Equal(1, native.RemoveDllDirectoryCalls);
        Assert.Equal(2, native.LoadLibraryCalls);
        Assert.Equal(1, native.LoadLibraryExCalls);
    }

    [Fact]
    public void EnsureLoaded_releases_owned_module_and_directory_cookie_when_path_verification_fails()
    {
        var native = new FakeWin32
        {
            LoadLibraryResult = new IntPtr(2),
            LoadedPath = @"C:\Other\CreoToolkit.NativeHost.dll",
        };

        Assert.Throws<InvalidOperationException>(
            () => NativeHostModule.EnsureLoaded(@"C:\Creo\native\CreoToolkit.NativeHost.dll", native));

        Assert.Equal(new IntPtr(2), native.FreedModule);
        Assert.Equal(1, native.FreeLibraryCalls);
        Assert.Equal(1, native.RemoveDllDirectoryCalls);
    }

    [Fact]
    public void AddDllDirectory_pinvoke_uses_kernel32_export_name()
    {
        var win32Type = typeof(NativeHostModule).GetNestedType("Win32", BindingFlags.NonPublic)!;
        var method = win32Type.GetMethod("AddDllDirectoryNative", BindingFlags.NonPublic | BindingFlags.Static)!;
        var import = method.GetCustomAttribute<DllImportAttribute>()!;

        Assert.Equal("AddDllDirectory", import.EntryPoint);
        Assert.True(import.ExactSpelling);
        Assert.Equal(CharSet.Unicode, import.CharSet);
    }

    [Fact]
    public void ResolvePath_rejects_invalid_explicit_environment_path_without_fallback()
    {
        var previous = Environment.GetEnvironmentVariable(CtkEnv.HostNativeDll);
        try
        {
            Environment.SetEnvironmentVariable(CtkEnv.HostNativeDll, @"C:\missing\CreoToolkit.NativeHost.dll");

            var error = Assert.Throws<DllNotFoundException>(() => NativeHostModule.ResolvePath());

            Assert.Contains(CtkEnv.HostNativeDll, error.Message);
            Assert.Contains("does not exist", error.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable(CtkEnv.HostNativeDll, previous);
        }
    }

    private sealed class FakeWin32 : NativeHostModule.IWin32
    {
        internal IntPtr ExistingModule { get; set; }
        internal string ExistingPath { get; set; } = string.Empty;
        internal string LoadedPath { get; set; } = string.Empty;
        internal IntPtr LoadLibraryResult { get; set; }
        internal int LoadLibraryCalls { get; private set; }
        internal int LoadLibraryExCalls { get; private set; }
        internal int AddDllDirectoryCalls { get; private set; }
        internal int RemoveDllDirectoryCalls { get; private set; }
        internal int FreeLibraryCalls { get; private set; }
        internal IntPtr FreedModule { get; private set; }

        public IntPtr GetModuleHandle(string moduleName) => ExistingModule;
        public string GetModuleFileName(IntPtr module)
            => module == ExistingModule ? ExistingPath : LoadedPath;
        public IntPtr AddDllDirectory(string directory)
        {
            AddDllDirectoryCalls++;
            return new IntPtr(AddDllDirectoryCalls + 2);
        }

        public bool RemoveDllDirectory(IntPtr cookie)
        {
            RemoveDllDirectoryCalls++;
            return true;
        }

        public IntPtr LoadLibrary(string path)
        {
            LoadLibraryCalls++;
            return LoadLibraryResult;
        }

        public IntPtr LoadLibraryEx(string path, uint flags)
        {
            LoadLibraryExCalls++;
            return IntPtr.Zero;
        }

        public bool FreeLibrary(IntPtr module)
        {
            FreeLibraryCalls++;
            FreedModule = module;
            return true;
        }

        public int GetLastError() => 126;
    }
}
