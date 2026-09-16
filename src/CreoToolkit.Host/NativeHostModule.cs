using System.Runtime.InteropServices;
using System.Text;
using CreoToolkit.App;

namespace CreoToolkit.Host;

/// <summary>
/// Binds managed P/Invokes to the single NativeHost module registered by Creo.
/// </summary>
internal static class NativeHostModule
{
    private const uint LoadLibrarySearchUserDirs = 0x00000400;
    private const int MaxModulePathCapacity = 32768;
    private static readonly object LoadGate = new();

    // AddDllDirectory has no automatic lifetime. This cookie is deliberately retained
    // until process exit when the managed fallback had to configure a search directory.
    private static IntPtr s_processDirectoryCookie;
    private static bool s_initialized;

    internal static void EnsureLoaded()
    {
        lock (LoadGate)
        {
            if (s_initialized)
                return;

            EnsureLoadedOnce(ref s_initialized, ResolvePath(), Win32.Instance);
        }
    }

    internal static void EnsureLoadedOnce(ref bool initialized, string resolvedPath, IWin32 win32)
    {
        if (initialized)
            return;

        EnsureLoaded(resolvedPath, win32);
        initialized = true;
    }

    // Narrow Win32 seam for loader behaviour tests. Production only reaches this from
    // EnsureLoaded above, which supplies process-once synchronization.
    internal static void EnsureLoaded(string resolvedPath, IWin32 win32)
    {
        var expectedPath = Path.GetFullPath(resolvedPath);
        var moduleName = Path.GetFileName(expectedPath);
        if (string.IsNullOrWhiteSpace(moduleName))
            throw new DllNotFoundException($"NativeHost path '{resolvedPath}' has no file name.");

        var existingModule = win32.GetModuleHandle(moduleName);
        if (existingModule != IntPtr.Zero)
        {
            var existingPath = win32.GetModuleFileName(existingModule);
            if (string.IsNullOrWhiteSpace(existingPath))
            {
                throw new DllNotFoundException(
                    $"NativeHost module '{moduleName}' is already loaded, but its full path could not be read (win32={win32.GetLastError()}).");
            }

            if (!PathsMatch(existingPath, expectedPath))
            {
                throw new InvalidOperationException(
                    $"NativeHost module '{moduleName}' is already loaded from '{existingPath}', but the resolved path is '{expectedPath}'. " +
                    "A same-name NativeHost from a different directory cannot be used in this process.");
            }

            return;
        }

        var directory = Path.GetDirectoryName(expectedPath);
        if (string.IsNullOrWhiteSpace(directory))
            throw new DllNotFoundException($"NativeHost path '{expectedPath}' has no directory.");

        // Do not change Creo's existing SetDllDirectory configuration. The explicit
        // fallback directory is scoped by a cookie and used by LoadLibraryEx below.
        var directoryCookie = win32.AddDllDirectory(directory);
        if (directoryCookie == IntPtr.Zero)
        {
            throw new DllNotFoundException(
                $"AddDllDirectory('{directory}') failed while preparing NativeHost P/Invoke resolution (win32={win32.GetLastError()}).");
        }

        var loadedModule = IntPtr.Zero;
        var completed = false;
        try
        {
            loadedModule = win32.LoadLibrary(expectedPath);
            if (loadedModule == IntPtr.Zero)
                loadedModule = win32.LoadLibraryEx(expectedPath, LoadLibrarySearchUserDirs);
            if (loadedModule == IntPtr.Zero)
            {
                throw new DllNotFoundException(
                    $"LoadLibraryW('{expectedPath}') failed win32={win32.GetLastError()}.");
            }

            var loadedPath = win32.GetModuleFileName(loadedModule);
            if (string.IsNullOrWhiteSpace(loadedPath) || !PathsMatch(loadedPath, expectedPath))
            {
                throw new InvalidOperationException(
                    $"NativeHost load resolved to '{loadedPath}', but the required path is '{expectedPath}'. " +
                    "A same-name NativeHost from a different directory cannot be used in this process.");
            }

            // Both the module reference and directory cookie intentionally remain
            // process-owned after a verified fallback load.
            s_processDirectoryCookie = directoryCookie;
            completed = true;
        }
        finally
        {
            if (!completed)
            {
                // Only loadedModule comes from our LoadLibrary/LoadLibraryEx calls;
                // never free the borrowed GetModuleHandle result checked above.
                if (loadedModule != IntPtr.Zero)
                    win32.FreeLibrary(loadedModule);

                win32.RemoveDllDirectory(directoryCookie);
            }
        }
    }

