namespace CineLibraryEssentials.Services;

/// <summary>
/// Cleans embedded container metadata (the "title" tag VLC shows in its title bar,
/// plus comment/description/encoder fields that often leak tracker URLs and release
/// group signatures). Backed by TagLib#.
/// </summary>
public class MetadataCleanerService
{
    public class CleanResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public bool ChangedAnything { get; set; }
    }

    /// <summary>
    /// Sets the file's container "Title" tag to <paramref name="newTitle"/> and clears
    /// junk fields (Comment, Description, Encoder, Copyright, Publisher).
    /// Safe no-op if the file format isn't supported by TagLib#.
    /// </summary>
    public CleanResult Clean(string filePath, string newTitle)
    {
        var result = new CleanResult();

        if (!File.Exists(filePath))
        {
            result.Error = "File not found";
            return result;
        }

        try
        {
            using var file = TagLib.File.Create(filePath);
            var tag = file.Tag;
            if (tag == null)
            {
                result.Success = true;
                return result;
            }

            bool changed = false;

            if (!string.IsNullOrWhiteSpace(newTitle) &&
                !string.Equals(tag.Title, newTitle, StringComparison.Ordinal))
            {
                tag.Title = newTitle;
                changed = true;
            }

            // Clear junk fields (not all containers store all of these — TagLib will ignore
            // the ones it doesn't support)
            if (!string.IsNullOrEmpty(tag.Comment)) { tag.Comment = string.Empty; changed = true; }
            if (!string.IsNullOrEmpty(tag.Description)) { tag.Description = string.Empty; changed = true; }
            if (!string.IsNullOrEmpty(tag.Copyright)) { tag.Copyright = string.Empty; changed = true; }
            if (!string.IsNullOrEmpty(tag.Publisher)) { tag.Publisher = string.Empty; changed = true; }
            if (tag.Performers != null && tag.Performers.Length > 0)
            {
                // We don't blindly nuke performers — keep them, scrubbing only happens
                // for free-form noise fields above.
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
            // Format not supported — silently succeed (e.g. some obscure containers)
            result.Success = true;
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            result.Success = false;
        }

        return result;
    }
}
