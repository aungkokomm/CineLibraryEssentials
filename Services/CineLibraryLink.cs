using System.Diagnostics;
using Microsoft.Win32;

namespace CineLibraryEssentials.Services;

/// <summary>
/// "Scan in CineLibrary": tells CineLibrary to read the folders Step 3 just
/// scraped, so they show up there without a manual refresh. It leaves a text
/// file (one folder per line) in CineLibrary's own CineLibrary-Data\requests,
/// which CineLibrary 5.4.1 and later picks up, and starts CineLibrary when it
/// isn't running. Nothing is registered anywhere.
/// </summary>
public static class CineLibraryLink
{
    // Where CineLibrary's installer records the folder it installed to
    private const string UninstallKey =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{8B7F4E2A-CF9E-4A1C-B6D2-CINE1000LIBRARY}_is1";

    /// <summary>The first CineLibrary that reads these requests.</summary>
    public static readonly Version FirstVersion = new(5, 4, 1);

    /// <summary>CineLibrary.exe: where you showed Essentials it is, else where its installer put it. Null when not found.</summary>
    public static string? FindExe(ConfigService config)
    {
        var chosen = config.GetCineLibraryExe();
        if (!string.IsNullOrEmpty(chosen) && File.Exists(chosen)) return chosen;
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            try
            {
                using var key = hive.OpenSubKey(UninstallKey);
                if (key?.GetValue("InstallLocation") is string dir)
                {
                    var exe = Path.Combine(dir, "CineLibrary.exe");
                    if (File.Exists(exe)) return exe;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Reading CineLibrary's install folder failed: {ex.Message}");
            }
        }
        return null;
    }

    /// <summary>The version of that CineLibrary.exe, or null when it can't be read.</summary>
    public static Version? VersionOf(string exe)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(exe);
            return new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Leaves the request for CineLibrary and starts it when it isn't running.
    /// Returns true when it was running already (it picks the request up by itself).
    /// </summary>
    public static bool RequestScan(string exe, IReadOnlyCollection<string> folders)
    {
        var requests = RequestsFolder(exe);
        Directory.CreateDirectory(requests);
        var name = $"scan-{DateTime.Now:yyyyMMdd-HHmmss-fff}";
        // Written whole first, then renamed, so CineLibrary never reads half a file
        var tmp = Path.Combine(requests, name + ".tmp");
        File.WriteAllLines(tmp, folders);
        File.Move(tmp, Path.Combine(requests, name + ".txt"));

        if (IsRunning(exe)) return true;
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe)! });
        return false;
    }

    /// <summary>CineLibrary keeps its data next to its exe.</summary>
    internal static string RequestsFolder(string exe)
        => Path.Combine(Path.GetDirectoryName(exe)!, "CineLibrary-Data", "requests");

    private static bool IsRunning(string exe)
    {
        foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)))
        {
            try
            {
                if (string.Equals(p.MainModule?.FileName, exe, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch
            {
                return true;   // can't tell which copy it is: one is running
            }
            finally
            {
                p.Dispose();
            }
        }
        return false;
    }
}
