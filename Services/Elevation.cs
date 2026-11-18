using System.ComponentModel;
using System.Diagnostics;

namespace NvidiaGsyncTweaks.Services;

internal readonly record struct ElevatedRun(bool Launched, bool Cancelled, int ExitCode, string? Error)
{
    public ElevatedExit Exit => (ElevatedExit)ExitCode;
}

internal static class Elevation
{
    private const int ERROR_CANCELLED = 1223;

    /// <summary>
    /// Starts this exe elevated (UAC prompt) with the worker command line and waits for it. The UI itself never runs elevated.
    /// </summary>
    public static async Task<ElevatedRun> RunWorkerAsync(string arguments)
    {
        var exe = Environment.ProcessPath;
        if (exe is null) return new(false, false, -1, "cannot determine the path of the running exe");
        try
        {
            using var p = Process.Start(new ProcessStartInfo(exe, arguments)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = AppContext.BaseDirectory,
            });
            if (p is null) return new(false, false, -1, "the elevated process could not be started");
            await p.WaitForExitAsync();
            return new(true, false, p.ExitCode, null);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ERROR_CANCELLED)
        {
            return new(false, true, -1, null);   // user declined the UAC prompt
        }
        catch (Exception ex)
        {
            return new(false, false, -1, ex.Message);
        }
    }
}
