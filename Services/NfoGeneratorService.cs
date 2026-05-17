using System.Xml.Linq;
using CineLibraryEssentials.Models;

namespace CineLibraryEssentials.Services;

public class NfoGeneratorService
{
    public string GenerateNfoXml(MovieMetadata metadata, string movieFolderPath)
    {
        var folderBase = Path.GetFileNameWithoutExtension(Path.GetFileName(movieFolderPath));

        // <movie> root must contain SIBLING <actor> elements (one per cast member),
        // not a single <actor> with many child <name>/<role> entries. Plex/Kodi/
        // Jellyfin/MediaElch all parse the sibling form — the wrapped form is what
        // made libraries show "1 actor" no matter how many we scraped.
        var root = new XElement("movie",
            new XElement("title", metadata.Title),
            new XElement("originaltitle", metadata.Title),
            new XElement("year", metadata.Year),
            new XElement("released", metadata.ReleaseDate),
            new XElement("plot", metadata.Overview),
            new XElement("runtime", metadata.Runtime),
            new XElement("rating", metadata.Rating.ToString("F1")),
            new XElement("votes", ""),
            new XElement("tmdbid", metadata.TmdbId),
            GenerateGenresElement(metadata.Genres),
            new XElement("poster", $"{folderBase}-poster.jpg"),
            new XElement("fanart", $"{folderBase}-fanart.jpg")
        );

        // Append each actor as its own sibling element under <movie>.
        foreach (var actorElement in BuildActorElements(metadata.Cast))
            root.Add(actorElement);

        return new XDocument(root).ToString();
    }

    public void SaveNfoFile(MovieMetadata metadata, string movieFolderPath)
    {
        try
        {
            var nfoContent = GenerateNfoXml(metadata, movieFolderPath);
            var nfoFileName = $"{Path.GetFileNameWithoutExtension(Path.GetFileName(movieFolderPath))}.nfo";
            var nfoPath = Path.Combine(movieFolderPath, nfoFileName);

            File.WriteAllText(nfoPath, nfoContent);
            System.Diagnostics.Debug.WriteLine($"NFO file saved: {nfoPath}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving NFO file: {ex.Message}");
        }
    }

    private XElement GenerateGenresElement(List<GenreInfo> genres)
    {
        var genresElement = new XElement("genres");

        foreach (var genre in genres)
        {
            genresElement.Add(new XElement("genre", genre.Name));
        }

        return genresElement;
    }

    /// <summary>
    /// Builds a list of well-formed &lt;actor&gt; XML elements — one per cast member
    /// — including the &lt;thumb&gt; ref that points to .actors/Firstname_Lastname.jpg.
    /// Plex/Kodi/Jellyfin all use the &lt;thumb&gt; to link the photo file.
    /// </summary>
    private IEnumerable<XElement> BuildActorElements(List<CastMember> cast)
    {
        for (int i = 0; i < cast.Count; i++)
        {
            var member = cast[i];
            var actor = new XElement("actor",
                new XElement("name", member.Name),
                new XElement("role", member.Character),
                new XElement("order", i)
            );

            // Local <thumb> reference for the photo on disk (when one exists).
            if (!string.IsNullOrEmpty(member.ProfilePath))
            {
                var fileName = ImageDownloadService.BuildActorFileName(member.Name);
                actor.Add(new XElement("thumb", $".actors/{fileName}"));
            }

            yield return actor;
        }
    }
}
