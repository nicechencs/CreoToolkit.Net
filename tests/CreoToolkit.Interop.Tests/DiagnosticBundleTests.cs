using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using CreoToolkit.App;
using CreoToolkit.App.Diagnostics;
using Xunit;

namespace CreoToolkit.Interop.Tests;

public class DiagnosticBundleTests : IDisposable
{
    private readonly string _dir;
    private readonly string? _oldEnv;
    private readonly string _oldCurrentDirectory;

    public DiagnosticBundleTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ctk-diag-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _oldEnv = Environment.GetEnvironmentVariable(CtkEnv.HostManagedLog);
        _oldCurrentDirectory = Environment.CurrentDirectory;
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(CtkEnv.HostManagedLog, _oldEnv);
        Environment.CurrentDirectory = _oldCurrentDirectory;
        try { Directory.Delete(_dir, true); } catch { /* 忽略 */ }
    }

    [Fact]
    public void Collect_Includes_Stamped_ManagedLogSeries_NewestAsCanonical()
    {
        var basePath = Path.Combine(_dir, "host-managed.log");
        var older = Path.Combine(_dir, "host-managed-20260101-000000-100.log");
        var newer = Path.Combine(_dir, "host-managed-20260102-000000-100.log");
        File.WriteAllText(older, "older-line");
        File.WriteAllText(newer, "newer-line");
        File.SetLastWriteTimeUtc(older, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(newer, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        Environment.SetEnvironmentVariable(CtkEnv.HostManagedLog, basePath);

        var zipPath = Path.Combine(_dir, "diag.zip");
        DiagnosticBundle.Collect(new DiagnosticBundleOptions(zipPath));

        using var zip = ZipFile.OpenRead(zipPath);
        var names = zip.Entries.Select(e => e.FullName).ToList();
        Assert.Contains("logs/host-managed.log", names);
        Assert.Contains("logs/managed-rolled/host-managed-20260101-000000-100.log", names);

        var entry = zip.GetEntry("logs/host-managed.log")!;
        using var reader = new StreamReader(entry.Open());
        Assert.Equal("newer-line", reader.ReadToEnd());
    }

    [Fact]
    public void Collect_Includes_Base_And_Stamped_ManagedLogs_Without_Duplicates()
    {
        var basePath = Path.Combine(_dir, "host-managed.log");
        var rolled = Path.Combine(_dir, "host-managed-20260101-000000-100.log");
        File.WriteAllText(basePath, "current-base-line");
        File.WriteAllText(rolled, "rolled-line");
        File.SetLastWriteTimeUtc(basePath, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(rolled, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Environment.SetEnvironmentVariable(CtkEnv.HostManagedLog, basePath);

        var zipPath = Path.Combine(_dir, "diag.zip");
        DiagnosticBundle.Collect(new DiagnosticBundleOptions(zipPath));

        using var zip = ZipFile.OpenRead(zipPath);
        var names = zip.Entries.Select(e => e.FullName).ToList();
        Assert.Equal(1, names.Count(name => name == "logs/host-managed.log"));
        Assert.Equal(1, names.Count(name => name == "logs/managed-rolled/host-managed-20260101-000000-100.log"));

        var canonical = zip.GetEntry("logs/host-managed.log")!;
        using var canonicalReader = new StreamReader(canonical.Open());
        Assert.Equal("current-base-line", canonicalReader.ReadToEnd());

        var rolledEntry = zip.GetEntry("logs/managed-rolled/host-managed-20260101-000000-100.log")!;
        using var rolledReader = new StreamReader(rolledEntry.Open());
        Assert.Equal("rolled-line", rolledReader.ReadToEnd());
    }

    [Fact]
    public void Collect_Preserves_Archive_When_It_Has_Same_Name_As_Active_Stamped_Log()
    {
        var basePath = Path.Combine(_dir, "host-managed.log");
        var stampedName = "host-managed-20260102-000000-100.log";
        var activeStamped = Path.Combine(_dir, stampedName);
        var archiveDir = Path.Combine(_dir, "archive");
        var archived = Path.Combine(archiveDir, stampedName);
        Directory.CreateDirectory(archiveDir);
        File.WriteAllText(activeStamped, "active-stamped-line");
        File.WriteAllText(archived, "archived-line");
        File.SetLastWriteTimeUtc(activeStamped, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(archived, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Environment.SetEnvironmentVariable(CtkEnv.HostManagedLog, basePath);

        var zipPath = Path.Combine(_dir, "diag.zip");
        DiagnosticBundle.Collect(new DiagnosticBundleOptions(zipPath));

        using var zip = ZipFile.OpenRead(zipPath);
        var activeEntryName = "logs/host-managed.log";
        var archiveEntryName = $"logs/managed-archive/{stampedName}";
        var names = zip.Entries.Select(e => e.FullName).ToList();
        Assert.Equal(1, names.Count(name => name == activeEntryName));
        Assert.Equal(1, names.Count(name => name == archiveEntryName));

        using var activeReader = new StreamReader(zip.GetEntry(activeEntryName)!.Open());
        Assert.Equal("active-stamped-line", activeReader.ReadToEnd());
        using var archiveReader = new StreamReader(zip.GetEntry(archiveEntryName)!.Open());
        Assert.Equal("archived-line", archiveReader.ReadToEnd());
    }

    [Fact]
    public void Collect_Includes_Stamped_ManagedLog_When_RelativeBaseHasNoDirectory()
    {
        Environment.CurrentDirectory = _dir;
        const string basePath = "host-managed";
        var stampedName = "host-managed-20260102-000000-100.log";
        File.WriteAllText(stampedName, "relative-stamped-line");
        Environment.SetEnvironmentVariable(CtkEnv.HostManagedLog, basePath);

        var zipPath = Path.Combine(_dir, "diag-relative.zip");
        DiagnosticBundle.Collect(new DiagnosticBundleOptions(zipPath));

        using var zip = ZipFile.OpenRead(zipPath);
        using var reader = new StreamReader(zip.GetEntry("logs/host-managed.log")!.Open());
        Assert.Equal("relative-stamped-line", reader.ReadToEnd());
    }

    [Fact]
    public void Collect_Includes_LogExtension_For_RelativeBaseWithoutExtension()
    {
        Environment.CurrentDirectory = _dir;
        const string basePath = "host-managed";
        File.WriteAllText("host-managed.log", "relative-literal-line");
        Environment.SetEnvironmentVariable(CtkEnv.HostManagedLog, basePath);

        var zipPath = Path.Combine(_dir, "diag-relative-literal.zip");
        DiagnosticBundle.Collect(new DiagnosticBundleOptions(zipPath));

        using var zip = ZipFile.OpenRead(zipPath);
        using var reader = new StreamReader(zip.GetEntry("logs/host-managed.log")!.Open());
        Assert.Equal("relative-literal-line", reader.ReadToEnd());
    }

    [Fact]
    public void Collect_Includes_Archive_For_RelativeBase_WithDirectory()
    {
        Environment.CurrentDirectory = _dir;
        const string basePath = "logs/host-managed.log";
        var archiveDir = Path.Combine(_dir, "logs", "archive");
        var archivedName = "host-managed-20260101-000000-100.log";
        Directory.CreateDirectory(archiveDir);
        File.WriteAllText(Path.Combine(archiveDir, archivedName), "relative-archive-line");
        Environment.SetEnvironmentVariable(CtkEnv.HostManagedLog, basePath);

        var zipPath = Path.Combine(_dir, "diag-relative-archive.zip");
        DiagnosticBundle.Collect(new DiagnosticBundleOptions(zipPath));

        using var zip = ZipFile.OpenRead(zipPath);
        var archiveEntryName = $"logs/managed-archive/{archivedName}";
        using var reader = new StreamReader(zip.GetEntry(archiveEntryName)!.Open());
        Assert.Equal("relative-archive-line", reader.ReadToEnd());
    }
}
