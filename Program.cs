using Microsoft.UI.Reactor;
using NvidiaGsyncTweaks.Services;
using NvidiaGsyncTweaks.UI;

// Elevated worker mode: the UI (always started as a standard user) re-launches this exe elevated for exactly one
// administrator-only action. No window, no UI framework start-up.
if (args.Length > 0 && args[0] == ElevatedRequest.Switch)
    return ElevatedWorker.Run(args);

ReactorApp.Run<MainView>(new WindowSpec
{
    Title = "NVIDIA G-SYNC Tweaks",
    Width = 1180,
    Height = 820,
    MinWidth = 900,
    MinHeight = 620,
    StartPosition = WindowStartPosition.CenterOnPrimary,
});
return 0;
