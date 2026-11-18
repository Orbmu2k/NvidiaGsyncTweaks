using System.Diagnostics;
using Microsoft.Win32;
using NvidiaGsyncTweaks.Services;
using Xunit;

namespace NvidiaGsyncTweaks.Tests;

/// <summary>Backup / apply / restore cycle against a scratch key in HKCU (never touches the driver key). Windows only.</summary>
public sealed class RegistryStoreTests : IDisposable
{
    private readonly string _path = @"Software\NvidiaGsyncTweaksTests\" + Guid.NewGuid().ToString("N");
    private readonly string _backups = Path.Combine(Path.GetTempPath(), "nvgt-tests-" + Guid.NewGuid().ToString("N"));

    private RegistryStore Store() => new(_path, Registry.CurrentUser) { BackupDirectory = _backups };

    public void Dispose()
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(_path, false); } catch { }
        try { if (Directory.Exists(_backups)) Directory.Delete(_backups, true); } catch { }
    }

    [Fact]
    public void Apply_WritesDwords_AndNullDeletesTheValue()
    {
        using (var k = Registry.CurrentUser.CreateSubKey(_path)) k.SetValue("a", 1, RegistryValueKind.DWord);
        var s = Store();

        s.Apply(new Dictionary<string, uint?> { ["a"] = 0x80010055, ["b"] = 7 });
        var v = s.Read(["a", "b"]);
        Assert.Equal(0x80010055u, v["a"]);
        Assert.Equal(7u, v["b"]);

        using (var k = Registry.CurrentUser.OpenSubKey(_path)!)
            Assert.Equal(RegistryValueKind.DWord, k.GetValueKind("a"));

        s.Apply(new Dictionary<string, uint?> { ["a"] = null });
        Assert.Null(s.Read(["a"])["a"]);
    }

    [Fact]
    public void Read_ReturnsNull_ForMissingKeyAndValues()
    {
        var v = Store().Read(["nothing"]);
        Assert.Null(v["nothing"]);
    }

    [Fact]
    public void Apply_Throws_WhenKeyDoesNotExist()
    {
        Assert.Throws<InvalidOperationException>(() => Store().Apply(new Dictionary<string, uint?> { ["a"] = 1 }));
    }

    [Fact]
    public void RestoreFile_RestoresTheExactPreviousState()
    {
        using (var k = Registry.CurrentUser.CreateSubKey(_path)) k.SetValue("a", 1, RegistryValueKind.DWord);
        var s = Store();

        var file = s.WriteRestoreFile(["a", "b"], "unit test");
        var text = File.ReadAllText(file);
        Assert.Contains("\"a\"=dword:00000001", text);
        Assert.Contains("\"b\"=-", text);
        Assert.Contains("[HKEY_CURRENT_USER\\" + _path + "]", text);

        s.Apply(new Dictionary<string, uint?> { ["a"] = 2, ["b"] = 3 });

        var psi = new ProcessStartInfo("reg.exe", $"import \"{file}\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        using (var p = Process.Start(psi)!) { p.WaitForExit(); Assert.Equal(0, p.ExitCode); }

        var v = s.Read(["a", "b"]);
        Assert.Equal(1u, v["a"]);
        Assert.Null(v["b"]);
    }
}