    internal static string ResolvePath()
    {
        var envPath = Environment.GetEnvironmentVariable(CtkEnv.HostNativeDll);
        if (!string.IsNullOrWhiteSpace(envPath))
        {
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(envPath);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                throw new DllNotFoundException(
                    $"{CtkEnv.HostNativeDll} is not a valid NativeHost path: '{envPath}'.", ex);
            }

            if (!File.Exists(fullPath))
            {
                throw new DllNotFoundException(
                    $"{CtkEnv.HostNativeDll} explicitly specifies '{fullPath}', but that file does not exist.");
            }

            return fullPath;
        }

        var hostDir = Path.GetDirectoryName(typeof(NativeHostModule).Assembly.Location);
        if (!string.IsNullOrEmpty(hostDir))
        {
            var sibling = Path.GetFullPath(Path.Combine(hostDir, "..", "native", "CreoToolkit.NativeHost.dll"));
            if (File.Exists(sibling))
                return sibling;
            var colocated = Path.Combine(hostDir, "CreoToolkit.NativeHost.dll");
            if (File.Exists(colocated))
                return colocated;
        }

        throw new DllNotFoundException(
            "CreoToolkit.NativeHost.dll not found. Set CTK_HOST_NATIVE_DLL to the loaded native module.");
    }

    private static bool PathsMatch(string left, string right)
        => string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            StringComparison.OrdinalIgnoreCase);

    internal interface IWin32
    {
        IntPtr GetModuleHandle(string moduleName);
        string GetModuleFileName(IntPtr module);
        IntPtr AddDllDirectory(string directory);
        bool RemoveDllDirectory(IntPtr cookie);
        IntPtr LoadLibrary(string path);
        IntPtr LoadLibraryEx(string path, uint flags);
        bool FreeLibrary(IntPtr module);
        int GetLastError();
    }

    private sealed class Win32 : IWin32
    {
        internal static readonly Win32 Instance = new();

        public IntPtr GetModuleHandle(string moduleName) => GetModuleHandleW(moduleName);

        public string GetModuleFileName(IntPtr module)
        {
            for (var capacity = 260; ; capacity = Math.Min(capacity * 2, MaxModulePathCapacity))
            {
                var path = new StringBuilder(capacity);
                var length = GetModuleFileNameW(module, path, capacity);
                if (length == 0)
                    return string.Empty;
                if (length < capacity)
                    return path.ToString();
                if (capacity == MaxModulePathCapacity)
                    return string.Empty;
            }
        }

        public IntPtr AddDllDirectory(string directory) => AddDllDirectoryNative(directory);
        public bool RemoveDllDirectory(IntPtr cookie) => RemoveDllDirectoryNative(cookie);
        public IntPtr LoadLibrary(string path) => LoadLibraryW(path);
        public IntPtr LoadLibraryEx(string path, uint flags) => LoadLibraryExW(path, IntPtr.Zero, flags);
        public bool FreeLibrary(IntPtr module) => FreeLibraryNative(module);
        public int GetLastError() => Marshal.GetLastWin32Error();

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr GetModuleHandleW(string lpModuleName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetModuleFileNameW(IntPtr hModule, StringBuilder lpFilename, int nSize);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryW(string lpFileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryExW(string lpFileName, IntPtr hFile, uint dwFlags);

        [DllImport("kernel32.dll", EntryPoint = "AddDllDirectory", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr AddDllDirectoryNative(string newDirectory);

        [DllImport("kernel32.dll", EntryPoint = "RemoveDllDirectory", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RemoveDllDirectoryNative(IntPtr cookie);

        [DllImport("kernel32.dll", EntryPoint = "FreeLibrary", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FreeLibraryNative(IntPtr hLibModule);
    }
}
