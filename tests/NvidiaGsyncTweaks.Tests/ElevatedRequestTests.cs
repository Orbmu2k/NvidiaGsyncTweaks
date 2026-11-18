using System.Diagnostics;
using Microsoft.Win32;
using NvidiaGsyncTweaks.Services;
using Xunit;

namespace NvidiaGsyncTweaks.Tests;

/// <summary>The elevated worker only ever does what these checks allow: security-relevant, so tested explicitly.</summary>
public class ElevatedRequestTests
{
    private const string GoodKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000";

    private static string[] Split(string commandLine)
    {
        // the same tokenisation Windows applies to a command line (quotes group, no escapes needed here)
        var list = new List<string>();
        var cur = new System.Text.StringBuilder();
        bool quoted = false;
        foreach (var ch in commandLine)
        {
            if (ch == '"') quoted = !quoted;
            else if (ch == ' ' && !quoted) { if (cur.Length > 0) { list.Add(cur.ToString()); cur.Clear(); } }
            else cur.Append(ch);
        }
        if (cur.Length > 0) list.Add(cur.ToString());
        return list.ToArray();
    }

    [Fact]
    public void RoundTrip_PreservesKeyChangesAndRestart()
    {
        var req = new ElevatedRequest(GoodKey,
            new Dictionary<string, uint?> { ["vrrFrameSplitBehavior"] = 0x80010055, ["VRRAlwaysOn"] = null, ["vrrMonAdj"] = 50 }, true);

        Assert.True(ElevatedRequest.TryParse(Split(req.ToArguments()), out var parsed, out var error), error);
        Assert.Equal(GoodKey, parsed!.KeyPath);
        Assert.True(parsed.RestartDriver);
        Assert.Equal(0x80010055u, parsed.Changes["vrrFrameSplitBehavior"]);
        Assert.Null(parsed.Changes["VRRAlwaysOn"]);
        Assert.True(parsed.Changes.ContainsKey("VRRAlwaysOn"));
        Assert.Equal(50u, parsed.Changes["vrrMonAdj"]);
    }

    [Fact]
    public void RestartOnly_IsAllowed()
    {
        Assert.True(ElevatedRequest.TryParse(["--elevated-apply", "--key", GoodKey, "--restart"], out var r, out _));
        Assert.Empty(r!.Changes);
    }

    [Theory]
    [InlineData(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000\Sub")]
    [InlineData(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\00001")]
    [InlineData(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\abcd")]
    [InlineData(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e965-e325-11ce-bfc1-08002be10318}\0000")]
    [InlineData(@"SYSTEM\CurrentControlSet\Services\nvlddmkm")]
    [InlineData(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run")]
    [InlineData(@"..\..\SOFTWARE")]
    [InlineData("")]
    public void ForeignOrMalformedKeys_AreRejected(string key)
    {
        Assert.False(ElevatedRequest.TryParse(["--elevated-apply", "--key", key, "--restart"], out _, out _));
    }

    [Fact]
    public void KeyIsCaseInsensitive_ButStillExactShape()
    {
        Assert.True(ElevatedRequest.IsValidKeyPath(GoodKey.ToUpperInvariant()));
        Assert.True(ElevatedRequest.IsValidKeyPath(GoodKey.Replace("0000", "0007")));
        Assert.False(ElevatedRequest.IsValidKeyPath(null));
    }

    [Theory]
    [InlineData("--set", "Run=1")]                       // not a managed value
    [InlineData("--set", "vrrMonAdj=abc")]               // not a number
    [InlineData("--set", "vrrMonAdj=-1")]                // negative
    [InlineData("--set", "vrrMonAdj=4294967296")]        // > uint
    [InlineData("--set", "=5")]                          // no name
    [InlineData("--delete", "SomethingElse")]
    [InlineData("--set", @"..\x=1")]
    public void UnknownNamesAndBadValues_AreRejected(string op, string arg)
    {
        Assert.False(ElevatedRequest.TryParse(["--elevated-apply", "--key", GoodKey, op, arg], out _, out _));
    }

    [Fact]
    public void WrongSwitch_UnknownArguments_AndEmptyRequests_AreRejected()
    {
        Assert.False(ElevatedRequest.TryParse([], out _, out _));
        Assert.False(ElevatedRequest.TryParse(["--other", "--key", GoodKey, "--restart"], out _, out _));
        Assert.False(ElevatedRequest.TryParse(["--elevated-apply", "--key", GoodKey, "--evil"], out _, out _));
        Assert.False(ElevatedRequest.TryParse(["--elevated-apply", "--key", GoodKey], out _, out _));           // nothing to do
        Assert.False(ElevatedRequest.TryParse(["--elevated-apply", "--restart"], out _, out _));                // no key
    }

    [Fact]
    public void RestoreFile_ParsesBackToTheSameValues()
    {
        var path = @"Software\NvidiaGsyncTweaksTests\" + Guid.NewGuid().ToString("N");
        var dir = Path.Combine(Path.GetTempPath(), "nvgt-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var k = Registry.CurrentUser.CreateSubKey(path)) { k.SetValue("vrrFrameSplitBehavior", unchecked((int)0x80010055), RegistryValueKind.DWord); }
            var store = new RegistryStore(path, Registry.CurrentUser) { BackupDirectory = dir };
            var file = store.WriteRestoreFile(["vrrFrameSplitBehavior", "VRRAlwaysOn"], "t");

            // a HKCU file is not accepted by the parser (only HKLM restore points exist in the real app)
            Assert.Null(RegistryStore.ParseRestoreFile(file));

            // the same content with an HKLM header is parsed
            File.WriteAllText(file, File.ReadAllText(file).Replace("HKEY_CURRENT_USER", "HKEY_LOCAL_MACHINE"), System.Text.Encoding.Unicode);
            var parsed = RegistryStore.ParseRestoreFile(file);
            Assert.NotNull(parsed);
            Assert.Equal(path, parsed!.Value.Key);
            Assert.Equal(0x80010055u, parsed.Value.Values["vrrFrameSplitBehavior"]);
            Assert.Null(parsed.Value.Values["VRRAlwaysOn"]);
            Assert.True(parsed.Value.Values.ContainsKey("VRRAlwaysOn"));
        }
        finally
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(path, false); } catch { }
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }
    }
}
