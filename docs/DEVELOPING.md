# Developing NVIDIA G-SYNC Tweaks

WinUI 3 app written with [Microsoft.UI.Reactor](https://www.nuget.org/packages/Microsoft.UI.Reactor) (declarative C# on WinUI 3, no XAML), .NET 10, NativeAOT release builds.
The app is framework-dependent: it uses the Windows App Runtime that is installed on the machine.

## Build

Requirements: .NET 10 SDK, the Windows App Runtime 2.x installed, for AOT the Visual Studio C++ build tools.

```powershell
dotnet build NvidiaGsyncTweaks.csproj -c Debug -p:Platform=x64                      # JIT, for development
dotnet publish NvidiaGsyncTweaks.csproj -c Release -r win-x64 -p:Platform=x64       # NativeAOT single exe (~10 MB)
```

If the AOT link step cannot find `vswhere.exe`, add `C:\Program Files (x86)\Microsoft Visual Studio\Installer` to `PATH`.

`NvidiaGsyncTweaks.slnx` opens both projects in Visual Studio (choose the **x64** platform). Visual Studio lists two files from the Windows App SDK NuGet packages
(`MddBootstrapAutoInitializer.cs`, `WindowsAppSDK-VersionInfo.cs`) as "outside the solution folder": they are compiled in by the package's build targets, are restored by NuGet and must not be committed.

## Tests

LFC model against the published numbers, tweak catalog consistency, backup / restore cycle on a scratch HKCU key, validation of the elevated worker (Windows only):

```powershell
dotnet test tests/NvidiaGsyncTweaks.Tests
```

## Layout

| folder | content |
|---|---|
| `Native/` | NVAPI (function pointers via `nvapi_QueryInterface`), SetupAPI / CfgMgr32 (adapters, driver restart), registry links, display modes |
| `Services/` | adapter resolver (NVAPI -> registry key), registry store + restore points, elevated worker |
| `Model/` | `FrameSplit` / `LfcModel` (re-implementation of the driver's multiplier selection), tweak catalog |
| `UI/` | Reactor components: shell, pages, widgets |
| `tests/` | unit tests (sources are linked, no WinUI needed) |
| `docs/` | `DRIVER-ANALYSIS.md` - reverse-engineering notes |

## Elevation model

The UI always runs as a standard user. The only administrator-only step - writing the registry values and restarting the display adapter - is done by the same exe,
started elevated through UAC in a headless worker mode (`--elevated-apply ...`, see `Services/ElevatedRequest.cs`). The worker accepts only allow-listed value names
below one NVIDIA display-adapter driver key, re-checks the key against the present adapters and reports through its exit code only (no files, no pipes).

## Releases (GitHub Actions)

[`.github/workflows/release.yml`](../.github/workflows/release.yml) runs **only when the branch `release` is pushed** - there is no build for any other
branch, for pull requests, tags or manual runs. It runs the tests, publishes the NativeAOT build (`win-x64`), uploads the zip as a workflow artifact and
creates the GitHub release `v<major>.<minor>.<build>`.

**Versioning:** `major.minor` are maintained in `<Version>` of `NvidiaGsyncTweaks.csproj`. The last digit is the **build number**, which the pipeline sets to its
workflow run number - every push to `release` automatically produces a higher version (`1.0.1`, `1.0.2`, ...) and nothing is committed back to the repository.
(Failed runs also use up a number, so gaps are normal; re-running a failed run keeps its number and replaces the assets of that release.)
The version is baked into the exe (`-p:Version`) and shown in the title bar of the app (`v1.0.57`); a local build keeps build number 0 and shows `v1.0.0 (local build)`.

To release: merge into `release` and push. To start a new minor / major version, change `major.minor` in the csproj.
