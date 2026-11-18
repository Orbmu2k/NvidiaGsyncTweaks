using NvidiaGsyncTweaks.Native;

namespace NvidiaGsyncTweaks.Services;

/// <summary>
/// Headless worker: the same exe, started elevated for a single action ("Apply" / "Apply &amp; restart driver" / "Restart driver").
/// The normal UI process never needs administrator rights; it reads the registry and NVAPI as a standard user.
/// </summary>
internal static class ElevatedWorker
{
    public static int Run(string[] args)
    {
        if (!ElevatedRequest.TryParse(args, out var request, out _) || request is null)
            return (int)ElevatedExit.InvalidArguments;

        // the key must belong to a present NVIDIA display adapter - looked up here, not taken from the caller
        var adapter = DisplayAdapters.Enumerate().FirstOrDefault(a =>
            a.VendorId == 0x10DE &&
            string.Equals(@"SYSTEM\CurrentControlSet\Control\Class\" + a.DriverKey, request.KeyPath, StringComparison.OrdinalIgnoreCase));
        if (adapter is null) return (int)ElevatedExit.NotNvidiaKey;

        if (request.Changes.Count > 0)
        {
            try { new RegistryStore(request.KeyPath).Apply(request.Changes); }
            catch { return (int)ElevatedExit.RegistryFailed; }
        }

        if (request.RestartDriver)
        {
            var r = DisplayAdapters.Restart(adapter.InstanceId);
            if (!r.Success) return (int)ElevatedExit.RestartFailed;
            if (r.RebootRequired) return (int)ElevatedExit.OkRebootRequired;
        }
        return (int)ElevatedExit.Ok;
    }
}
