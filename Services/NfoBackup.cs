using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CineLibraryEssentials.Services;

/// <summary>
/// "Restore previous version" for the NFO editor and bulk edit. Before a save
/// changes anything, every file it will touch (the NFO, a replaced poster,
/// episode NFOs...) is copied into one backup set for that NFO, kept in the
/// app's local data folder so the movie library itself stays clean. Restore
/// puts the files back, and deletes files the save created. One set per NFO:
/// the newest save replaces the previous backup.
/// </summary>
public static class NfoBackup
{
    /// <summary>Where backup sets live (tests point it at a temp folder).</summary>
    public static string Root { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CineLibraryEssentials", "Backups");

    private record Entry(string Path, string? Copy);

    /// <summary>Collects the files one save is about to change.</summary>
    public sealed class Set
    {
        private readonly string _dir;
        private readonly List<Entry> _entries = new();
        private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);

        internal Set(string dir) => _dir = dir;

        /// <summary>Remembers a file before it's overwritten (or notes it didn't exist yet).</summary>
        public void Add(string path)
        {
            var full = System.IO.Path.GetFullPath(path);
            if (!_seen.Add(full)) return;
            string? copy = null;
            if (File.Exists(full))
            {
                copy = $"{_entries.Count}.bak";
                File.Copy(full, System.IO.Path.Combine(_dir, copy), overwrite: true);
            }
            _entries.Add(new Entry(full, copy));
            File.WriteAllText(System.IO.Path.Combine(_dir, "manifest.json"), JsonSerializer.Serialize(_entries));
        }
    }

    /// <summary>Starts a new backup set for this NFO, replacing the previous one.</summary>
    public static Set Begin(string nfoPath)
    {
        var dir = DirFor(nfoPath);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "when.txt"), DateTime.Now.ToString("O"));
        return new Set(dir);
    }

    /// <summary>When the restorable version was saved, or null when there is none.</summary>
    public static DateTime? BackupTime(string nfoPath)
    {
        var dir = DirFor(nfoPath);
        var when = Path.Combine(dir, "when.txt");
        if (!File.Exists(Path.Combine(dir, "manifest.json")) || !File.Exists(when)) return null;
        return DateTime.TryParse(File.ReadAllText(when), null, System.Globalization.DateTimeStyles.RoundtripKind, out var t) ? t : null;
    }

    /// <summary>
    /// Puts back every file of the last save and removes the backup. Returns
    /// how many files were restored or removed.
    /// </summary>
    public static int Restore(string nfoPath)
    {
        var dir = DirFor(nfoPath);
        var manifest = Path.Combine(dir, "manifest.json");
        if (!File.Exists(manifest)) return 0;

        var entries = JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(manifest)) ?? new();
        int n = 0;
        foreach (var e in entries)
        {
            if (e.Copy != null)
            {
                File.Copy(Path.Combine(dir, e.Copy), e.Path, overwrite: true);
                n++;
            }
            else if (File.Exists(e.Path))
            {
                File.Delete(e.Path);   // the save created it
                n++;
            }
        }
        Directory.Delete(dir, recursive: true);
        return n;
    }

    private static string DirFor(string nfoPath)
    {
        var key = Path.GetFullPath(nfoPath).ToLowerInvariant();
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(key)))[..16];
        return Path.Combine(Root, hash);
    }
}
