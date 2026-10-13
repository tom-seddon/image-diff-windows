using System.Reflection;

namespace ImageDiff.Core;

public static class AppVersion
{
    /// <summary>The running app's version, e.g. "1.2.3". Release builds get it from the git tag.</summary>
    public static string Current { get; } = Read();

    private static string Read()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(AppVersion).Assembly;
        string? version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrEmpty(version))
            return assembly.GetName().Version?.ToString(3) ?? "unknown";

        // The SDK appends "+<commit hash>"; drop it.
        int plus = version.IndexOf('+');
        return plus >= 0 ? version[..plus] : version;
    }
}
