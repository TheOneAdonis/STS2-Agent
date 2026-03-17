using System.Diagnostics;
using System.Reflection;
using MegaCrit.Sts2.Core.Logging;

namespace STS2AIAgent.Desktop;

internal static class DesktopWindowLauncher
{
    private const string LogPrefix = "[STS2AIAgent.Desktop]";
    private const string DesktopExeName = "CreativeAI.Desktop.exe";
    private static int _launchAttempted;

    public static void TryLaunch()
    {
        if (Interlocked.Exchange(ref _launchAttempted, 1) != 0)
        {
            return;
        }

        try
        {
            var exePath = ResolveDesktopExePath();
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            {
                Log.Warn($"{LogPrefix} Desktop UI executable not found at '{exePath}'.");
                return;
            }

            var processName = Path.GetFileNameWithoutExtension(exePath);
            if (Process.GetProcessesByName(processName).Any())
            {
                Log.Info($"{LogPrefix} Desktop UI already running.");
                return;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory,
                UseShellExecute = true
            };

            Process.Start(startInfo);
            Log.Info($"{LogPrefix} Launched desktop UI: {exePath}");
        }
        catch (Exception ex)
        {
            Log.Error($"{LogPrefix} Failed to launch desktop UI: {ex}");
        }
    }

    private static string ResolveDesktopExePath()
    {
        var assemblyPath = Assembly.GetExecutingAssembly().Location;
        var modDir = Path.GetDirectoryName(assemblyPath) ?? AppContext.BaseDirectory;
        return Path.Combine(modDir, "desktop", DesktopExeName);
    }
}
