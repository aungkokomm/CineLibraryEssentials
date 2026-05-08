namespace CineLibraryEssentials.Services;

/// <summary>
/// Tracks the most-recent rename batch so the user can undo it. We keep only the
/// latest batch (the toast shows for ~30s, after which we discard it).
/// </summary>
public class UndoService
{
    public record RenameRecord(string OldPath, string NewPath, bool IsDirectory);

    private readonly object _lock = new();
    private List<RenameRecord>? _lastBatch;

    public bool HasUndo
    {
        get { lock (_lock) return _lastBatch != null && _lastBatch.Count > 0; }
    }

    public void Push(IEnumerable<RenameRecord> batch)
    {
        lock (_lock) _lastBatch = batch.ToList();
    }

    public void Clear()
    {
        lock (_lock) _lastBatch = null;
    }

    /// <summary>
    /// Reverses the last batch. Files first, then folders (in reverse order).
    /// Returns count of successful + failed reversals.
    /// </summary>
    public (int succeeded, int failed) UndoLast()
    {
        List<RenameRecord>? batch;
        lock (_lock)
        {
            batch = _lastBatch;
            _lastBatch = null;
        }

        if (batch == null || batch.Count == 0) return (0, 0);

        int ok = 0, bad = 0;

        // Reverse files first (folder renames last so paths still resolve)
        var files = batch.Where(r => !r.IsDirectory).ToList();
        var dirs = batch.Where(r => r.IsDirectory).Reverse().ToList();

        foreach (var f in files)
        {
            try
            {
                if (File.Exists(f.NewPath) && !File.Exists(f.OldPath))
                {
                    File.Move(f.NewPath, f.OldPath);
                    ok++;
                }
            }
            catch { bad++; }
        }

        foreach (var d in dirs)
        {
            try
            {
                if (Directory.Exists(d.NewPath) && !Directory.Exists(d.OldPath))
                {
                    Directory.Move(d.NewPath, d.OldPath);
                    ok++;
                }
            }
            catch { bad++; }
        }

        return (ok, bad);
    }
}
