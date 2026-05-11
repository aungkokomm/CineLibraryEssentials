using System.Diagnostics;
using System.Text.RegularExpressions;

namespace CineLibraryEssentials.Services;

/// <summary>
/// Cleans embedded container metadata. For .mkv files uses bundled mkvpropedit
/// (in-place edit, only the metadata block is rewritten — never touches the
/// video/audio/subtitle data). For other formats falls back to TagLib#.
/// </summary>
public class MetadataCleanerService
{
    public class CleanResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public bool ChangedAnything { get; set; }
        public string? Engine { get; set; }
    }

    public static string? BundledMkvPropEditPath
    {
        get
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Tools", "MKVToolNix", "mkvpropedit.exe");
            return File.Exists(path) ? path : null;
        }
    }

    /// <summary>
    /// Writes a line to %AppData%/CineLibraryEssentials/metadata-clean.log so failures
    /// are visible after the fact (Debug.WriteLine is invisible to end users).
    /// </summary>
    private static void Log(string message)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "CineLibraryEssentials");
            Directory.CreateDirectory(dir);
            var logPath = Path.Combine(dir, "metadata-clean.log");
            File.AppendAllText(logPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch { /* logging must never throw */ }
    }

    public CleanResult Clean(string filePath, string newTitle)
    {
        var result = new CleanResult();
        Log($"Clean() called: file='{filePath}', newTitle='{newTitle}'");

        if (!File.Exists(filePath))
        {
            result.Error = "File not found";
            Log($"  -> File not found, aborting");
            return result;
        }

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        Log($"  -> ext='{ext}', bundledMkvPropEdit='{BundledMkvPropEditPath ?? "<missing>"}'");

        if (ext == ".mkv" && BundledMkvPropEditPath != null)
            return CleanMkvWithPropEdit(filePath, newTitle, BundledMkvPropEditPath);

        return CleanWithTagLib(filePath, newTitle);
    }

    // -----------------------------------------------------------------
    //  MKV path: mkvpropedit
    // -----------------------------------------------------------------

    private static CleanResult CleanMkvWithPropEdit(string filePath, string newTitle, string mkvPropEditExe)
    {
        var result = new CleanResult { Engine = "mkvpropedit" };

        // Probe the file structure so we know how many tracks/attachments to clean.
        // Falls back to safe defaults if the probe fails.
        var (trackCount, attachmentCount) = ProbeMkvStructure(filePath);
        Log($"  -> mkvpropedit path: tracks={trackCount}, attachments={attachmentCount}");

        // ---- Pass 1: clear ALL SimpleTags + set segment Title in one shot ----
        // No manual quoting in args — ProcessStartInfo.ArgumentList handles
        // escaping itself, and adding quotes makes mkvpropedit see literal
        // quotes inside the path string and fail with "file not found".
        var pass1 = new List<string>
        {
            filePath,
            "--tags", "all:"
        };
        if (!string.IsNullOrWhiteSpace(newTitle))
        {
            pass1.Add("--edit"); pass1.Add("info");
            pass1.Add("--set"); pass1.Add($"title={newTitle}");
        }

        var pass1Result = RunMkvPropEdit(mkvPropEditExe, pass1);
        Log($"  -> Pass 1 (tags+title): success={pass1Result.success}, error={pass1Result.error ?? "<none>"}");
        if (!pass1Result.success)
        {
            result.Success = false;
            result.Error = $"tags+title pass: {pass1Result.error}";
            return result;
        }

        // ---- Pass 2: clear track names (one --edit per track) ----
        // Done in a single batched call. Tracks are 1-indexed globally regardless of type.
        if (trackCount > 0)
        {
            var pass2 = new List<string> { filePath };
            for (int i = 1; i <= trackCount; i++)
            {
                pass2.Add("--edit");
                pass2.Add($"track:{i}");
                pass2.Add("--delete");
                pass2.Add("name");
            }
            var pass2Result = RunMkvPropEdit(mkvPropEditExe, pass2);
            Log($"  -> Pass 2 (track names): success={pass2Result.success}, error={pass2Result.error ?? "<none>"}");
            if (!pass2Result.success)
                Debug.WriteLine($"track-names pass: {pass2Result.error}");
        }

        // ---- Pass 3: delete attachments (logo images, fonts, anything) ----
        // We delete in REVERSE order because mkvpropedit re-numbers attachments
        // after each delete. Going N..1 keeps the indices stable.
        if (attachmentCount > 0)
        {
            var pass3 = new List<string> { filePath };
            for (int i = attachmentCount; i >= 1; i--)
            {
                pass3.Add("--delete-attachment");
                pass3.Add(i.ToString());
            }
            var pass3Result = RunMkvPropEdit(mkvPropEditExe, pass3);
            Log($"  -> Pass 3 (attachments): success={pass3Result.success}, error={pass3Result.error ?? "<none>"}");
            if (!pass3Result.success)
                Debug.WriteLine($"attachment-delete pass: {pass3Result.error}");
        }

        Log($"  -> mkvpropedit DONE for '{filePath}'");
        result.Success = true;
        result.ChangedAnything = true;
        return result;
    }

    /// <summary>
    /// Runs mkvpropedit with the given args and returns success/error.
    /// Exit codes: 0 = OK, 1 = OK with warnings, 2+ = error.
    /// </summary>
    private static (bool success, string? error) RunMkvPropEdit(string exe, List<string> args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);

            using var proc = Process.Start(psi);
            if (proc == null) return (false, "Could not start mkvpropedit");

            var stderr = proc.StandardError.ReadToEnd();
            var stdout = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();

            if (proc.ExitCode == 0 || proc.ExitCode == 1)
                return (true, null);

            var msg = !string.IsNullOrWhiteSpace(stderr)
                ? stderr.Trim()
                : !string.IsNullOrWhiteSpace(stdout)
                    ? stdout.Trim()
                    : $"exit code {proc.ExitCode}";
            return (false, msg);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Returns track + attachment counts via TagLib# (uses reflection so it tolerates
    /// minor version differences in TagLib.Matroska.File property surface).
    /// Returns (0, 0) if probing fails — the caller will skip those passes safely.
    /// </summary>
    private static (int tracks, int attachments) ProbeMkvStructure(string filePath)
    {
        try
        {
            using var file = TagLib.File.Create(filePath);

            int tracks = 0, attachments = 0;

            // Try TagLib.Matroska.File specifically
            var t = file.GetType();
            var tracksProp = t.GetProperty("Tracks");
            if (tracksProp?.GetValue(file) is System.Collections.IEnumerable tList)
                tracks = tList.Cast<object>().Count();

            // Fallback for tracks: Properties.Codecs.Count
            if (tracks == 0)
                tracks = file.Properties?.Codecs?.Count() ?? 0;

            var attachProp = t.GetProperty("Attachments");
            if (attachProp?.GetValue(file) is System.Collections.IEnumerable aList)
                attachments = aList.Cast<object>().Count();

            return (tracks, attachments);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ProbeMkvStructure failed: {ex.Message}");
            return (0, 0);
        }
    }

    // -----------------------------------------------------------------
    //  Non-MKV path: TagLib# (MP4, AVI, MOV, etc.)
    // -----------------------------------------------------------------

    private static CleanResult CleanWithTagLib(string filePath, string newTitle)
    {
        var result = new CleanResult { Engine = "taglib" };

        try
        {
            using var file = TagLib.File.Create(filePath);
            bool changed = false;

            var tag = file.Tag;
            if (tag != null)
            {
                if (!string.IsNullOrWhiteSpace(newTitle) &&
                    !string.Equals(tag.Title, newTitle, StringComparison.Ordinal))
                {
                    tag.Title = newTitle;
                    changed = true;
                }

                changed |= ClearField(() => tag.Album, v => tag.Album = v);
                changed |= ClearField(() => tag.Comment, v => tag.Comment = v);
                changed |= ClearField(() => tag.Description, v => tag.Description = v);
                changed |= ClearField(() => tag.Copyright, v => tag.Copyright = v);
                changed |= ClearField(() => tag.Publisher, v => tag.Publisher = v);
            }

            if (changed)
            {
                file.Save();
                result.ChangedAnything = true;
            }
            result.Success = true;
        }
        catch (TagLib.UnsupportedFormatException)
        {
            result.Engine = "skipped";
            result.Success = true;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
        }

        return result;
    }

    private static bool ClearField(Func<string?> getter, Action<string> setter)
    {
        try
        {
            if (!string.IsNullOrEmpty(getter()))
            {
                setter(string.Empty);
                return true;
            }
        }
        catch { }
        return false;
    }
}
