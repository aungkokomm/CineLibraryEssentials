namespace CineLibraryEssentials.Services;

/// <summary>
/// Bulk edit: applies one <see cref="NfoEditor.BulkChange"/> to the main NFO of
/// many folders. Each NFO that changes is backed up first, so "Restore previous
/// version" in Edit NFO can undo it per title.
/// </summary>
public static class BulkNfoEdit
{
    public record Result(int Updated, int Unchanged, int NoNfo, int Failed);

    public static Result Apply(IEnumerable<string> folders, NfoEditor.BulkChange change)
    {
        int updated = 0, unchanged = 0, noNfo = 0, failed = 0;
        foreach (var folder in folders.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var nfo = NfoLocator.FindMainNfo(folder);
            if (nfo == null) { noNfo++; continue; }
            try
            {
                var doc = NfoFile.Load(nfo);
                var root = doc.Root!;
                var before = NfoEditor.Read(root);
                var after = NfoEditor.ApplyBulk(before, change);
                if (!NfoEditor.Apply(root, before, after, change.Lock ?? NfoEditor.IsLocked(root)))
                {
                    unchanged++;
                    continue;
                }
                NfoBackup.Begin(nfo).Add(nfo);   // copies the file as it is on disk, before saving
                NfoFile.Save(doc, nfo);
                updated++;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Bulk edit failed for {nfo}: {ex.Message}");
                failed++;
            }
        }
        return new Result(updated, unchanged, noNfo, failed);
    }
}
