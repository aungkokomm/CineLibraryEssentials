namespace CineLibraryEssentials.Models;

/// <summary>
/// Kodi/MediaElch-compatible &lt;fileinfo&gt;&lt;streamdetails&gt; payload — populated
/// by MediaProbeService from the actual video file at scrape time. Fields are
/// optional; any empty/zero value is omitted from the NFO output.
/// </summary>
public class StreamDetails
{
    public VideoStream Video { get; set; } = new();
    public List<AudioStream> AudioTracks { get; set; } = new();
    public List<SubtitleStream> SubtitleTracks { get; set; } = new();
}

public class VideoStream
{
    /// <summary>Video codec id (h264, hevc, av1, vp9, mpeg4, …).</summary>
    public string Codec { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public int DurationInSeconds { get; set; }
    /// <summary>Display aspect ratio as a decimal (1.78, 2.39, …).</summary>
    public string Aspect { get; set; } = string.Empty;
}

public class AudioStream
{
    public string Codec { get; set; } = string.Empty;
    /// <summary>ISO 639-2 (3-letter) language code, e.g. "eng", "hin", "spa".</summary>
    public string Language { get; set; } = string.Empty;
    public int Channels { get; set; }
}

public class SubtitleStream
{
    /// <summary>ISO 639-2 (3-letter) language code, or empty for unknown.</summary>
    public string Language { get; set; } = string.Empty;
}
