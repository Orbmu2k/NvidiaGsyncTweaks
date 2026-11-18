using System.Reflection;

namespace NvidiaGsyncTweaks.Services;

/// <summary>
/// Version of the running app. Release builds get <c>major.minor.buildnumber</c> from the GitHub pipeline
/// (major.minor from the csproj, build number = workflow run number); a local build keeps the csproj version, whose last digit is 0.
/// </summary>
internal static class AppInfo
{
    /// <summary>"1.0.57"</summary>
    public static string Version { get; } = Format(typeof(AppInfo).Assembly.GetName().Version);

    /// <summary>A local / development build: the pipeline never produces build number 0.</summary>
    public static bool IsLocalBuild { get; } = IsLocal(typeof(AppInfo).Assembly.GetName().Version);

    /// <summary>"v1.0.57" or "v1.0.0 (local build)"</summary>
    public static string Display => IsLocalBuild ? $"v{Version} (local build)" : $"v{Version}";

    public static string Format(Version? v) => v is null ? "0.0.0" : $"{v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}";

    public static bool IsLocal(Version? v) => v is null || Math.Max(v.Build, 0) == 0;
}
