using System.Xml.Linq;
using CineLibraryEssentials.Models;

namespace CineLibraryEssentials.Services;

public class NfoGeneratorService
{
    public string GenerateNfoXml(MovieMetadata metadata, string movieFolderPath)
    {
        var nfo = new XDocument(
            new XElement("movie",
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
                GenerateCastElements(metadata.Cast),
                new XElement("poster", $"{Path.GetFileNameWithoutExtension(Path.GetFileName(movieFolderPath))}-poster.jpg"),
                new XElement("fanart", $"{Path.GetFileNameWithoutExtension(Path.GetFileName(movieFolderPath))}-fanart.jpg")
            )
        );

        return nfo.ToString();
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

    private XElement GenerateCastElements(List<CastMember> cast)
    {
        var actorElement = new XElement("actor");

        foreach (var member in cast.Take(10)) // Limit to top 10
        {
            actorElement.Add(
                new XElement("name", member.Name),
                new XElement("role", member.Character),
                new XElement("order", cast.IndexOf(member) + 1)
            );
        }

        return actorElement;
    }
}
